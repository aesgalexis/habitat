using Habitat.Services;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;

var checks = 0;
void Check(bool success, string message) { if (!success) throw new Exception(message); checks++; }
await using var bridge = new BridgeService(0); // Never use the real app's port.
await bridge.StartAsync();
var address = bridge.Address ?? throw new Exception("The synthetic bridge did not start.");
using var http = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };
using var deniedStatus = await http.GetAsync("/status");
Check(deniedStatus.StatusCode == HttpStatusCode.Forbidden, "Diagnostics must require the pairing key.");
http.DefaultRequestHeaders.Add("X-Habitat-Key", bridge.PairKey);
async Task<JsonDocument> Status() => JsonDocument.Parse(await http.GetStringAsync("/status"));
using (var status = await Status()) Check(!status.RootElement.GetProperty("connected").GetBoolean() && !bridge.IsConnected,
    "Reading status must never manufacture a heartbeat.");
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
