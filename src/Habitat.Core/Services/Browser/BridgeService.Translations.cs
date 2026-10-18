using Habitat.Features.Translation;
using Microsoft.AspNetCore.Http;
using System.Net.WebSockets;
using System.Text.Json;

namespace Habitat.Services;

public sealed partial class BridgeService
{
    internal BrowserTranslationService Translation { get; }

    private async Task HandleTranslationSocketAsync(HttpContext context)
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
                !IsAuthorized(key.GetString() ?? "") || !auth.RootElement.TryGetProperty("translationProtocol", out var protocol) ||
                !protocol.TryGetInt32(out var version) || version != 1 || !IsBrowserAllowed(context.Request, auth))
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Unauthorized", lifetime.Token);
                return;
            }
            Translation.Attach(owner);
            receive = ReceiveTranslationResultsAsync(socket, owner, lifetime.Token);
            while (!receive.IsCompleted && socket.State == WebSocketState.Open)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new { kind = "translation-tick", request = Translation.Pending(owner) }, SocketJson);
                await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, lifetime.Token);
                await Task.Delay(700, lifetime.Token);
            }
            await receive;
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or JsonException or InvalidDataException or InvalidOperationException) { }
        finally
        {
            lifetime.Cancel();
            Translation.Detach(owner);
            if (receive is not null)
                try { await receive; }
                catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or JsonException or InvalidDataException or InvalidOperationException) { }
        }
    }

    private async Task ReceiveTranslationResultsAsync(WebSocket socket, object owner, CancellationToken token)
    {
        while (socket.State == WebSocketState.Open)
        {
            using var packet = await ReadSocketJsonAsync(socket, token, 65536);
            if (packet is null) return;
            var root = packet.RootElement;
            Translation.Touch(owner);
            if (!root.TryGetProperty("kind", out var kind) || kind.GetString() != "translation-result") continue;
            static string? Read(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            Translation.Complete(owner, Read(root, "id"), Read(root, "text"), Read(root, "error"), Read(root, "diagnostic"));
        }
    }
}
