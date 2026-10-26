using Habitat.Services;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;

var checks = 0;
void Check(bool success, string message) { if (!success) throw new Exception(message); checks++; }
var messengerTracePath = Path.Combine(AppContext.BaseDirectory, "isolated-messenger", Guid.NewGuid().ToString("N"), "facebook-messaging.jsonl");
await using var bridge = new BridgeService(0, facebookTracePath: messengerTracePath); // Never use the real app's port.
await bridge.StartAsync();
var address = bridge.Address ?? throw new Exception("The synthetic bridge did not start.");
using var http = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };
using var deniedStatus = await http.GetAsync("/status");
Check(deniedStatus.StatusCode == HttpStatusCode.Forbidden, "Diagnostics must require the pairing key.");
http.DefaultRequestHeaders.Add("X-Habitat-Key", bridge.PairKey);
using (var missingIntent = await http.PostAsync("/facebook/message", new StringContent("{}", System.Text.Encoding.UTF8, "application/json")))
    Check(missingIntent.StatusCode == HttpStatusCode.Forbidden && !bridge.FacebookMessaging.IsBusy, "A diagnostic read cannot become a send without explicit native-client intent.");
using (var crossOrigin = new HttpRequestMessage(HttpMethod.Post, "/facebook/message"))
{
    crossOrigin.Headers.Add("Origin", "https://example.test");
    crossOrigin.Headers.Add("X-Habitat-Action", "send-facebook-message");
    crossOrigin.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
    using var response = await http.SendAsync(crossOrigin);
    Check(response.StatusCode == HttpStatusCode.Forbidden && !bridge.FacebookMessaging.IsBusy, "Browser-origin requests cannot use the native diagnostic submission path.");
}
async Task<JsonDocument> Status() => JsonDocument.Parse(await http.GetStringAsync("/status"));
using (var status = await Status()) Check(!status.RootElement.GetProperty("connected").GetBoolean() && !bridge.IsConnected,
    "Reading status must never manufacture a heartbeat.");
using (var status = await Status()) Check(status.RootElement.GetProperty("facebook").GetProperty("stage").GetString() == "waiting-for-extension" &&
    !bridge.Facebook.IsConnected, "Facebook diagnostics distinguish a missing worker without starting a read.");
var socketUrl = new Uri(address.Replace("http:", "ws:") + "/bridge");
async Task Send(ClientWebSocket socket, object packet) => await socket.SendAsync(
    JsonSerializer.SerializeToUtf8Bytes(packet).AsMemory(), WebSocketMessageType.Text, true, CancellationToken.None);
async Task<JsonDocument?> Receive(ClientWebSocket socket)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var bytes = new byte[8192];
    using var buffer = new MemoryStream();
    ValueWebSocketReceiveResult result;
    do
    {
        result = await socket.ReceiveAsync(bytes.AsMemory(), timeout.Token);
        if (result.MessageType == WebSocketMessageType.Close) return null;
        buffer.Write(bytes, 0, result.Count);
    } while (!result.EndOfMessage);
    return JsonDocument.Parse(buffer.ToArray());
}
async Task WaitUntil(Func<bool> condition)
{
    var deadline = DateTime.UtcNow.AddSeconds(3);
    while (!condition()) { if (DateTime.UtcNow >= deadline) throw new Exception("Bridge state did not converge."); await Task.Delay(20); }
}
using (var unauthorized = new ClientWebSocket())
{
    await unauthorized.ConnectAsync(socketUrl, CancellationToken.None);
    await Send(unauthorized, new { kind = "auth", key = "invalid-key" });
    Check(await Receive(unauthorized) is null && unauthorized.CloseStatus == WebSocketCloseStatus.PolicyViolation,
        "Reject unauthenticated WebSocket clients.");
}
using var socket = new ClientWebSocket();
await socket.ConnectAsync(socketUrl, CancellationToken.None);
await Send(socket, new { kind = "auth", key = bridge.PairKey });
using (var tick = await Receive(socket)) Check(tick!.RootElement.GetProperty("kind").GetString() == "tick" && !bridge.IsConnected,
    "Socket traffic is not enough; the page must acknowledge readiness.");
await Send(socket, new { kind = "heartbeat", ready = true, model = "OpenAI Sol 5.6" });
await WaitUntil(() => bridge.IsConnected);
Check(!bridge.TryQueueMessage("Needs external support", out var upgradeReason, openExternally: true) && upgradeReason.Contains("Reload"),
    "An older extension cannot silently send an external action without opening the chat.");
await Send(socket, new { kind = "heartbeat", ready = true, supportsExternalOpen = true });
await Task.Delay(30);
var openChat = bridge.OpenChatAsync();
using (var openTick = await Receive(socket))
{
    var openId = openTick!.RootElement.GetProperty("openChatId").GetString();
    Check(openId is not null && !bridge.HasMessageInFlight, "An empty open request does not queue a message.");
    await Send(socket, new { kind = "chat-opened", id = openId, opened = true });
}
Check(await openChat, "The linked chat window acknowledges an authenticated explicit open.");
using (var status = await Status()) Check(status.RootElement.GetProperty("transport").GetString() == "worker-websocket", "Report the worker transport independently of page visibility.");
Check(bridge.TryQueueMessage("Synthetic local message", out _, openExternally: true) && !bridge.TryQueueMessage("Duplicate", out _), "Allow only one in-flight request.");
string? id;
using (var tick = await Receive(socket))
{
    var message = tick!.RootElement.GetProperty("message");
    id = message.GetProperty("id").GetString();
    Check(message.GetProperty("text").GetString() == "Synthetic local message", "Push the queued message to the extension worker.");
    Check(message.GetProperty("openExternally").GetBoolean(), "Carry the explicit external action with the exact queued message.");
}
await http.PostAsJsonAsync("/event", new { type = "response", id = "wrong-id", text = "ignored" });
Check(bridge.HasMessageInFlight, "Ignore completion events belonging to another request.");
await http.PostAsJsonAsync("/event", new { type = "received", id });
using (var tick = await Receive(socket)) Check(tick!.RootElement.GetProperty("message").ValueKind == JsonValueKind.Null && bridge.HasMessageInFlight,
    "Stop redelivery once acknowledged but wait for the actual response.");
await Send(socket, new { kind = "heartbeat", ready = true });
await http.PostAsJsonAsync("/event", new { type = "response", id, text = "Synthetic local response" });
Check(!bridge.HasMessageInFlight, "Release the in-flight request after its matching response.");
await Send(socket, new { kind = "heartbeat", ready = false, issue = "Chrome suspended the linked tab" });
await WaitUntil(() => !bridge.IsConnected);
Check(!bridge.TryQueueMessage("Must retain draft", out var reason) && reason.Contains("suspended"), "Refuse delivery when the actual page is unresponsive.");
await Send(socket, new { kind = "heartbeat", ready = true });
await WaitUntil(() => bridge.IsConnected);
await Task.Delay(4200);
Check(!bridge.IsConnected, "A stale page heartbeat must expire even if the socket still receives ticks.");
socket.Abort();
var orphanEvents = 0;
bridge.EventReceived += (_, data) => { if (data.Type == "response") orphanEvents++; };
await http.PostAsJsonAsync("/event", new { type = "response", text = "Orphan response without an ID" });
Check(orphanEvents == 0, "Events without an active request and ID cannot create responses.");
using (var diagnostics = await Status()) Check(diagnostics.RootElement.GetProperty("protocolVersion").GetInt32() == 1 &&
    diagnostics.RootElement.GetProperty("processId").GetInt32() == Environment.ProcessId,
    "Updates identify bridge version and process without reading presentation text.");
await using (var conflicting = new BridgeService(new Uri(address).Port))
{
    var failed = false;
    conflicting.StartFailed += (_, _) => failed = true;
    await conflicting.StartAsync();
    Check(failed && conflicting.Address is null, "Failed bridge startup disposes its server.");
    await conflicting.DisposeAsync();
    await conflicting.DisposeAsync();
}
Console.WriteLine($"Bridge checks passed: {checks}. Only a random local port was used; no Chrome or ChatGPT requests.");

var translationSocketUrl = new Uri(address.Replace("http:", "ws:") + "/translate-bridge");
using (var denied = new ClientWebSocket())
{
    await denied.ConnectAsync(translationSocketUrl, CancellationToken.None);
    await Send(denied, new { key = "invalid", translationProtocol = 1 });
    Check(await Receive(denied) is null && denied.CloseStatus == WebSocketCloseStatus.PolicyViolation,
        "The translation channel must reject an invalid pairing key.");
}
using (var browser = new ClientWebSocket())
{
    await browser.ConnectAsync(translationSocketUrl, CancellationToken.None);
    await Send(browser, new { key = bridge.PairKey, translationProtocol = 1 });
    using (var tick = await Receive(browser)) Check(tick!.RootElement.GetProperty("kind").GetString() == "translation-tick",
        "The independent translation channel does not require a ChatGPT tab.");
    var result = bridge.Translation.TranslateAsync("Synthetic browser-page input", "el", CancellationToken.None);
    string? translationId = null;
    while (translationId is null)
    {
        using var tick = await Receive(browser);
        var request = tick!.RootElement.GetProperty("request");
        if (request.ValueKind != JsonValueKind.Null) translationId = request.GetProperty("id").GetString();
    }
    await Send(browser, new { kind = "translation-result", id = "wrong", text = "Ignored" });
    Check(!result.IsCompleted, "Translation results must match their browser job ID.");
    await Send(browser, new { kind = "translation-result", id = translationId, text = "Γεια σου κόσμε" });
    Check((await result.WaitAsync(TimeSpan.FromSeconds(3))).Text == "Γεια σου κόσμε" && !bridge.HasMessageInFlight,
        "Only the requested translated text returns, without creating a chat message.");
    browser.Abort();
}
Console.WriteLine($"Bridge and browser translation checks passed: {checks}.");

var searchUrl = new Uri(address.Replace("http:", "ws:") + "/search-bridge");
using (var denied = new ClientWebSocket())
{
    await denied.ConnectAsync(searchUrl, CancellationToken.None);
    await Send(denied, new { key = "invalid", searchProtocol = 1 });
    Check(await Receive(denied) is null && denied.CloseStatus == WebSocketCloseStatus.PolicyViolation,
        "The search channel rejects an invalid pairing key.");
}
using (var browser = new ClientWebSocket())
{
    await browser.ConnectAsync(searchUrl, CancellationToken.None);
    await Send(browser, new { key = bridge.PairKey, searchProtocol = 1 });
    using (var tick = await Receive(browser)) Check(tick!.RootElement.GetProperty("kind").GetString() == "search-tick",
        "Google search connects independently of ChatGPT and Translate.");
    var search = bridge.Search.SearchAsync("Synthetic Google query ☕", 10, CancellationToken.None);
    string? searchId = null;
    while (searchId is null)
    {
        using var tick = await Receive(browser);
        var request = tick!.RootElement.GetProperty("request");
        if (request.ValueKind == JsonValueKind.Null) continue;
        searchId = request.GetProperty("id").GetString();
        Check(request.GetProperty("query").GetString() == "Synthetic Google query ☕" && request.GetProperty("start").GetInt32() == 10,
            "Search WebSocket carries the exact query and page offset.");
    }
    await Send(browser, new { kind = "search-result", id = "wrong", results = Array.Empty<object>() });
    await Send(browser, new { kind = "search-pong" });
    Check(!search.IsCompleted, "The search channel ignores unrelated IDs.");
    await Send(browser, new { kind = "search-result", id = searchId, results = new[] {
        new { title = "Plain <title> ☕", url = "https://example.com/one", snippet = "Visible text & 雪" } }, nextStart = 20 });
    var page = await search.WaitAsync(TimeSpan.FromSeconds(3));
    Check(page.Results.Single().Title == "Plain <title> ☕" && page.Results.Single().Snippet == "Visible text & 雪" && page.NextStart == 20 && !bridge.HasMessageInFlight,
        "Google results cross the authenticated bridge without creating a conversation message.");
    using (var status = await Status()) Check(status.RootElement.GetProperty("searchConnected").GetBoolean(), "Status reports the independent search connection.");
    bridge.Search.Reset();
    search = bridge.Search.SearchAsync("New query after clear", 0, CancellationToken.None);
    string? resetId = null;
    while (resetId is null)
    {
        using var tick = await Receive(browser);
        var root = tick!.RootElement;
        if (!root.TryGetProperty("reset", out var reset) || reset.ValueKind != JsonValueKind.String) continue;
        resetId = reset.GetString();
        Check(root.GetProperty("request").ValueKind == JsonValueKind.Null, "Tab cleanup is delivered before the following search request.");
    }
    await Send(browser, new { kind = "search-reset", id = "stale-reset", closed = true });
    Check(bridge.Search.HasPendingReset, "Unrelated cleanup acknowledgements cannot release the following search.");
    await Send(browser, new { kind = "search-reset", id = resetId, closed = true });
    await WaitUntil(() => !bridge.Search.HasPendingReset);
    searchId = null;
    while (searchId is null)
    {
        using var tick = await Receive(browser);
        var request = tick!.RootElement.GetProperty("request");
        if (request.ValueKind != JsonValueKind.Null) searchId = request.GetProperty("id").GetString();
    }
    await Send(browser, new { kind = "search-result", id = searchId, results = Array.Empty<object>() });
    await search.WaitAsync(TimeSpan.FromSeconds(3));
    Check(bridge.Search.LastResetClosedTab == true, "Authenticated tab cleanup acknowledgement releases the replacement search.");
    search = bridge.Search.SearchAsync("Invalid links", 0, CancellationToken.None);
    browser.Abort();
    try { await search.WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("Disconnect ignored."); }
    catch (Habitat.Features.Search.BrowserSearchException) { Check(true, "Disconnecting Chrome releases the search channel."); }
}
Console.WriteLine($"Bridge, Translate and Google Search checks passed: {checks}.");

var youtubeUrl = new Uri(address.Replace("http:", "ws:") + "/youtube-bridge");
using (var denied = new ClientWebSocket())
{
    await denied.ConnectAsync(youtubeUrl, CancellationToken.None);
    await Send(denied, new { key = "invalid", searchProtocol = 1 });
    Check(await Receive(denied) is null && denied.CloseStatus == WebSocketCloseStatus.PolicyViolation,
        "YouTube rejects an unauthenticated search connection.");
}
using (var youtube = new ClientWebSocket())
using (var google = new ClientWebSocket())
{
    await youtube.ConnectAsync(youtubeUrl, CancellationToken.None);
    await google.ConnectAsync(searchUrl, CancellationToken.None);
    await Send(youtube, new { key = bridge.PairKey, searchProtocol = 1 });
    await Send(google, new { key = bridge.PairKey, searchProtocol = 1 });
    using (var tick = await Receive(youtube)) Check(tick!.RootElement.GetProperty("kind").GetString() == "search-tick", "YouTube connects without ChatGPT.");
    using (var tick = await Receive(google)) { }
    var videoTask = bridge.YouTubeSearch.SearchAsync("Synthetic YouTube ☕", 20, default);
    var webTask = bridge.Search.SearchAsync("Independent web search", 0, default);
    async Task<string> RequestId(ClientWebSocket channel)
    {
        while (true)
        {
            using var tick = await Receive(channel);
            var request = tick!.RootElement.GetProperty("request");
            if (request.ValueKind != JsonValueKind.Null) return request.GetProperty("id").GetString()!;
        }
    }
    var videoId = await RequestId(youtube);
    var webId = await RequestId(google);
    await Send(google, new { kind = "search-result", id = videoId, results = Array.Empty<object>() });
    await Send(youtube, new { kind = "search-result", id = webId, results = Array.Empty<object>() });
    Check(!videoTask.IsCompleted && !webTask.IsCompleted, "The Google and YouTube channels cannot complete one another's queries.");
    await Send(youtube, new { kind = "search-result", id = videoId, results = new[] {
        new { title = "Video ☕", url = "https://www.youtube.com/watch?v=abcdefghijk", snippet = "Channel · 12:34\nDescription" } }, nextStart = 40 });
    Check((await videoTask.WaitAsync(TimeSpan.FromSeconds(3))).Results.Single().Title == "Video ☕" && !webTask.IsCompleted,
        "YouTube returns video data while Google remains independently pending.");
    var channelTask = bridge.YouTubeSearch.SearchAsync("Synthetic channel", 0, default);
    var channelId = await RequestId(youtube);
    await Send(youtube, new { kind = "search-result", id = channelId, results = new[] {
        new { title = "Synthetic channel", url = "https://www.youtube.com/@synthetic", snippet = "@synthetic · 12K subscribers" },
        new { title = "Video ☕", url = "https://www.youtube.com/watch?v=abcdefghijk", snippet = "Channel · 12:34" } }, nextStart = 20 });
    var channelPage = await channelTask.WaitAsync(TimeSpan.FromSeconds(3));
    Check(channelPage.Results.Count == 2 && channelPage.Results[0].Url == "https://www.youtube.com/@synthetic" &&
        channelPage.NextStart == 20 && !webTask.IsCompleted,
        "The YouTube bridge preserves a matching channel before videos and keeps video pagination independent.");
    bridge.YouTubeSearch.Reset();
    string? resetId = null;
    while (resetId is null)
    {
        using var tick = await Receive(youtube);
        var reset = tick!.RootElement.GetProperty("reset");
        if (reset.ValueKind == JsonValueKind.String) resetId = reset.GetString();
    }
    await Send(youtube, new { kind = "search-reset", id = resetId, closed = true });
    await WaitUntil(() => !bridge.YouTubeSearch.HasPendingReset);
    Check(!bridge.Search.HasPendingReset && !webTask.IsCompleted && bridge.YouTubeSearch.LastResetClosedTab == true,
        "YouTube cleanup affects only its search tab, even during a Google query.");
    await Send(google, new { kind = "search-result", id = webId, results = Array.Empty<object>() });
    await webTask.WaitAsync(TimeSpan.FromSeconds(3));
    using (var status = await Status()) Check(status.RootElement.GetProperty("youtubeConnected").GetBoolean() &&
        status.RootElement.GetProperty("searchConnected").GetBoolean(), "Diagnostics expose both provider connections independently.");
    youtube.Abort();
    google.Abort();
}
Console.WriteLine($"All browser bridge checks passed: {checks}.");

// Both the current Chrome extension and its older packets connect without a browser preference.
foreach (var endpoint in new[] { "/bridge", "/translate-bridge", "/search-bridge", "/youtube-bridge" })
foreach (var withIdentity in new[] { false, true })
{
    using var chrome = new ClientWebSocket();
    await chrome.ConnectAsync(new Uri(address.Replace("http:", "ws:") + endpoint), default);
    var auth = new Dictionary<string, object> { ["kind"] = "auth", ["key"] = bridge.PairKey,
        ["translationProtocol"] = 1, ["searchProtocol"] = 1 };
    if (withIdentity) auth["browser"] = "chrome";
    await Send(chrome, auth);
    using var tick = await Receive(chrome);
    Check(tick is not null, $"Chrome connects to {endpoint} with browser metadata: {withIdentity}.");
    chrome.Abort();
}
Console.WriteLine($"Chrome bridge compatibility checks passed: {checks}.");

var facebookUrl = new Uri(address.Replace("http:", "ws:") + "/facebook-bridge");
using (var denied = new ClientWebSocket())
{
    await denied.ConnectAsync(facebookUrl, default);
    await Send(denied, new { key = "wrong", facebookProtocol = 1 });
    Check(await Receive(denied) is null && denied.CloseStatus == WebSocketCloseStatus.PolicyViolation,
        "Facebook snapshots require the local pairing key.");
}
using (var facebook = new ClientWebSocket())
{
    await facebook.ConnectAsync(facebookUrl, default);
    await Send(facebook, new { key = bridge.PairKey, facebookProtocol = 1, extensionVersion = "0.10.2" });
    using (var tick = await Receive(facebook)) Check(tick!.RootElement.GetProperty("kind").GetString() == "facebook-tick", "Facebook uses its own snapshot protocol.");
    await WaitUntil(() => bridge.Facebook.IsConnected);
    try
    {
        await bridge.FacebookMessaging.SendAsync("https://www.facebook.com/messages/t/synthetic.a", "Synthetic A", "Synthetic only", default);
        throw new Exception("An old extension unexpectedly accepted a message.");
    }
    catch (InvalidOperationException exception) { Check(exception.Message.Contains("Reload"), "Messaging needs an explicitly advertised extension capability before anything is queued."); }
    async Task<string> FacebookRequest()
    {
        while (true)
        {
            using var tick = await Receive(facebook);
            var request = tick!.RootElement.GetProperty("request");
            if (request.ValueKind != JsonValueKind.Null) return request.GetProperty("id").GetString()!;
        }
    }
    var snapshot = bridge.Facebook.ReadAsync(default);
    var requestId = await FacebookRequest();
    await Send(facebook, new { kind = "facebook-progress", id = "stale", diagnostic = new { stage = "section-read", photos = 999 } });
    await Task.Delay(30);
    Check(bridge.Facebook.Status.Reader is null, "Stale progress cannot change the current Facebook diagnostic.");
    await Send(facebook, new { kind = "facebook-progress", id = requestId,
        diagnostic = new { stage = "section-missing", sections = 2, photos = -1, rows = 10001 } });
    await WaitUntil(() => bridge.Facebook.Status.Reader is not null);
    Check(bridge.Facebook.Status.ExtensionVersion == "0.10.2" && bridge.Facebook.Status.Stage == "reading" &&
        bridge.Facebook.Status.Reader is { Stage: "section-missing", Sections: 2, Photos: null, Rows: null } && !snapshot.IsCompleted,
        "Reader progress identifies the failing stage, validates bounded counts and never completes the request.");
    await Send(facebook, new { kind = "facebook-result", id = "stale", contacts = Array.Empty<object>() });
    await Task.Delay(30);
    Check(!snapshot.IsCompleted, "Unrelated or stale Facebook snapshots cannot complete a current request.");
    await Send(facebook, new { kind = "facebook-result", id = requestId, contacts = new[] {
        new { name = "Amiga ☕ 雪", photoUrl = "https://scontent.fbcdn.net/avatar.jpg", link = (string?)null } } });
    var contacts = await snapshot.WaitAsync(TimeSpan.FromSeconds(3));
    Check(contacts.Count == 1 && contacts[0].Name == "Amiga ☕ 雪" && contacts[0].Link is null,
        "Contact names keep their original language and links are optional.");
    Check(contacts[0].IsOnline is null, "Older extensions without presence leave the online state unknown.");
    foreach (var online in new[] { true, false })
    {
        snapshot = bridge.Facebook.ReadAsync(default);
        requestId = await FacebookRequest();
        await Send(facebook, new { kind = "facebook-result", id = requestId, contacts = new[] {
            new { name = "Amiga ☕ 雪", photoUrl = "https://scontent.fbcdn.net/avatar.jpg", isOnline = online } } });
        contacts = await snapshot.WaitAsync(TimeSpan.FromSeconds(3));
        Check(contacts[0].IsOnline == online, "The bridge preserves explicit online and offline states.");
    }
    using (var status = await Status())
    {
        var metadata = status.RootElement.GetProperty("facebook");
        Check(metadata.GetProperty("stage").GetString() == "loaded" && metadata.GetProperty("contactCount").GetInt32() == 1 &&
            !metadata.ToString().Contains("fbcdn") && !metadata.ToString().Contains("Amiga"),
            "Facebook diagnostics retain the received count, without contact names or photo URLs.");
    }
    foreach (var unsafePhoto in new[] { "http://scontent.fbcdn.net/a.jpg", "https://fbcdn.net.evil.test/a.jpg", "https://user@scontent.fbcdn.net/a.jpg", "file:///C:/private.png" })
    {
        snapshot = bridge.Facebook.ReadAsync(default);
        requestId = await FacebookRequest();
        await Send(facebook, new { kind = "facebook-result", id = requestId, contacts = new[] { new { name = "Synthetic", photoUrl = unsafePhoto } } });
        try { await snapshot.WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("Unsafe photo accepted."); }
        catch (InvalidOperationException) { checks++; }
    }
    snapshot = bridge.Facebook.ReadAsync(default);
    requestId = await FacebookRequest();
    await Send(facebook, new { kind = "facebook-result", id = requestId, contacts = new[] {
        new { name = "Synthetic", photoUrl = "https://scontent.fbcdn.net/avatar.jpg", link = "https://www.facebook.com/l.php?u=https://evil.test" } } });
    try { await snapshot.WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("Redirect link accepted."); }
    catch (InvalidOperationException) { checks++; }
    using (var cancel = new CancellationTokenSource())
    {
        snapshot = bridge.Facebook.ReadAsync(cancel.Token);
        await FacebookRequest();
        cancel.Cancel();
        try { await snapshot; throw new Exception("Canceled snapshot completed."); }
        catch (OperationCanceledException) { checks++; }
    }
    snapshot = bridge.Facebook.ReadAsync(default);
    requestId = await FacebookRequest();
    await Send(facebook, new { kind = "facebook-result", id = requestId, error = "permission" });
    try { await snapshot.WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("Missing permission ignored."); }
    catch (InvalidOperationException exception) { Check(exception.Message.Contains("Enable Facebook contacts"), "Permission failures offer the English extension recovery action."); }
    snapshot = bridge.Facebook.ReadAsync(default);
    await FacebookRequest();
    facebook.Abort();
    try { await snapshot.WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("Disconnected snapshot did not fail."); }
    catch (InvalidOperationException) { checks++; }
    await WaitUntil(() => !bridge.Facebook.IsConnected);
}
Console.WriteLine($"Facebook bridge checks passed: {checks}. Synthetic snapshots only.");

using (var messaging = new ClientWebSocket())
{
    await messaging.ConnectAsync(facebookUrl, default);
    await Send(messaging, new { key = bridge.PairKey, facebookProtocol = 1, facebookMessagingProtocol = 1, extensionVersion = "0.10.5" });
    await WaitUntil(() => bridge.Facebook.Status.ExtensionVersion == "0.10.5");
    async Task<string> MessageRequest(bool inspection = false)
    {
        while (true)
        {
            using var tick = await Receive(messaging);
            var message = tick!.RootElement.GetProperty("message");
            if (message.ValueKind != JsonValueKind.Null)
            {
                Check(message.GetProperty("inspectOnly").GetBoolean() == inspection, "The wire protocol keeps inspection distinct from explicit sending.");
                return message.GetProperty("id").GetString()!;
            }
        }
    }
    const string target = "https://www.facebook.com/messages/e2ee/t/synthetic.a";
    var pending = bridge.FacebookMessaging.SendAsync(target, "Synthetic A", "Synthetic exact text ☕\nsecond line", default);
    var messengerOperationId = await MessageRequest();
    await Send(messaging, new { kind = "facebook-message-progress", id = "stale", stage = "send" });
    await Task.Delay(30);
    Check(bridge.FacebookMessaging.LastDiagnostic?.Stage == "queued", "Stale progress cannot rewrite the current Messenger trace.");
    await Send(messaging, new { kind = "facebook-message-progress", id = messengerOperationId, stage = "resolve",
        diagnostic = new { page = "profile", mains = 1, nameMatches = 0, messageButtons = 1, editors = -1,
            privateText = "MUST NOT BE LOGGED", url = "https://private.test/" } });
    await WaitUntil(() => bridge.FacebookMessaging.LastDiagnostic?.Stage == "resolve");
    Check(bridge.FacebookMessaging.LastDiagnostic?.Details is { Page: "profile", NameMatches: 0, MessageButtons: 1, Editors: null },
        "The trace preserves useful structural counts while rejecting invalid diagnostic values.");
    Check(bridge.HasMessageInFlight, "Pending Messenger operations participate in update handoff protection.");
    await Send(messaging, new { kind = "facebook-message-result", id = "stale", status = "submitted" });
    await Task.Delay(30);
    Check(!pending.IsCompleted, "A stale submission result cannot clear the current recipient's draft.");
    try
    {
        await bridge.FacebookMessaging.SendAsync(target, "Synthetic A", "Concurrent synthetic text", default);
        throw new Exception("A concurrent Messenger operation was admitted.");
    }
    catch (InvalidOperationException exception) { Check(exception.Message.Contains("Wait"), "Only one explicit Messenger submission can be in flight."); }
    await Send(messaging, new { kind = "facebook-message-result", id = messengerOperationId, status = "submitted" });
    Check((await pending.WaitAsync(TimeSpan.FromSeconds(3))).Status == "submitted" && !bridge.HasMessageInFlight,
        "A matching acknowledgement completes exactly the pending operation.");
    var traceText = File.ReadAllText(messengerTracePath);
    Check(traceText.Contains("resolve") && traceText.Contains("submitted") && !traceText.Contains("Synthetic A") &&
        !traceText.Contains("Synthetic exact text") && !traceText.Contains("private.test") && !traceText.Contains("MUST NOT BE LOGGED"),
        "The bounded local trace records the full attempt without names, message text or URLs.");
    using (var native = new HttpRequestMessage(HttpMethod.Post, "/facebook/message"))
    {
        native.Headers.Add("X-Habitat-Action", "send-facebook-message");
        native.Content = new StringContent(JsonSerializer.Serialize(new { contactName = "Synthetic Native A", text = "Synthetic native diagnostic message" }), System.Text.Encoding.UTF8, "application/json");
        var nativePending = http.SendAsync(native);
        while (true)
        {
            using var tick = await Receive(messaging);
            var lookup = tick!.RootElement.GetProperty("request");
            if (lookup.ValueKind == JsonValueKind.Null) continue;
            await Send(messaging, new { kind = "facebook-result", id = lookup.GetProperty("id").GetString(), contacts = new[] {
                new { name = "Synthetic Native A", photoUrl = "https://scontent.fbcdn.net/native.jpg", link = target } } });
            break;
        }
        var nativeOperation = await MessageRequest();
        await Send(messaging, new { kind = "facebook-message-result", id = nativeOperation, status = "submitted" });
        using var response = await nativePending;
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Check(result.RootElement.GetProperty("status").GetString() == "submitted", "The native tool resolves a unique contact and uses the same messaging service, with synthetic sockets only.");
    }
    pending = bridge.FacebookMessaging.InspectAsync(target, "Synthetic A", default);
    messengerOperationId = await MessageRequest(true);
    await Send(messaging, new { kind = "facebook-message-result", id = messengerOperationId, status = "not-sent", error = "inspected" });
    Check((await pending.WaitAsync(TimeSpan.FromSeconds(3))) is { Status: "not-sent", Error: "inspected" },
        "A read-only inspection preserves its non-send outcome in the native diagnostics.");
    pending = bridge.FacebookMessaging.SendAsync(target, "Synthetic A", "Another synthetic draft", default);
    messengerOperationId = await MessageRequest();
    await Send(messaging, new { kind = "facebook-message-result", id = messengerOperationId, status = "uncertain" });
    Check((await pending.WaitAsync(TimeSpan.FromSeconds(3))).Status == "uncertain", "Uncertain delivery is preserved and never automatically retried.");
    using var cancelMessage = new CancellationTokenSource();
    pending = bridge.FacebookMessaging.SendAsync(target, "Synthetic A", "Canceled synthetic draft", cancelMessage.Token);
    await MessageRequest(); cancelMessage.Cancel();
    try { await pending; throw new Exception("Canceled message remained active."); }
    catch (OperationCanceledException) { Check(!bridge.FacebookMessaging.IsBusy, "Cancellation discards the pending operation without retrying."); }
    pending = bridge.FacebookMessaging.SendAsync(target, "Synthetic A", "Disconnect synthetic draft", default);
    await MessageRequest(); messaging.Abort();
    Check((await pending.WaitAsync(TimeSpan.FromSeconds(3))).Status == "uncertain", "A disconnected worker leaves delivery uncertain instead of resending.");
}
Console.WriteLine($"Facebook messaging bridge checks passed: {checks}. Synthetic sockets only; no Facebook tab or message.");
var rotationPath = Path.Combine(Path.GetDirectoryName(messengerTracePath)!, "rotation.jsonl");
File.WriteAllText(rotationPath, new string('x', 512 * 1024 + 1));
var rotationTrace = new Habitat.Features.Facebook.FacebookMessageTrace(TimeProvider.System, rotationPath);
rotationTrace.Write(Guid.NewGuid().ToString("N"), "queued");
Check(File.Exists(rotationPath + ".previous") && new FileInfo(rotationPath).Length < 1024,
    "Messenger logging rotates a bounded previous file without touching application data.");

var workerInfo = new System.Diagnostics.ProcessStartInfo("node")
{
    UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true
};
workerInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "fixtures", "facebook-bridge.worker.cjs"));
workerInfo.ArgumentList.Add(address);
using (var worker = System.Diagnostics.Process.Start(workerInfo) ?? throw new Exception("Could not start the isolated Facebook worker."))
{
    var errors = worker.StandardError.ReadToEndAsync();
    try
    {
        await WaitUntil(() => bridge.Facebook.IsConnected);
        var result = await bridge.Facebook.ReadAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        Check(result.Count == 1 && result[0].Name == "Real wire · synthetic contact ☕" && result[0].IsOnline == true,
            "The actual JavaScript Facebook worker pairs, authenticates and returns a snapshot through the real C# WebSocket bridge.");
    }
    finally
    {
        if (!worker.HasExited) worker.Kill(entireProcessTree: true);
        await worker.WaitForExitAsync();
        var diagnostic = await errors;
        if (!string.IsNullOrEmpty(diagnostic)) Console.WriteLine(diagnostic);
    }
}
Console.WriteLine($"Facebook worker-to-desktop integration passed: {checks}. Random port and synthetic contacts only.");
