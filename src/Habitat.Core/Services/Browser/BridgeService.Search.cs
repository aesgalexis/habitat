using Habitat.Features.Search;
using Microsoft.AspNetCore.Http;
using System.Net.WebSockets;
using System.Text.Json;

namespace Habitat.Services;

public sealed partial class BridgeService
{
    internal BrowserSearchService Search { get; }
    internal BrowserSearchService YouTubeSearch { get; }

    private Task HandleSearchSocketAsync(HttpContext context) => HandleSearchSocketAsync(context, Search);

    private async Task HandleSearchSocketAsync(HttpContext context, BrowserSearchService search)
    {
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, BrowserToken);
        var owner = new object();
        Task? receive = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var auth = await ReadSocketJsonAsync(socket, timeout.Token);
            if (auth is null || !auth.RootElement.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String ||
                !IsAuthorized(key.GetString() ?? "") || !auth.RootElement.TryGetProperty("searchProtocol", out var protocol) ||
                protocol.ValueKind != JsonValueKind.Number || !protocol.TryGetInt32(out var version) || version != 1 || !IsBrowserAllowed(context.Request, auth))
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Unauthorized", lifetime.Token);
                return;
            }
            search.Attach(owner);
            receive = ReceiveSearchResultsAsync(socket, owner, search, lifetime.Token);
            while (!receive.IsCompleted && socket.State == WebSocketState.Open)
            {
                var pending = search.PendingState(owner);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new { kind = "search-tick", request = pending.Request, reset = pending.Reset }, SocketJson);
                await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, lifetime.Token);
                await Task.Delay(700, lifetime.Token);
            }
            await receive;
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or JsonException or InvalidDataException or InvalidOperationException) { }
        finally
        {
            lifetime.Cancel();
            search.Detach(owner);
            if (receive is not null)
                try { await receive; }
                catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or JsonException or InvalidDataException or InvalidOperationException) { }
        }
    }

    private async Task ReceiveSearchResultsAsync(WebSocket socket, object owner, BrowserSearchService search, CancellationToken token)
    {
        while (socket.State == WebSocketState.Open)
        {
            using var packet = await ReadSocketJsonAsync(socket, token, 262144);
            if (packet is null) return;
            var root = packet.RootElement;
            search.Touch(owner);
            static string? Read(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (Read(root, "kind") == "search-reset" && root.TryGetProperty("closed", out var closed) &&
                closed.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                search.AcknowledgeReset(owner, Read(root, "id"), closed.GetBoolean());
                continue;
            }
            if (Read(root, "kind") != "search-result") continue;
            SearchResult[]? results = null;
            int? nextStart = null;
            var error = Read(root, "error");
            try
            {
                if (root.TryGetProperty("results", out var items)) results = items.Deserialize<SearchResult[]>(SocketJson);
                if (root.TryGetProperty("nextStart", out var next) && next.ValueKind != JsonValueKind.Null) nextStart = next.GetInt32();
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException) { error = "result"; }
            search.Complete(owner, Read(root, "id"), results, nextStart, error, Read(root, "diagnostic"));
        }
    }
}
