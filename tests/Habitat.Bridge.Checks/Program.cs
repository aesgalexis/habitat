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
using (var status = await Status()) Check(status.RootElement.GetProperty("transport").GetString() == "worker-websocket", "Report the worker transport independently of page visibility.");
Check(bridge.TryQueueMessage("Synthetic local message", out _) && !bridge.TryQueueMessage("Duplicate", out _), "Allow only one in-flight request.");
string? id;
using (var tick = await Receive(socket))
{
    var message = tick!.RootElement.GetProperty("message");
    id = message.GetProperty("id").GetString();
    Check(message.GetProperty("text").GetString() == "Synthetic local message", "Push the queued message to the extension worker.");
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
Console.WriteLine($"Bridge checks passed: {checks}. Only a random local port was used; no Chrome or ChatGPT requests.");
