using Habitat.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Habitat.Features.Contacts;

internal sealed class GoogleContactsService(GmailAuthService auth, HttpClient? http = null) : IDisposable
{
    private readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    private const string Fields = "names,nicknames,emailAddresses,phoneNumbers,addresses,organizations,biographies,birthdays,urls,photos,imClients,userDefined,relations,events,externalIds,metadata";

    // Full reads are deliberate, paginated and side-effect free. A partial read is
    // never committed, and stable resource names keep reloads idempotent.
    internal async Task<ContactCandidate[]> ReadAsync(CancellationToken token)
    {
        var result = new Dictionary<string, ContactCandidate>();
        string? page = null;
        var pages = new HashSet<string>();
        do
        {
            var uri = "https://people.googleapis.com/v1/people/me/connections?pageSize=1000&personFields=" + Uri.EscapeDataString(Fields) +
                (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page));
            using var response = await GetAsync(uri, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new InvalidOperationException("Google Contacts access is unavailable. Enable People API in your Google OAuth project and reconnect with read-only contacts permission.");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (json.Length > 16 * 1024 * 1024) throw new InvalidDataException("Google returned a contact page that is too large. Nothing was imported.");
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("connections", out var connections))
            foreach (var person in connections.EnumerateArray())
            {
                var candidate = Parse(person);
                if (!result.TryAdd(candidate.RemoteId, candidate))
                    throw new InvalidDataException("Google repeated a contact across pages. Nothing was imported; reload contacts to retry.");
                if (result.Count > 20000) throw new InvalidDataException("Import up to 20,000 Google contacts at a time.");
            }
            page = document.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
            if (page is not null && (page.Length > 8192 || !pages.Add(page)))
                throw new InvalidDataException("Google returned an invalid next page. Nothing was imported.");
        } while (!string.IsNullOrEmpty(page));
        return result.Values.ToArray();
    }

    private async Task<HttpResponseMessage> GetAsync(string uri, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await auth.GetAccessTokenAsync(token, attempt > 0).ConfigureAwait(false));
            var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Unauthorized || attempt != 0) return response;
            response.Dispose();
        }
    }

    internal static ContactCandidate Parse(JsonElement person)
    {
        var id = person.GetProperty("resourceName").GetString();
        if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("people/", StringComparison.Ordinal))
            throw new InvalidDataException("Google returned an invalid contact identifier.");
        var values = new List<ContactValue>();
        Add("names", "displayName", "name"); Add("nicknames", "value", "nickname");
        Add("emailAddresses", "value", "email"); Add("phoneNumbers", "value", "phone");
        Add("addresses", "formattedValue", "address"); Add("organizations", "name", "organization");
        Add("organizations", "title", "job"); Add("biographies", "value", "note");
        Add("urls", "value", "url"); Add("photos", "url", "photo"); Add("imClients", "username", "im");
        foreach (var key in new[] { "birthdays", "userDefined", "relations", "events", "externalIds" })
        {
            if (!person.TryGetProperty(key, out var items)) continue;
            foreach (var item in items.EnumerateArray()) values.Add(new(key == "birthdays" ? "birthday" : "other", item.GetRawText(), key));
        }
        if (values.Count == 0) values.Add(new("name", "Unnamed contact"));
        return new(id, values.Distinct().ToArray(), person.GetRawText());

        void Add(string array, string property, string kind)
        {
            if (!person.TryGetProperty(array, out var items)) return;
            foreach (var item in items.EnumerateArray())
            {
                var value = item.TryGetProperty(property, out var field) ? field.GetString() : null;
                var label = item.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "";
                if (kind == "im" && item.TryGetProperty("protocol", out var protocol)) label = protocol.GetString() ?? label;
                if (!string.IsNullOrWhiteSpace(value)) values.Add(new(kind, value, label));
            }
        }
    }

    public void Dispose() { if (http is null) _http.Dispose(); }
}
