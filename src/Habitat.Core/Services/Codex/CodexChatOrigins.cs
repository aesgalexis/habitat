using System.Text.Json;
using Habitat.Features.Codex;

namespace Habitat.Services;

// Only successful new-chat deliveries may register an identity. Titles and
// the visible Codex selection are never evidence that HABITAT started a chat.
internal sealed class CodexChatOrigins(string? path)
{
    private sealed record Registry(int Version, string[] ChatIds);
    private readonly HashSet<string> _ids = Load(path);

    public bool Contains(CodexChatTarget target) => _ids.Contains(target.Id);

    public void Register(CodexChatTarget target)
    {
        if (!CodexReadClient.ValidId(target.Id)) throw new InvalidDataException("Invalid Codex chat identifier.");
        if (_ids.Contains(target.Id)) return;
        // Commit to memory only after persistence succeeds. Failure must never
        // turn an unconfirmed delivery into a linked conversation.
        if (path is not null)
            AtomicFile.Write(path, JsonSerializer.SerializeToUtf8Bytes(new Registry(1, [.. _ids, target.Id])));
        _ids.Add(target.Id);
    }

    private static HashSet<string> Load(string? path)
    {
        if (path is null) return new(StringComparer.Ordinal);
        try
        {
            var stored = JsonSerializer.Deserialize<Registry>(File.ReadAllBytes(path));
            if (stored is { Version: 1, ChatIds: { } ids } && ids.All(CodexReadClient.ValidId))
                return new(ids, StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
        // Missing, unreadable or invalid provenance grants no access.
        return new(StringComparer.Ordinal);
    }
}
