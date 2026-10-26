using Habitat.Features.Facebook;
using Microsoft.AspNetCore.Http;
using System.Net.WebSockets;
using System.Text.Json;

namespace Habitat.Services;

public sealed partial class BridgeService
{
    internal FacebookContactsService Facebook { get; }
    internal FacebookMessagingService FacebookMessaging { get; }

    private sealed record MessageDiagnosticInput(string ContactName, string? Text, bool InspectOnly = false);
    private async Task<IResult> HandleFacebookMessageAsync(HttpRequest request)
    {
        // Native diagnostic client only. Browsers cannot turn a cross-origin
        // request into a send, even if they somehow obtain a pairing token.
        if (!IsAuthorized(request) || request.Headers.ContainsKey("Origin") || request.Headers["X-Habitat-Action"] != "send-facebook-message")
            return Results.Json(new { error = "Unauthorized" }, statusCode: 403);
        if (request.ContentLength is null or <= 0 or > 32768) return Results.BadRequest(new { error = "invalid-request" });
        MessageDiagnosticInput? input;
        try { input = await JsonSerializer.DeserializeAsync<MessageDiagnosticInput>(request.Body, SocketJson, request.HttpContext.RequestAborted); }
        catch (JsonException) { return Results.BadRequest(new { error = "invalid-request" }); }
        if (input is null || string.IsNullOrWhiteSpace(input.ContactName) || input.ContactName.Length > 200 ||
            !input.InspectOnly && (string.IsNullOrWhiteSpace(input.Text) || input.Text.Length > 5000)) return Results.BadRequest(new { error = "invalid-request" });
        static string Name(string value) => string.Join(' ', value.Normalize(System.Text.NormalizationForm.FormC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        try
        {
            var contacts = await Facebook.ReadAsync(request.HttpContext.RequestAborted);
            var matches = contacts.Where(contact => Name(contact.Name) == Name(input.ContactName)).ToArray();
            if (matches.Length != 1 || matches[0].Link is null)
                return Results.Json(new FacebookMessageResult("not-sent", matches.Length > 1 ? "ambiguous-contact" : "contact-not-found"));
            return Results.Json(input.InspectOnly ?
                await FacebookMessaging.InspectAsync(matches[0].Link!, matches[0].Name, request.HttpContext.RequestAborted) :
                await FacebookMessaging.SendAsync(matches[0].Link!, matches[0].Name, input.Text!, request.HttpContext.RequestAborted));
        }
        catch (InvalidOperationException) { return Results.Json(new FacebookMessageResult("not-sent", "unavailable")); }
    }

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
            FacebookMessaging.Attach(owner, auth.RootElement.TryGetProperty("facebookMessagingProtocol", out var messaging) &&
                messaging.ValueKind == JsonValueKind.Number && messaging.TryGetInt32(out var messagingVersion) && messagingVersion == 1);
            receive = ReceiveFacebookAsync(socket, owner, lifetime.Token);
            while (!receive.IsCompleted && socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { kind = "facebook-tick", request = Facebook.Pending(owner), message = FacebookMessaging.Pending(owner) }, SocketJson).AsMemory(),
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
            FacebookMessaging.Detach(owner);
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
            FacebookMessaging.Touch(owner);
            var root = packet.RootElement;
            static string? Read(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var kind = Read(root, "kind");
            if (kind == "facebook-message-progress")
            {
                FacebookMessageDiagnostic? details = null;
                try { if (root.TryGetProperty("diagnostic", out var metadata)) details = metadata.Deserialize<FacebookMessageDiagnostic>(SocketJson); }
                catch (JsonException) { }
                FacebookMessaging.Progress(owner, Read(root, "id"), Read(root, "stage"), details);
                continue;
            }
            if (kind == "facebook-message-result")
            {
                FacebookMessaging.Complete(owner, Read(root, "id"), Read(root, "status"), Read(root, "error"));
                continue;
            }
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
