using Habitat.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Habitat.Services;

internal sealed class GmailAuthService : IDisposable
{
    internal const string ReadOnlyScope = "https://www.googleapis.com/auth/gmail.readonly";
    internal const string ComposeScope = "https://www.googleapis.com/auth/gmail.compose";
    internal const string ContactsScope = "https://www.googleapis.com/auth/contacts.readonly";
    internal const string EmailScope = "https://www.googleapis.com/auth/userinfo.email";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";
    private readonly IGmailCredentialStore _store;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _tokenLock = new(1);
    private readonly Func<string, Task> _openBrowser;
    private GmailAccount _account;
    private readonly bool _contactsOnly;

    public GmailAuthService(IGmailCredentialStore store, HttpClient? http = null, Func<string, Task>? openBrowser = null, bool contactsOnly = false)
    {
        _store = store;
        _contactsOnly = contactsOnly;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _ownsClient = http is null;
        _openBrowser = openBrowser ?? (url =>
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return Task.CompletedTask;
        });
        _account = store.Load();
    }

    public bool IsConfigured => _account.Client is not null;
    public bool IsSignedIn => _account.Tokens is not null;
    public string? Email => _account.Email;
    internal GmailClientConfiguration? Client => _account.Client;
    internal GmailAccount Account => _account;

    public async Task<bool> RestoreSavedSessionAsync(CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsSignedIn) return true;
            // A saved account may become available after startup (for example,
            // after authorization completed in another HABITAT setup process).
            var saved = _store.Load();
            if (saved.Client is not null) _account = saved;
            return IsSignedIn;
        }
        finally { _tokenLock.Release(); }
    }

    public void Configure(GmailClientConfiguration client)
    {
        if (_account.Client == client) return;
        var account = new GmailAccount(client, null, null);
        _store.Save(account);
        _account = account;
    }

    public async Task SignInAsync(CancellationToken cancellationToken, bool authorizeDrafts = false)
    {
        if (IsSignedIn && !authorizeDrafts)
        {
            try
            {
                await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (GmailSignInRequiredException) { /* Revoked consent requires a new browser sign-in. */ }
        }
        var client = _account.Client ?? throw new GmailSignInRequiredException(UiText.Get("GoogleSigninNeedsADesktopOAuthClientOpen"));
        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var verifier = RandomValue(48);
            var state = RandomValue(32);
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            await using var server = builder.Build();
            server.MapGet("/oauth/callback", (HttpRequest request) =>
            {
                var suppliedState = request.Query["state"].ToString();
                if (!FixedEquals(state, suppliedState)) return Results.Text(UiText.Get("GoogleInvalidSignIn"), statusCode: 400);
                if (request.Query.ContainsKey("error"))
                {
                    completion.TrySetException(new GmailSignInRequiredException(UiText.Get("GoogleSigninWasCancelledOrDenied")));
                    return Results.Text(UiText.Get("SigninCancelledYouCanReturnToHABITAT"));
                }
                var code = request.Query["code"].ToString();
                if (code.Length == 0 || code.Length > 8192) return Results.Text("Missing authorization code.", statusCode: 400);
                completion.TrySetResult(code);
                return Results.Content("<!doctype html><html lang='en'><meta charset='utf-8'><title>HABITAT</title>" +
                    "<body style='font:18px system-ui;padding:48px'>Google authorization received. Return to HABITAT to finish connecting.</body></html>", "text/html");
            });
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            await server.StartAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                var redirect = server.Urls.Single().TrimEnd('/') + "/oauth/callback";
                var authorizationUrl = AuthorizationUrl(client.ClientId, redirect, state, challenge, authorizeDrafts, _contactsOnly);
                await _openBrowser(authorizationUrl).ConfigureAwait(false);
                var code = await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                var tokens = await ExchangeAsync(new Dictionary<string, string>
                {
                    ["client_id"] = client.ClientId, ["client_secret"] = client.ClientSecret,
                    ["code"] = code, ["code_verifier"] = verifier, ["redirect_uri"] = redirect,
                    ["grant_type"] = "authorization_code"
                }, null, timeout.Token).ConfigureAwait(false);
                var email = await ReadProfileAsync(tokens.AccessToken, timeout.Token).ConfigureAwait(false);
                var account = new GmailAccount(client, tokens, email);
                _store.Save(account);
                _account = account;
            }
            finally
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await server.StopAsync(stop.Token).ConfigureAwait(false);
            }
        }
        finally { _tokenLock.Release(); }
    }

    internal static string AuthorizationUrl(string clientId, string redirect, string state, string challenge, bool authorizeDrafts = false, bool contactsOnly = false)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["redirect_uri"] = redirect, ["response_type"] = "code",
            ["scope"] = contactsOnly ? ContactsScope + " " + EmailScope : authorizeDrafts ? ReadOnlyScope + " " + ComposeScope : ReadOnlyScope,
            ["access_type"] = "offline", ["prompt"] = "consent select_account",
            ["state"] = state, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256"
        };
        return "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&", query.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tokens = _account.Tokens ?? throw new GmailSignInRequiredException(UiText.Get("SignInWithGoogleToSeeUnreadEmails"));
            if (!forceRefresh && tokens.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return tokens.AccessToken;
            var client = _account.Client ?? throw new GmailSignInRequiredException(UiText.Get("OpenGmailSetup"));
            try
            {
                tokens = await ExchangeAsync(new Dictionary<string, string>
                {
                    ["client_id"] = client.ClientId, ["client_secret"] = client.ClientSecret,
                    ["refresh_token"] = tokens.RefreshToken, ["grant_type"] = "refresh_token"
                }, tokens.RefreshToken, cancellationToken).ConfigureAwait(false);
            }
            catch (GmailSignInRequiredException)
            {
                var disconnected = _account with { Tokens = null, Email = null };
                _store.Save(disconnected);
                _account = disconnected;
                throw;
            }
            var account = _account with { Tokens = tokens };
            _store.Save(account);
            _account = account;
            return tokens.AccessToken;
        }
        finally { _tokenLock.Release(); }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Disconnect locally. Google consent is not changed implicitly.
            var account = _account with { Tokens = null, Email = null };
            _store.Save(account);
            _account = account;
        }
        finally { _tokenLock.Release(); }
    }

    private async Task<GmailTokens> ExchangeAsync(Dictionary<string, string> fields, string? refreshToken, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        using var response = await _http.PostAsync(TokenUrl, content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                var code = error.RootElement.TryGetProperty("error", out var errorValue) ? errorValue.GetString() : null;
                if (code == "invalid_grant")
                    throw new GmailSignInRequiredException(UiText.Get("GoogleAuthorizationExpiredOrWasRevokedSignIn"));
                if (code == "invalid_client")
                    throw new InvalidOperationException(UiText.Get("GoogleRejectedTheDesktopOAuthClientCheckGmail"));
            }
            response.EnsureSuccessStatusCode();
        }
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var root = document.RootElement;
        var access = root.GetProperty("access_token").GetString();
        if (root.TryGetProperty("scope", out var scopes))
        {
            var granted = (scopes.GetString() ?? "").Split(' ');
            if (!granted.Contains(_contactsOnly ? ContactsScope : ReadOnlyScope))
                throw new GmailSignInRequiredException(_contactsOnly ? "Google did not grant read-only contacts access. Sign in again and allow Contacts." : UiText.Get("GoogleDidNotGrantReadonlyGmailAccessSign"));
        }
        var refresh = root.TryGetProperty("refresh_token", out var value) ? value.GetString() : refreshToken;
        if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh))
            throw new GmailSignInRequiredException(UiText.Get("GoogleDidNotGrantOfflineReadAccessSign"));
        return new GmailTokens(access, refresh, DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()));
    }

    private async Task<string> ReadProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _contactsOnly
            ? "https://www.googleapis.com/oauth2/v2/userinfo" : "https://gmail.googleapis.com/gmail/v1/users/me/profile");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var email = document.RootElement.GetProperty(_contactsOnly ? "email" : "emailAddress").GetString();
        if (_contactsOnly && (!document.RootElement.TryGetProperty("verified_email", out var verified) || verified.ValueKind != JsonValueKind.True))
            throw new InvalidDataException("Google did not return a verified account email. Nothing was imported.");
        return !string.IsNullOrWhiteSpace(email) ? email : throw new InvalidDataException("Google returned no account email.");
    }

    private static bool FixedEquals(string expected, string supplied) => CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied));
    private static string RandomValue(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public void Dispose() { if (_ownsClient) _http.Dispose(); }
}
