using Habitat.Features.Facebook;
using Microsoft.AspNetCore.Http;
using System.Net.WebSockets;
using System.Text.Json;

namespace Habitat.Services;

public sealed partial class BridgeService
{
    internal FacebookContactsService Facebook { get; }

    private async Task HandleFacebookSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var owner = new object();
        Task? receive = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var auth = await ReadSocketJsonAsync(socket, timeout.Token);
            if (auth is null || !auth.RootElement.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String ||
                !IsAuthorized(key.GetString() ?? "") || !auth.RootElement.TryGetProperty("facebookProtocol", out var protocol) ||
                protocol.ValueKind != JsonValueKind.Number || !protocol.TryGetInt32(out var version) || version != 1)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Unauthorized", lifetime.Token);
                return;
            }
            Facebook.Attach(owner, auth.RootElement.TryGetProperty("extensionVersion", out var extensionVersion) &&
                extensionVersion.ValueKind == JsonValueKind.String ? extensionVersion.GetString() : null);
            receive = ReceiveFacebookAsync(socket, owner, lifetime.Token);
            while (!receive.IsCompleted && socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { kind = "facebook-tick", request = Facebook.Pending(owner) }, SocketJson).AsMemory(),
                    WebSocketMessageType.Text, true, lifetime.Token);
                await Task.Delay(700, lifetime.Token);
            }
            await receive;
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or JsonException or InvalidDataException or InvalidOperationException) { }
        finally
        {
            lifetime.Cancel();
            Facebook.Detach(owner);
            if (receive is not null)
                try { await receive; }
                catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or JsonException or InvalidDataException or InvalidOperationException) { }
        }
    }

    private async Task ReceiveFacebookAsync(WebSocket socket, object owner, CancellationToken token)
    {
        while (socket.State == WebSocketState.Open)
        {
            using var packet = await ReadSocketJsonAsync(socket, token, 262144);
            if (packet is null) return;
            Facebook.Touch(owner);
            var root = packet.RootElement;
            static string? Read(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var kind = Read(root, "kind");
            if (kind is not ("facebook-result" or "facebook-progress")) continue;
            if (root.TryGetProperty("diagnostic", out var diagnostic))
            {
                try
                {
                    if (diagnostic.Deserialize<FacebookReadDiagnostic>(SocketJson) is { } metadata)
                        Facebook.Progress(owner, Read(root, "id"), metadata);
                }
                catch (JsonException) { }
            }
            if (kind != "facebook-result") continue;
            FacebookContact[]? contacts = null;
            var error = Read(root, "error");
            try { if (root.TryGetProperty("contacts", out var items)) contacts = items.Deserialize<FacebookContact[]>(SocketJson); }
            catch (JsonException) { error = "result"; }
            Facebook.Complete(owner, Read(root, "id"), contacts, error);
        }
    }
}
