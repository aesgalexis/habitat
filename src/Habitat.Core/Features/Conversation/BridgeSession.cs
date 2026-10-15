using Habitat.Models;
using System.Security.Cryptography;

namespace Habitat.Services;

internal enum DeliveryPhase { Idle, Queued, Received, Responding }

// The transport owns synchronization. This state machine has no sockets,
// controls or wall-clock dependency and can be tested without waiting.
internal sealed class BridgeSession(TimeProvider time)
{
    public OutgoingMessage? Message { get; private set; }
    public DeliveryPhase Phase { get; private set; }
    private DateTimeOffset _queuedAt;
    public OutgoingMessage? PendingDelivery => Phase == DeliveryPhase.Queued ? Message : null;

    public bool TryQueue(string text)
    {
        if (Message is not null) return false;
        Message = new(Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(), text);
        _queuedAt = time.GetUtcNow();
        Phase = DeliveryPhase.Queued;
        return true;
    }

    public bool Accept(BridgeEvent data)
    {
        if (data.Type is "status" or "model") return true;
        if (Message is null || string.IsNullOrEmpty(data.Id) || data.Id != Message.Id) return false;
        switch (data.Type)
        {
            case "received":
                if (Phase == DeliveryPhase.Queued) Phase = DeliveryPhase.Received;
                return true;
            case "started": Phase = DeliveryPhase.Responding; return true;
            case "response" or "error": Clear(); return true;
            default: return false;
        }
    }

    public bool TryExpire(TimeSpan timeout)
    {
        if (Message is null || time.GetUtcNow() - _queuedAt <= timeout) return false;
        Clear(); return true;
    }
    private void Clear() { Message = null; Phase = DeliveryPhase.Idle; }
}

internal static class BridgeProtocol
{
    public const int Version = 1;
    public static readonly TimeSpan ConnectionLifetime = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(4);
}
