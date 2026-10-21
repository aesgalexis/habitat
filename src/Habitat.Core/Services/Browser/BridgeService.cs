using Habitat.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.WebSockets;

namespace Habitat.Services;

public sealed partial class BridgeService : IAsyncDisposable
{
    private readonly int _port;
    private readonly object _stateLock = new();
    private WebApplication? _server;
    private DateTimeOffset _lastPoll = DateTimeOffset.MinValue;
    private readonly BridgeSession _session;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _lifecycle = new(1);
    private bool _disposed;
    private DateTimeOffset UtcNow => _time.GetUtcNow();
    private object? _connectionOwner;
    private string _connectionIssue = UiText.Get("ReconnectingToChatGPT");
    private static readonly JsonSerializerOptions SocketJson = new(JsonSerializerDefaults.Web);

    public BridgeService(int port = 8765, TimeProvider? timeProvider = null)
    {
        _port = port;
        _time = timeProvider ?? TimeProvider.System;
        _session = new BridgeSession(_time);
        Translation = new Features.Translation.BrowserTranslationService(_time);
        Search = new Features.Search.BrowserSearchService(_time);
        YouTubeSearch = new Features.Search.BrowserSearchService(_time, ComposerDestination.YouTube);
        PairKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public string PairKey { get; }
    internal string? Address => _server?.Urls.SingleOrDefault();
    public string ConnectionIssue { get { lock (_stateLock) return UiText.BridgeMessage(_connectionIssue); } }

    public bool IsConnected
    {
        get { lock (_stateLock) return UtcNow - _lastPoll < BridgeProtocol.ConnectionLifetime; }
    }

    public bool HasMessageInFlight
    {
        get { lock (_stateLock) return _session.Message is not null; }
    }

    public event EventHandler<BridgeEvent>? EventReceived;
    public event EventHandler<string>? StartFailed;

    public async Task StartAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_disposed || _server is not null) return;
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls($"http://127.0.0.1:{_port}");
            var server = builder.Build();
            server.UseWebSockets();
            server.Map("/bridge", HandleSocketAsync);
            server.Map("/translate-bridge", HandleTranslationSocketAsync);
            server.Map("/search-bridge", HandleSearchSocketAsync);
            server.Map("/youtube-bridge", context => HandleSearchSocketAsync(context, YouTubeSearch));
            server.MapGet("/status", (HttpRequest request) =>
            {
                if (!IsAuthorized(request)) return Results.Json(new { error = "Unauthorized" }, statusCode: 403);
                lock (_stateLock)
                {
                    // Read-only diagnostics: must not create a heartbeat or
                    // dequeue/acknowledge messages during verification.
                    return Results.Json(new { connected = UtcNow - _lastPoll < BridgeProtocol.ConnectionLifetime,
                        hasMessageInFlight = _session.Message is not null, translationConnected = Translation.IsConnected,
                        searchConnected = Search.IsConnected,
                        youtubeConnected = YouTubeSearch.IsConnected,
                        protocolVersion = BridgeProtocol.Version, processId = Environment.ProcessId,
                        deliveryPhase = _session.Phase.ToString(),
                        transport = _connectionOwner is null ? "page-http" : "worker-websocket",
                        heartbeatAgeSeconds = _lastPoll == DateTimeOffset.MinValue ? (double?)null : (UtcNow - _lastPoll).TotalSeconds,
                        issue = _connectionIssue });
                }
            });

            server.MapGet("/pair", (HttpRequest request) =>
                request.Headers["X-Habitat-Pair"] == "1"
                    ? Results.Json(new { key = PairKey, protocolVersion = BridgeProtocol.Version })
                    : Results.Json(new { error = "Pairing denied" }, statusCode: StatusCodes.Status403Forbidden));

            server.MapGet("/next", (HttpRequest request) =>
            {
                if (!IsAuthorized(request))
                {
                    return Results.Json(new { error = "Unauthorized" }, statusCode: StatusCodes.Status403Forbidden);
                }
                OutgoingMessage? message;
                lock (_stateLock)
                {
                    _lastPoll = UtcNow;
                    _connectionOwner = null;
                    _connectionIssue = UiText.Get("ReconnectingToChatGPT");
                    // Keep offering an unacknowledged message. If Chrome
                    // reloads or invalidates the content script between this
                    // response and its acknowledgement, the replacement
                    // script can recover the same delivery.
                    message = _session.PendingDelivery;
                }
                return Results.Json(new { message });
            });

            server.MapPost("/event", async (HttpRequest request) =>
            {
                if (!IsAuthorized(request))
                {
                    return Results.Json(new { error = "Unauthorized" }, statusCode: StatusCodes.Status403Forbidden);
                }

                BridgeEvent? bridgeEvent;
                try
                {
                    bridgeEvent = await request.ReadFromJsonAsync<BridgeEvent>();
                }
                catch
                {
                    return Results.Json(new { error = "Invalid JSON" }, statusCode: StatusCodes.Status400BadRequest);
                }

                if (bridgeEvent is null || bridgeEvent.Type is not ("received" or "started" or "response" or "error" or "status" or "model"))
                {
                    return Results.Json(new { error = "Invalid event" }, statusCode: StatusCodes.Status400BadRequest);
                }

                bool accepted;
                lock (_stateLock) accepted = _session.Accept(bridgeEvent);
                if (accepted) EventReceived?.Invoke(this, bridgeEvent);
                return Results.Json(new { ok = true });
            });

            _server = server;
            await server.StartAsync();
        }
        catch (Exception exception)
        {
            if (_server is not null) { await _server.DisposeAsync(); _server = null; }
            StartFailed?.Invoke(this, UiText.Format("CouldNotOpenPort", _port, exception.Message));
        }
        finally { _lifecycle.Release(); }
    }

    public bool TryQueueMessage(string text, out string reason, bool openExternally = false)
    {
        lock (_stateLock)
        {
            if (UtcNow - _lastPoll >= BridgeProtocol.ConnectionLifetime)
            {
                reason = _connectionIssue;
                return false;
            }
            if (openExternally && (_connectionOwner is null || !_supportsExternalOpen))
            {
                reason = UiText.Get("ReloadTheHABITATBrowserExtensionToSendAnd");
                return false;
            }
            if (!_session.TryQueue(text, openExternally))
            {
                reason = UiText.Get("WaitForTheCurrentResponseToFinish");
                return false;
            }
            reason = string.Empty;
            return true;
        }
    }

    public bool TryExpireMessage(TimeSpan timeout)
    {
        lock (_stateLock) return _session.TryExpire(timeout);
    }

    private bool IsAuthorized(HttpRequest request)
    {
        return IsAuthorized(request.Headers["X-Habitat-Key"].ToString());
    }

    private bool IsAuthorized(string supplied)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(PairKey);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    private async Task HandleSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var owner = new object();
        Task? receive = null;
        try
        {
            // Authenticate in the first frame, not in a URL, so the pairing
            // key cannot appear in browser history or request URL logs.
            using var authTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            authTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var auth = await ReadSocketJsonAsync(socket, authTimeout.Token);
            if (auth is null || !auth.RootElement.TryGetProperty("kind", out var kind) || kind.GetString() != "auth" ||
                !auth.RootElement.TryGetProperty("key", out var key) || !IsAuthorized(key.GetString() ?? ""))
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Unauthorized", lifetime.Token);
                return;
            }
            receive = ReceiveHeartbeatsAsync(socket, owner, lifetime.Token);
            while (!receive.IsCompleted && socket.State == WebSocketState.Open)
            {
                OutgoingMessage? message;
                string? openChatId;
                lock (_stateLock) { message = _session.PendingDelivery; openChatId = _openChatId; }
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new { kind = "tick", protocolVersion = BridgeProtocol.Version, message, openChatId }, SocketJson);
                await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, lifetime.Token);
                await Task.Delay(TimeSpan.FromMilliseconds(1200), lifetime.Token);
            }
            await receive;
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or JsonException or InvalidDataException or InvalidOperationException) { }
        finally
        {
            lifetime.Cancel();
            if (receive is not null)
            {
                // Cancel an active reader without discarding an authentication
                // rejection's already-sent close frame.
                socket.Abort();
                try { await receive; }
                catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or JsonException or InvalidDataException or InvalidOperationException) { }
            }
            lock (_stateLock)
            {
                if (ReferenceEquals(_connectionOwner, owner))
                {
                    _lastPoll = DateTimeOffset.MinValue;
                    _connectionOwner = null;
                    _supportsExternalOpen = false;
                }
            }
        }
    }

    private async Task ReceiveHeartbeatsAsync(WebSocket socket, object owner, CancellationToken cancellationToken)
    {
        var lastModel = "";
        while (socket.State == WebSocketState.Open)
        {
            using var data = await ReadSocketJsonAsync(socket, cancellationToken);
            if (data is null) return;
            var root = data.RootElement;
            if (!root.TryGetProperty("kind", out var kind)) continue;
            if (kind.GetString() == "chat-opened")
            {
                lock (_stateLock)
                    if (root.TryGetProperty("id", out var openedId) && openedId.GetString() == _openChatId)
                        _openChatCompletion?.TrySetResult(root.TryGetProperty("opened", out var opened) && opened.ValueKind == JsonValueKind.True);
                continue;
            }
            if (kind.GetString() != "heartbeat") continue;
            var ready = root.TryGetProperty("ready", out var value) && value.ValueKind == JsonValueKind.True;
            lock (_stateLock)
            {
                // Socket traffic alone is NOT a connection. The worker must
                // have received a fresh acknowledgement from the bound page.
                _lastPoll = ready ? UtcNow : DateTimeOffset.MinValue;
                _connectionOwner = owner;
                _supportsExternalOpen = root.TryGetProperty("supportsExternalOpen", out var supportsOpen) && supportsOpen.ValueKind == JsonValueKind.True;
                var issue = root.TryGetProperty("issue", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null;
                _connectionIssue = string.IsNullOrWhiteSpace(issue) ? UiText.Get("ReconnectingToChatGPT") : issue[..Math.Min(issue.Length, 160)];
            }
            if (ready && root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String)
            {
                var label = model.GetString() ?? "";
                if (label.Length > 0 && label.Length <= 100 && label != lastModel)
                {
                    lastModel = label;
                    EventReceived?.Invoke(this, new BridgeEvent("model", null, null, label));
                }
            }
        }
    }

    private static async Task<JsonDocument?> ReadSocketJsonAsync(WebSocket socket, CancellationToken cancellationToken, int maximumBytes = 8192)
    {
        var bytes = new byte[1024];
        using var buffer = new MemoryStream();
        ValueWebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(bytes.AsMemory(), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text || buffer.Length + result.Count > maximumBytes)
                throw new InvalidDataException("Invalid bridge frame.");
            buffer.Write(bytes, 0, result.Count);
        } while (!result.EndOfMessage);
        return JsonDocument.Parse(buffer.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;
            var server = _server;
            _server = null;
            if (server is null) return;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await server.StopAsync(cancellation.Token); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            finally { await server.DisposeAsync(); }
        }
        finally { _lifecycle.Release(); }
    }
}
