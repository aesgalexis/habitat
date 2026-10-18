using Habitat.Models;
using Habitat.Services;
using Habitat;
using Microsoft.AspNetCore.WebUtilities;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var checks = 0;
void Check(bool success, string message)
{
    if (!success) throw new Exception(message);
    checks++;
}
async Task Reject<T>(Func<Task> action, string message) where T : Exception
{
    try { await action(); throw new Exception(message); }
    catch (T) { checks++; }
}
HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
{ Content = new StringContent(json, Encoding.UTF8, "application/json") };
string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
GmailMessage Parse(string json, bool body = true)
{
    using var document = JsonDocument.Parse(json);
    return GmailMessageParser.Parse(document.RootElement, body);
}
string Message(string mime = "text/plain", string text = "Hello, Mallorca 🌤", string id = "abc123") => JsonSerializer.Serialize(new
{
    id, threadId = "thread123", internalDate = "1791882000000", snippet = "Hello &amp; welcome",
    payload = new { mimeType = mime, headers = new[] { new { name = "From", value = "Sample <sample@example.com>" },
        new { name = "Subject", value = "=?UTF-8?B?SG9sYSDwn5GM?=" } }, body = new { data = Encode(text) } }
});
var clientConfig = GmailClientConfiguration.FromJson("{\"installed\":{\"client_id\":\"test.apps.googleusercontent.com\",\"client_secret\":\"test-only\",\"token_uri\":\"https://untrusted.invalid/token\"}}");
Check(clientConfig.ClientId == "test.apps.googleusercontent.com", "Only the desktop client configuration should be imported.");
await Reject<InvalidDataException>(() => Task.Run(() => GmailClientConfiguration.FromJson("{\"web\":{}}")), "Web clients must not be accepted.");
await Reject<InvalidDataException>(() => Task.Run(() => GmailClientConfiguration.FromJson("{\"installed\":{\"client_id\":\"attacker.invalid\",\"client_secret\":\"x\"}}")), "Reject invalid client ids.");
var authQuery = QueryHelpers.ParseQuery(new Uri(GmailAuthService.AuthorizationUrl(clientConfig.ClientId, "http://127.0.0.1:1234/oauth/callback", "state", "challenge")).Query);
Check(authQuery["scope"] == GmailAuthService.ReadOnlyScope && authQuery["code_challenge_method"] == "S256" && authQuery["access_type"] == "offline",
    "Request only read-only Gmail access with PKCE and offline refresh.");

var plain = Parse(Message());
Check(plain.Body == "Hello, Mallorca 🌤" && plain.Subject == "Hola 👌" && plain.Preview == "Hello & welcome", "Decode Unicode bodies, headers and snippets.");
Check(Parse(Message(), false).Body is null, "Metadata must not preload the message body.");
var linkedMessageJson = JsonNode.Parse(Message())!;
linkedMessageJson["payload"]!["headers"]!.AsArray().Add(new JsonObject { ["name"] = "Message-ID", ["value"] = "<unique+message@example.com>" });
var linkedMessage = Parse(linkedMessageJson.ToJsonString(), false);
Check(linkedMessage.RfcMessageId == "<unique+message@example.com>" && linkedMessage.Body is null,
    "Read the original Message-ID from metadata without downloading the body.");
var webLink = GmailWebLink.ForMessage("sample+alias@example.com", linkedMessage);
Check(webLink.Scheme == "https" && webLink.Host == "mail.google.com" &&
    QueryHelpers.ParseQuery(webLink.Query)["authuser"] == "sample+alias@example.com",
    "Gmail links target the connected account instead of assuming browser account zero.");
Check(Uri.UnescapeDataString(webLink.Fragment) == "#search/rfc822msgid:unique+message@example.com",
    "Locate the exact message by its RFC Message-ID, preserving plus characters.");
Check(GmailWebLink.ForMessage("sample@example.com", plain).Fragment == "#all/thread123",
    "Messages without a Message-ID fall back to their Gmail conversation.");
await Reject<InvalidDataException>(() => Task.Run(() => GmailWebLink.ForMessage("sample@example.com", plain with { ThreadId = "../inbox" })),
    "Reject malformed fallback conversation identifiers.");
Check(plain.Date == DateTimeOffset.FromUnixTimeMilliseconds(1791882000000), "Use Gmail's internal timestamp.");
Check(GmailMessageParser.DecodeHeader("=?UTF-8?Q?Buenos_d=C3=ADas?=") == "Buenos días", "Decode quoted-printable MIME headers.");
var html = Parse(Message("text/html", "<html><head><style>hidden</style></head><body><script>execute()</script><!-- tracking --><p>Hello &amp; welcome</p><img src='https://tracker.invalid/pixel'><p>Bye</p></body></html>"));
Check(html.Body == "Hello & welcome\nBye", "Never execute HTML or expose remote image content; convert it to plain text.");
var multipart = JsonNode.Parse(Message())!;
multipart["payload"]!["mimeType"] = "multipart/alternative";
multipart["payload"]!["body"] = null;
multipart["payload"]!["parts"] = new JsonArray(
    new JsonObject { ["mimeType"] = "text/html", ["body"] = new JsonObject { ["data"] = Encode("<p>HTML version</p>") } },
    new JsonObject { ["mimeType"] = "text/plain", ["body"] = new JsonObject { ["data"] = Encode("Plain version") } },
    new JsonObject { ["mimeType"] = "text/plain", ["filename"] = "attachment.txt", ["body"] = new JsonObject { ["data"] = Encode("Do not download me") } });
Check(Parse(multipart.ToJsonString()).Body == "Plain version", "Prefer text/plain and exclude attachments.");
var invalidBody = JsonNode.Parse(Message())!;
invalidBody["payload"]!["body"]!["data"] = "invalid%%%";
Check(Parse(invalidBody.ToJsonString()).Body!.Contains("No text body"), "Invalid MIME data must not crash the reader.");
Check(Parse(Message(text: new string('a', 180000))).Body!.EndsWith("[Message truncated]"), "Limit the rendered body size.");

var freshAccount = new GmailAccount(clientConfig, new GmailTokens("access-test", "refresh-test", DateTimeOffset.UtcNow.AddHours(1)), "sample@example.com");
var draftScopes = QueryHelpers.ParseQuery(new Uri(GmailAuthService.AuthorizationUrl(clientConfig.ClientId,
    "http://127.0.0.1:1234/oauth/callback", "state", "challenge", authorizeDrafts: true)).Query)["scope"].ToString().Split(' ');
Check(draftScopes.Contains(GmailAuthService.ReadOnlyScope) && draftScopes.Contains(GmailAuthService.ComposeScope),
    "Explicit draft authorization adds compose access and preserves reader access.");
var draftCalls = 0;
var draftStatus = HttpStatusCode.OK;
string? postedMime = null;
using var draftHttp = new HttpClient(new FakeHandler(async request =>
{
    draftCalls++;
    Check(request.Method == HttpMethod.Post && request.RequestUri!.AbsoluteUri == "https://gmail.googleapis.com/gmail/v1/users/me/drafts",
        "Saving uses only drafts.create, never a send endpoint.");
    Check(request.Headers.Authorization?.Parameter == "access-test", "Saving a draft uses the cached authorized account.");
    using var data = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
    var raw = data.RootElement.GetProperty("message").GetProperty("raw").GetString()!;
    Check(!raw.Contains('+') && !raw.Contains('/') && !raw.Contains('='), "Raw MIME uses unpadded base64url.");
    var padded = raw.Replace('-', '+').Replace('_', '/');
    postedMime = Encoding.UTF8.GetString(Convert.FromBase64String(padded.PadRight((padded.Length + 3) / 4 * 4, '=')));
    return draftStatus == HttpStatusCode.Forbidden
        ? Json("{\"error\":{\"errors\":[{\"reason\":\"insufficientPermissions\"}]}}", draftStatus)
        : Json("{\"id\":\"draft123\"}", draftStatus);
}));
using var draftAuth = new GmailAuthService(new MemoryStore(freshAccount), draftHttp, _ => throw new Exception("Saving must never open a browser."));
using var draftService = new GmailService(draftAuth, draftHttp);
const string draftBody = "  Buenos días ☕\r\nestas son las primeras ocho palabras y el resto del correo.  ";
var savedDraft = await draftService.CreateDraftAsync(draftBody, CancellationToken.None);
Check(savedDraft.Id == "draft123" && savedDraft.Subject == "Buenos días ☕ estas son las primeras ocho", "Use the first eight words as the subject.");
var mimeParts = postedMime!.Split("\r\n\r\n", 2);
Check(Encoding.UTF8.GetString(Convert.FromBase64String(mimeParts[1])) == draftBody &&
    !mimeParts[0].Contains("\r\nTo:") && !mimeParts[0].Contains("\r\nBcc:"), "Preserve the exact Unicode body, whitespace and line breaks with no recipient.");
var subjectHeader = mimeParts[0].Split("Subject: ")[1].Split("\r\nContent-Type:")[0];
Check(string.Concat(System.Text.RegularExpressions.Regex.Matches(subjectHeader, @"=\?UTF-8\?B\?([^?]*)\?=")
    .Cast<System.Text.RegularExpressions.Match>().Select(match => Encoding.UTF8.GetString(Convert.FromBase64String(match.Groups[1].Value)))) == savedDraft.Subject,
    "Encode the Unicode subject as a valid MIME header.");
Check(savedDraft.BrowserUri(freshAccount.Email).Query.Contains("authuser=sample%40example.com") &&
    Uri.UnescapeDataString(savedDraft.BrowserUri(freshAccount.Email).Fragment).Contains("rfc822msgid:" + savedDraft.RfcMessageId),
    "Open the saved draft by its Message-ID in the connected account without composing a duplicate.");
Check(GmailDraft.SubjectFrom("\r\n Hello\tworld \n") == "Hello world", "Normalize subject whitespace only.");
var longSubject = string.Concat(Enumerable.Repeat("😀", 100));
var encodedLong = GmailDraft.EncodeMessage("body", longSubject, "test@habitat.local");
var decodedLong = Encoding.UTF8.GetString(Convert.FromBase64String(encodedLong.Replace('-', '+').Replace('_', '/').PadRight((encodedLong.Length + 3) / 4 * 4, '=')));
Check(decodedLong.Split("\r\n").TakeWhile(line => line.Length > 0).All(line => line.Length < 998), "Fold long Unicode subject headers safely.");
draftStatus = HttpStatusCode.Forbidden;
await Reject<GmailDraftAuthorizationException>(() => draftService.CreateDraftAsync(draftBody, CancellationToken.None), "Missing compose permission requests explicit authorization without opening the browser.");
draftStatus = HttpStatusCode.ServiceUnavailable;
var callsBeforeFailure = draftCalls;
await Reject<HttpRequestException>(() => draftService.CreateDraftAsync(draftBody, CancellationToken.None), "Surface ambiguous save failures.");
Check(draftCalls == callsBeforeFailure + 1, "Do not retry draft creation after an ambiguous failure and risk duplicates.");
await Reject<ArgumentException>(() => draftService.CreateDraftAsync(" \r\n", CancellationToken.None), "Do not save an empty draft.");
using (var unsignedAuth = new GmailAuthService(new MemoryStore(new GmailAccount(clientConfig, null, null)), draftHttp,
    _ => throw new Exception("Saving while signed out must not launch sign-in.")))
using (var unsignedDrafts = new GmailService(unsignedAuth, draftHttp))
    await Reject<GmailDraftAuthorizationException>(() => unsignedDrafts.CreateDraftAsync(draftBody, CancellationToken.None), "Signed-out saves preserve the draft and require explicit authorization.");
var store = new MemoryStore(freshAccount);
var requests = new List<string>();
using var apiClient = new HttpClient(new FakeHandler(request =>
{
    Check(request.Method == HttpMethod.Get, "Gmail mailbox operations must always be GET, never mark/read/send/delete.");
    Check(request.Headers.Authorization?.Parameter == "access-test", "Gmail requests need bearer authorization.");
    requests.Add(request.RequestUri!.AbsoluteUri);
    var path = request.RequestUri.AbsolutePath;
    if (path.EndsWith("/labels/INBOX")) return Task.FromResult(Json("{\"messagesUnread\":42}"));
    if (path.EndsWith("/messages")) return Task.FromResult(Json("{\"messages\":[{\"id\":\"abc123\",\"threadId\":\"thread123\"}],\"nextPageToken\":\"page2\",\"resultSizeEstimate\":44}"));
    return Task.FromResult(Json(Message()));
}));
using var auth = new GmailAuthService(store, apiClient, _ => throw new Exception("Never open a browser during a cached-token read."));
using var gmail = new GmailService(auth, apiClient);
var inbox = await gmail.GetUnreadAsync(true, null, CancellationToken.None);
Check(inbox.UnreadCount == 42 && !inbox.CountIsEstimate && inbox.Messages.Count == 1 && inbox.NextPageToken == "page2", "Inbox unread count comes from the INBOX label.");
Check(Uri.UnescapeDataString(requests[0]).Contains("q=in:inbox is:unread") && requests[1].Contains("format=metadata"), "Fetch only unread ids and metadata for the list.");
Check(requests[1].Contains("metadataHeaders=Message-ID"), "Include the header needed to open the selected message in Gmail.");
var all = await gmail.GetUnreadAsync(false, "page2", CancellationToken.None);
Check(all.UnreadCount == 44 && all.CountIsEstimate && Uri.UnescapeDataString(requests[^2]).Contains("q=is:unread") && requests[^2].Contains("pageToken=page2"),
    "All unread should expose an estimate and support pagination.");
Check((await gmail.GetMessageAsync("abc123", CancellationToken.None)).Body == plain.Body && requests[^1].EndsWith("format=full"), "Fetch the body only when requested.");
await Reject<InvalidDataException>(() => gmail.GetMessageAsync("../profile", CancellationToken.None), "Reject path injection in message ids.");
Check(store.Saves == 0, "Fresh cached access tokens should not rewrite credentials.");
using (var mailbox = new GmailSession(() => new GmailAuthService(new MemoryStore(freshAccount), apiClient),
    account => new GmailService(account, apiClient), exception => exception.Message))
{
    mailbox.Initialize();
    var renders = 0;
    mailbox.Changed += () => renders++;
    await mailbox.RefreshAsync(() => true, false, CancellationToken.None);
    Check(!mailbox.IsBusy && mailbox.Messages.Count == 1 && mailbox.Inbox?.UnreadCount == 42 && renders == 2,
        "Mailbox coordination exposes loading and committed state without controls.");
    await mailbox.RefreshAsync(() => true, true, CancellationToken.None);
    Check(mailbox.Messages.Count == 1, "Mailbox pagination deduplicates IDs.");
    await mailbox.ReadAsync(mailbox.Messages[0], CancellationToken.None);
    Check(mailbox.Selected?.Body == plain.Body, "Mailbox selection loads the requested body independently of UI.");
    using var stopMailbox = new CancellationTokenSource();
    stopMailbox.Cancel();
    var previousRenders = renders;
    await mailbox.RefreshAsync(() => true, false, stopMailbox.Token);
    Check(renders == previousRenders && !mailbox.IsBusy, "Cancelled mailbox work does not update the view.");
    await mailbox.DisconnectAsync(CancellationToken.None);
    Check(mailbox.Messages.Count == 0 && mailbox.Inbox is null && mailbox.Auth?.IsSignedIn == false,
        "Mailbox disconnect clears view state after authorization is saved.");
}
var lateStore = new MemoryStore(new GmailAccount(null, null, null));
using var lateAuth = new GmailAuthService(lateStore, apiClient, _ => throw new Exception("Restoring credentials must not open Google."));
Check(!await lateAuth.RestoreSavedSessionAsync(CancellationToken.None), "Missing saved credentials should not open a browser.");
lateStore.Save(freshAccount);
var lateSaves = lateStore.Saves;
Check(await lateAuth.RestoreSavedSessionAsync(CancellationToken.None) && lateAuth.IsConfigured && lateAuth.Email == freshAccount.Email,
    "Recover an account saved after this application instance started.");
await lateAuth.SignInAsync(CancellationToken.None);
Check(lateStore.Saves == lateSaves, "Restoring and reusing a saved session must not rewrite or clear it.");
auth.Configure(clientConfig);
Check(auth.IsSignedIn && store.Saves == 0, "Reimporting the same OAuth client must preserve the saved session.");
await auth.SignInAsync(CancellationToken.None);
Check(auth.IsSignedIn && store.Saves == 0, "Connecting an authenticated account must reuse the session without opening Google.");

var refreshStore = new MemoryStore(freshAccount with { Tokens = freshAccount.Tokens! with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) } });
var exchanges = 0;
using var refreshHttp = new HttpClient(new FakeHandler(async request =>
{
    Check(request.RequestUri!.AbsoluteUri == "https://oauth2.googleapis.com/token" && request.Method == HttpMethod.Post, "Token exchanges must use Google's fixed OAuth endpoint.");
    var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync());
    Check(form["grant_type"] == "refresh_token" && form["refresh_token"] == "refresh-test", "Refresh access without a new browser login.");
    exchanges++;
    return Json("{\"access_token\":\"refreshed-test\",\"expires_in\":3600}");
}));
using var refreshing = new GmailAuthService(refreshStore, refreshHttp);
Check(await refreshing.GetAccessTokenAsync(CancellationToken.None) == "refreshed-test" && refreshStore.Account.Tokens!.RefreshToken == "refresh-test", "Persist a refresh without losing the previous refresh token.");
await refreshing.GetAccessTokenAsync(CancellationToken.None);
Check(exchanges == 1, "Reuse the renewed access token.");
await refreshing.DisconnectAsync(CancellationToken.None);
Check(!refreshing.IsSignedIn && refreshStore.Account.Tokens is null && refreshStore.Account.Client == clientConfig, "Disconnect clears tokens but retains the desktop client.");

var revokedStore = new MemoryStore(freshAccount with { Tokens = freshAccount.Tokens! with { ExpiresAt = DateTimeOffset.MinValue } });
using var revokedHttp = new HttpClient(new FakeHandler(_ => Task.FromResult(Json("{\"error\":\"invalid_grant\"}", HttpStatusCode.BadRequest))));
using var revoked = new GmailAuthService(revokedStore, revokedHttp);
await Reject<GmailSignInRequiredException>(() => revoked.GetAccessTokenAsync(CancellationToken.None), "Expired consent must request a new login.");
Check(revokedStore.Account.Tokens is null && !revoked.IsSignedIn, "Do not retain a revoked session.");

var rejectedStore = new MemoryStore(freshAccount with { Tokens = freshAccount.Tokens! with { ExpiresAt = DateTimeOffset.MinValue } });
using var rejectedHttp = new HttpClient(new FakeHandler(_ => Task.FromResult(Json("{\"error\":\"invalid_request\"}", HttpStatusCode.BadRequest))));
using var rejectedAuth = new GmailAuthService(rejectedStore, rejectedHttp);
await Reject<HttpRequestException>(() => rejectedAuth.GetAccessTokenAsync(CancellationToken.None), "A token request error must be reported.");
Check(rejectedAuth.IsSignedIn && rejectedStore.Saves == 0, "A request error must not delete persisted consent.");
using var invalidClientHttp = new HttpClient(new FakeHandler(_ => Task.FromResult(Json("{\"error\":\"invalid_client\"}", HttpStatusCode.Unauthorized))));
using var invalidClientAuth = new GmailAuthService(rejectedStore, invalidClientHttp);
await Reject<InvalidOperationException>(() => invalidClientAuth.GetAccessTokenAsync(CancellationToken.None), "Report a misconfigured client separately from revoked consent.");
Check(invalidClientAuth.IsSignedIn && rejectedStore.Saves == 0, "A client configuration error must preserve the saved session.");

var retries = 0;
var renewalCalls = 0;
using var retryHttp = new HttpClient(new FakeHandler(request =>
{
    if (request.RequestUri!.Host == "oauth2.googleapis.com")
    {
        renewalCalls++;
        return Task.FromResult(Json("{\"access_token\":\"retry-access\",\"expires_in\":3600}"));
    }
    retries++;
    if (retries == 1) return Task.FromResult(Json("{}", HttpStatusCode.Unauthorized));
    if (retries == 2)
    {
        var unavailable = Json("{}", HttpStatusCode.TooManyRequests);
        unavailable.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(100));
        return Task.FromResult(unavailable);
    }
    return Task.FromResult(Json(Message()));
}));
using var retryAuth = new GmailAuthService(new MemoryStore(freshAccount), retryHttp);
using var retryGmail = new GmailService(retryAuth, retryHttp);
await retryGmail.GetMessageAsync("abc123", CancellationToken.None);
Check(renewalCalls == 1 && retries == 3, "Refresh once after a 401 and retry a rate limit without unnecessary token exchanges.");

// Exercise the actual loopback callback, while all Google endpoints remain fake.
var loginStore = new MemoryStore(new GmailAccount(clientConfig, null, null));
string? expectedChallenge = null;
using var loginHttp = new HttpClient(new FakeHandler(async request =>
{
    if (request.RequestUri!.Host == "oauth2.googleapis.com")
    {
        var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync());
        var verifier = form["code_verifier"].ToString();
        var actualChallenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Check(actualChallenge == expectedChallenge && form["code"] == "synthetic-code", "Exchange the authorization code with the original PKCE verifier.");
        Check(new Uri(form["redirect_uri"].ToString()).Host == "127.0.0.1", "Only use the local loopback redirect.");
        return Json("{\"access_token\":\"login-test\",\"refresh_token\":\"offline-test\",\"expires_in\":3600,\"scope\":\"https://www.googleapis.com/auth/gmail.readonly\"}");
    }
    Check(request.RequestUri.AbsolutePath.EndsWith("/profile"), "Read only the selected account's profile after sign-in.");
    return Json("{\"emailAddress\":\"sample@example.com\"}");
}));
using var login = new GmailAuthService(loginStore, loginHttp, async url =>
{
    var query = QueryHelpers.ParseQuery(new Uri(url).Query);
    expectedChallenge = query["code_challenge"].ToString();
    var callback = query["redirect_uri"].ToString();
    using var local = new HttpClient();
    using var forged = await local.GetAsync(callback + "?code=forged&state=wrong");
    Check(forged.StatusCode == HttpStatusCode.BadRequest, "Reject a callback with the wrong state without completing login.");
    using var missing = await local.GetAsync(callback + "?state=" + query["state"]);
    Check(missing.StatusCode == HttpStatusCode.BadRequest, "Reject callbacks without an authorization code.");
    using var accepted = await local.GetAsync(callback + "?code=synthetic-code&state=" + query["state"]);
    Check(accepted.IsSuccessStatusCode, "Accept the matching local callback.");
});
await login.SignInAsync(CancellationToken.None);
Check(login.IsSignedIn && login.Email == "sample@example.com" && loginStore.Saves == 1, "Save only a fully authenticated account.");

using var cancellation = new CancellationTokenSource();
var cancelledStore = new MemoryStore(new GmailAccount(clientConfig, null, null));
using var cancelled = new GmailAuthService(cancelledStore, loginHttp, _ => { cancellation.Cancel(); return Task.CompletedTask; });
await Reject<OperationCanceledException>(() => cancelled.SignInAsync(cancellation.Token), "Cancel loopback login promptly.");
Check(cancelledStore.Saves == 0, "A cancelled sign-in must not save partial tokens.");
using var denied = new GmailAuthService(cancelledStore, loginHttp, async url =>
{
    var query = QueryHelpers.ParseQuery(new Uri(url).Query);
    using var local = new HttpClient();
    using var response = await local.GetAsync(query["redirect_uri"] + "?error=access_denied&state=" + query["state"]);
});
await Reject<GmailSignInRequiredException>(() => denied.SignInAsync(CancellationToken.None), "Handle Google consent denial without a crash.");

if (OperatingSystem.IsWindows())
{
    var testDirectory = Path.Combine(Path.GetTempPath(), "habitat-gmail-checks-" + Guid.NewGuid().ToString("N"));
    var testPath = Path.Combine(testDirectory, "gmail-account.bin");
    var testVault = "HABITAT/Gmail/Test/" + Guid.NewGuid().ToString("N");
    try
    {
        Check(Path.IsPathFullyQualified(GmailCredentialStore.DefaultPath) &&
            !GmailCredentialStore.DefaultPath.Contains("\\Packages\\", StringComparison.OrdinalIgnoreCase),
            "Default Gmail storage uses the desktop profile, independent of package redirection.");
        Check(new GmailCredentialStore(testPath).Load().Client is null, "A genuinely missing file represents an unconfigured account.");
        Directory.CreateDirectory(testDirectory);
        await Reject<UnauthorizedAccessException>(() => Task.Run(() => new GmailCredentialStore(testDirectory).Load()),
            "An unreadable account path must not masquerade as a missing account.");
        var protectedStore = new GmailCredentialStore(testPath);
        protectedStore.Save(freshAccount);
        Check(protectedStore.Load() == freshAccount, "Round-trip credentials through Windows DPAPI.");
        var raw = Encoding.UTF8.GetString(File.ReadAllBytes(testPath));
        Check(!raw.Contains("refresh-test") && !raw.Contains("sample@example.com") && !raw.Contains("test-only"), "Never store credentials as plain text.");
        protectedStore.Save(freshAccount with { Tokens = freshAccount.Tokens! with { ExpiresAt = DateTimeOffset.MinValue } });
        using (var reopened = new GmailAuthService(new GmailCredentialStore(testPath), refreshHttp,
            _ => throw new Exception("Reopening must not open Google.")))
        {
            Check(reopened.IsSignedIn && reopened.Email == freshAccount.Email, "Restore the account from encrypted storage after reopening.");
            await reopened.SignInAsync(CancellationToken.None);
            Check(protectedStore.Load().Tokens!.AccessToken == "refreshed-test", "Silently renew an expired access token and persist it after reopening.");
        }
        using (var reopenedAgain = new GmailAuthService(new GmailCredentialStore(testPath), refreshHttp,
            _ => throw new Exception("A second reopen must not open Google.")))
        {
            var callsBefore = exchanges;
            await reopenedAgain.SignInAsync(CancellationToken.None);
            Check(exchanges == callsBefore && reopenedAgain.IsSignedIn, "A second reopen reuses the persisted renewed access token.");
        }
        protectedStore.Save(freshAccount);
        var migratingStore = new GmailCredentialStore(testPath, testVault);
        Check(migratingStore.Load() == freshAccount, "Migrate the existing DPAPI account into Windows Credential Manager.");
        File.Delete(testPath);
        var vaultAccount = new GmailCredentialStore(testPath, testVault).Load();
        Check(vaultAccount.Client == freshAccount.Client && vaultAccount.Tokens!.RefreshToken == "refresh-test" &&
            vaultAccount.Tokens.AccessToken == "" && vaultAccount.Tokens.ExpiresAt == DateTimeOffset.MinValue,
            "Reopening reads persistent offline authorization from Windows even when the legacy file is absent.");
        for (var reopen = 0; reopen < 3; reopen++)
        {
            using var vaultedAuth = new GmailAuthService(new GmailCredentialStore(testPath, testVault), refreshHttp,
                _ => throw new Exception("Vault recovery must not open Google."));
            await vaultedAuth.SignInAsync(CancellationToken.None);
            Check(vaultedAuth.IsSignedIn && !File.Exists(testPath), "A fresh vault-backed instance renews silently without relying on an account file.");
        }
        using (var disconnectVault = new GmailAuthService(new GmailCredentialStore(testPath, testVault), refreshHttp))
            await disconnectVault.DisconnectAsync(CancellationToken.None);
        Check(new GmailCredentialStore(testPath, testVault).Load().Tokens is null,
            "Explicit disconnect remains persisted in the Windows vault.");
    }
    finally
    {
        GmailCredentialStore.DeleteTestVault(testVault);
        // Exact test-owned targets only; no profile or application storage.
        if (File.Exists(testPath)) File.Delete(testPath);
        if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory);
    }
}
Console.WriteLine($"Gmail checks passed: {checks}. No real Google login or mailbox was used.");

sealed class MemoryStore(GmailAccount account) : IGmailCredentialStore
{
    public GmailAccount Account { get; private set; } = account;
    public int Saves { get; private set; }
    public GmailAccount Load() => Account;
    public void Save(GmailAccount value) { Account = value; Saves++; }
}
sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request);
}
