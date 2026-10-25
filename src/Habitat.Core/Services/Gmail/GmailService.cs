using Habitat.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Habitat.Services;

internal sealed class GmailService : IDisposable
{
    private const string Api = "https://gmail.googleapis.com/gmail/v1/users/me/";
    private readonly GmailAuthService _auth;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public GmailService(GmailAuthService auth, HttpClient? http = null)
    {
        _auth = auth;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        _ownsClient = http is null;
    }

    public async Task<GmailInbox> GetUnreadAsync(bool inboxOnly, string? pageToken, CancellationToken cancellationToken)
    {
        var query = inboxOnly ? "in:inbox is:unread" : "is:unread";
        var url = Api + "messages?maxResults=25&q=" + Uri.EscapeDataString(query);
        if (pageToken is not null) url += "&pageToken=" + Uri.EscapeDataString(pageToken);
        using var list = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        var root = list.RootElement;
        var ids = root.TryGetProperty("messages", out var messages)
            ? messages.EnumerateArray().Select(message => message.GetProperty("id").GetString()!).ToArray() : [];
        if (ids.Length > 25) throw new InvalidDataException(UiText.Get("GmailPageSizeUnexpected"));
        using var parallel = new SemaphoreSlim(4);
        var results = await Task.WhenAll(ids.Select(async id =>
        {
            await parallel.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var data = await GetJsonAsync(MessageUrl(id) + "?format=metadata&metadataHeaders=From&metadataHeaders=Subject&metadataHeaders=Date&metadataHeaders=Message-ID",
                    cancellationToken).ConfigureAwait(false);
                return GmailMessageParser.Parse(data.RootElement, false);
            }
            finally { parallel.Release(); }
        })).ConfigureAwait(false);
        var count = root.TryGetProperty("resultSizeEstimate", out var estimate) ? estimate.GetInt32() : results.Length;
        if (inboxOnly)
        {
            using var label = await GetJsonAsync(Api + "labels/INBOX", cancellationToken).ConfigureAwait(false);
            count = label.RootElement.GetProperty("messagesUnread").GetInt32();
        }
        return new GmailInbox(results.OrderByDescending(message => message.Date).ToArray(), count, !inboxOnly,
            root.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null, DateTimeOffset.UtcNow);
    }

    public async Task<GmailMessage> GetMessageAsync(string id, CancellationToken cancellationToken)
    {
        using var data = await GetJsonAsync(MessageUrl(id) + "?format=full", cancellationToken).ConfigureAwait(false);
        return GmailMessageParser.Parse(data.RootElement, true);
    }

    private static string MessageUrl(string id)
    {
        if (!Regex.IsMatch(id, @"^[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant)) throw new InvalidDataException(UiText.Get("GmailMessageIdentifierInvalid"));
        return Api + "messages/" + id;
    }

    public async Task<GmailDraft> CreateDraftAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException(UiText.Get("WriteAnEmailBeforeSavingADraft"), nameof(text));
        if (!_auth.IsSignedIn) throw new GmailDraftAuthorizationException(UiText.Get("AuthorizeGmailDraftsThenPressDraftEmailAgain"));
        var subject = GmailDraft.SubjectFrom(text);
        var messageId = Guid.NewGuid().ToString("N") + "@habitat.local";
        var raw = GmailDraft.EncodeMessage(text, subject, messageId);
        for (var attempt = 0; ; attempt++)
        {
            var token = await _auth.GetAccessTokenAsync(cancellationToken, attempt > 0).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Post, Api + "drafts")
            { Content = JsonContent.Create(new { message = new { raw } }) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) continue;
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new GmailDraftAuthorizationException(UiText.Get("AuthorizeGmailDraftsThenPressDraftEmailAgain"));
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (error.Contains("insufficientPermissions", StringComparison.OrdinalIgnoreCase) ||
                    error.Contains("ACCESS_TOKEN_SCOPE_INSUFFICIENT", StringComparison.OrdinalIgnoreCase))
                    throw new GmailDraftAuthorizationException(UiText.Get("AuthorizeGmailDraftsThenPressDraftEmailAgain"));
                throw new InvalidOperationException(UiText.Get("GoogleDeniedDraftCreationCheckGmailAPIAccess"));
            }
            // A POST may already have saved the draft. Never retry ambiguous failures automatically.
            response.EnsureSuccessStatusCode();
            using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var id = data.RootElement.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException(UiText.Get("GoogleDidNotConfirmTheSavedDraft"));
            return new GmailDraft(id, subject, messageId);
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        var refreshed = false;
        var forceRefresh = false;
        for (var attempt = 0; ; attempt++)
        {
            var token = await _auth.GetAccessTokenAsync(cancellationToken, forceRefresh).ConfigureAwait(false);
            forceRefresh = false;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (!refreshed) { refreshed = true; forceRefresh = true; continue; }
                throw new GmailSignInRequiredException(UiText.Get("GoogleAuthorizationIsNoLongerValidSignIn"));
            }
            if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 2)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt + 1);
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 0.1, 5)), cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new InvalidOperationException(UiText.Get("GoogleDeniedGmailAccessCheckThatGmailAPI"));
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 6000000) throw new InvalidDataException(UiText.Get("ThisGmailMessageIsTooLargeToPreview"));
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > 6000000) throw new InvalidDataException(UiText.Get("ThisGmailMessageIsTooLargeToPreview"));
                buffer.Write(bytes, 0, read);
            }
            return JsonDocument.Parse(buffer.ToArray());
        }
    }

    public void Dispose() { if (_ownsClient) _http.Dispose(); }
}
