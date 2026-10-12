using Habitat.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Habitat.Services;

public sealed class BridgeService : IAsyncDisposable
{
    private const int Port = 8765;
    private readonly ConcurrentQueue<OutgoingMessage> _pending = new();
    private readonly object _stateLock = new();
    private WebApplication? _server;
    private DateTimeOffset _lastPoll = DateTimeOffset.MinValue;
    private OutgoingMessage? _inflight;
    private DateTimeOffset? _inflightSince;

    public BridgeService()
    {
        PairKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public string PairKey { get; }

    public bool IsConnected
    {
        get { lock (_stateLock) return DateTimeOffset.UtcNow - _lastPoll < TimeSpan.FromSeconds(4); }
    }

    public bool HasMessageInFlight
    {
        get { lock (_stateLock) return _inflight is not null; }
    }

    public event EventHandler<BridgeEvent>? EventReceived;
    public event EventHandler<string>? StartFailed;

    public async Task StartAsync()
    {
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls($"http://127.0.0.1:{Port}");
            var server = builder.Build();

            server.MapGet("/pair", (HttpRequest request) =>
                request.Headers["X-Habitat-Pair"] == "1"
                    ? Results.Json(new { key = PairKey })
                    : Results.Json(new { error = "Pairing denied" }, statusCode: StatusCodes.Status403Forbidden));

            server.MapGet("/next", (HttpRequest request) =>
            {
                if (!IsAuthorized(request))
                {
                    return Results.Json(new { error = "Unauthorized" }, statusCode: StatusCodes.Status403Forbidden);
                }
                lock (_stateLock) _lastPoll = DateTimeOffset.UtcNow;
                _pending.TryDequeue(out var message);
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

                if (bridgeEvent is null || bridgeEvent.Type is not ("started" or "response" or "error" or "status"))
                {
                    return Results.Json(new { error = "Invalid event" }, statusCode: StatusCodes.Status400BadRequest);
                }

                var accepted = bridgeEvent.Type == "status";
                lock (_stateLock)
                {
                    if (bridgeEvent.Type != "status" && bridgeEvent.Id == _inflight?.Id)
                    {
                        accepted = true;
                        if (bridgeEvent.Type is "response" or "error")
                        {
                            _inflight = null;
                            _inflightSince = null;
                        }
                    }
                }
                if (accepted) EventReceived?.Invoke(this, bridgeEvent);
                return Results.Json(new { ok = true });
            });

            _server = server;
            await server.StartAsync();
        }
        catch (Exception exception)
        {
            StartFailed?.Invoke(this, $"No se puede abrir el puerto {Port}: {exception.Message}");
        }
    }

    public bool TryQueueMessage(string text, out string reason)
    {
        lock (_stateLock)
        {
            if (DateTimeOffset.UtcNow - _lastPoll >= TimeSpan.FromSeconds(4))
            {
                reason = "Reconectando con ChatGPT…";
                return false;
            }
            if (_inflight is not null)
            {
                reason = "Espera a que termine la respuesta";
                return false;
            }
            _inflight = new OutgoingMessage(Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(), text);
            _inflightSince = DateTimeOffset.UtcNow;
            _pending.Enqueue(_inflight);
            reason = string.Empty;
            return true;
        }
    }

    public bool TryExpireMessage(TimeSpan timeout)
    {
        lock (_stateLock)
        {
            if (_inflight is null || _inflightSince is null || DateTimeOffset.UtcNow - _inflightSince <= timeout)
            {
                return false;
            }
            _inflight = null;
            _inflightSince = null;
            return true;
        }
    }

    private bool IsAuthorized(HttpRequest request)
    {
        var supplied = request.Headers["X-Habitat-Key"].ToString();
        var expectedBytes = Encoding.UTF8.GetBytes(PairKey);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is null) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await _server.StopAsync(cancellation.Token);
        await _server.DisposeAsync();
        _server = null;
    }
}
