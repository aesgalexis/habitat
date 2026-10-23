using System.Text.Json;
using Habitat.Features.Codex;

namespace Habitat.Services;

// Only successful new-chat deliveries may register an identity. Titles and
// the visible Codex selection are never evidence that HABITAT started a chat.
internal sealed class CodexChatOrigins(string? path)
{
    private sealed record Registry(int Version, string[] ChatIds, CodexChatTarget? Linked = null, bool HasSelection = false);
    private Registry _registry = Load(path);

    public bool Contains(CodexChatTarget target) => _registry.ChatIds.Contains(target.Id, StringComparer.Ordinal);
    public CodexChatTarget? Linked => _registry.Linked;
    public bool HasSelection => _registry.HasSelection;
    public CodexChatTarget? LatestAvailable(IReadOnlyList<CodexChatTarget> targets) =>
        _registry.ChatIds.Reverse().Select(id => targets.FirstOrDefault(target => target.Id == id)).FirstOrDefault(target => target is not null);

    public void Register(CodexChatTarget target, bool link = false)
    {
        if (!CodexReadClient.ValidId(target.Id)) throw new InvalidDataException("Invalid Codex chat identifier.");
        if (Contains(target) && !link) return;
        // Commit to memory only after persistence succeeds. Failure must never
        // turn an unconfirmed delivery into a linked conversation.
        Save(_registry with { ChatIds = Contains(target) ? _registry.ChatIds : [.. _registry.ChatIds, target.Id],
            Linked = link ? target : Linked, HasSelection = link || HasSelection });
    }

    public void Select(CodexChatTarget? target)
    {
        if (target is not null && !Contains(target)) throw new InvalidDataException("Cannot link an unregistered Codex chat.");
        Save(_registry with { Linked = target, HasSelection = true });
    }

    private void Save(Registry registry)
    {
        if (path is not null) AtomicFile.Write(path, JsonSerializer.SerializeToUtf8Bytes(registry));
        _registry = registry;
    }

    private static Registry Load(string? path)
    {
        if (path is null) return new(1, [], HasSelection: true);
        try
        {
            var stored = JsonSerializer.Deserialize<Registry>(File.ReadAllBytes(path));
            if (stored is { Version: 1, ChatIds: { } ids } && ids.All(CodexReadClient.ValidId) &&
                (stored.Linked is null || stored.HasSelection && ids.Contains(stored.Linked.Id, StringComparer.Ordinal)))
                return stored;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
        // Missing, unreadable or invalid provenance grants no access.
        return new(1, [], HasSelection: true);
    }
}
