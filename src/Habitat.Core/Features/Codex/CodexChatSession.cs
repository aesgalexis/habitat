namespace Habitat.Features.Codex;

// UI-independent identity and turn tracking. A late reply can update its own
// cached chat, but can never appear under a different selected chat.
internal sealed class CodexChatSession(int maxCachedChats = 32)
{
    private readonly int _capacity = maxCachedChats >= 2 ? maxCachedChats : throw new ArgumentOutOfRangeException(nameof(maxCachedChats));
    private sealed record Pending(CodexChatTarget Target, string? PreviousTurnId, string Text, string? TurnId = null);
    private readonly Dictionary<string, CodexChatHistory> _history = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Chat, string Turn), string> _originalText = new();
    private readonly Dictionary<string, string> _issues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _access = new(StringComparer.Ordinal);
    private long _sequence;
    private Pending? _pending;
    public CodexChatTarget? Selected { get; private set; }
    public CodexChatTarget? PendingTarget => _pending?.Target;
    public bool IsWaiting => _pending is not null;
    public CodexChatHistory? Current => Selected is { } target ? _history.GetValueOrDefault(target.Id) : null;
    public string Issue => _issues.GetValueOrDefault(Selected?.Id ?? "", "");

    internal int CachedChatCount => _access.Count;
    internal int OriginalTextCount => _originalText.Count;
    public void Select(CodexChatTarget? target) { Selected = target; Touch(target); }
    public void SetIssue(CodexChatTarget? target, string message)
    {
        if (message.Length == 0) _issues.Remove(target?.Id ?? "");
        else _issues[target?.Id ?? ""] = message;
        Touch(target);
    }

    public void Accept(CodexChatSubmission submission, string text)
    {
        if (submission.Result != Services.CodexDeliveryResult.Sent || submission.Target is not { } target) return;
        _pending = new(target, submission.PreviousTurnId, text);
        _issues.Remove(target.Id);
        if (Selected is null) Selected = target;
        Touch(target);
    }

    public void Observe(CodexChatHistory history)
    {
        history = history with { Turns = history.Turns.TakeLast(20).ToArray() };
        _history[history.Target.Id] = history;
        var turns = history.Turns.Select(turn => turn.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _originalText.Keys.Where(key => key.Chat == history.Target.Id && !turns.Contains(key.Turn)).ToArray())
            _originalText.Remove(key);
        Touch(history.Target);
        if (_issues.GetValueOrDefault(history.Target.Id) == "Could not read the Codex response. Retrying…")
            _issues.Remove(history.Target.Id);
        if (_pending is not { } pending || pending.Target.Id != history.Target.Id) return;
        _issues.Remove(history.Target.Id);
        var turn = pending.TurnId is null ? FirstNewTurn(history.Turns, pending.PreviousTurnId) :
            history.Turns.FirstOrDefault(item => item.Id == pending.TurnId);
        if (turn is null) return;
        _originalText[(history.Target.Id, turn.Id)] = pending.Text;
        _pending = pending with { TurnId = turn.Id };
        if (turn.IsRunning) return;
        if (turn.Status == "completed")
            SetIssue(history.Target, turn.Messages.Any(message => message.Author == "Codex" && message.Text.Length > 0)
                ? "" : "Codex finished without a text reply. Open the chat in Codex for details.");
        else if (turn.Status is "failed" or "interrupted")
            SetIssue(history.Target, "The Codex turn did not complete. Open the chat in Codex for details.");
        else return;
        _pending = null;
    }

    private static CodexChatTurn? FirstNewTurn(IReadOnlyList<CodexChatTurn> turns, string? previous)
    {
        if (previous is null) return turns.FirstOrDefault();
        for (var index = 0; index < turns.Count - 1; index++)
            if (turns[index].Id == previous) return turns[index + 1];
        return null;
    }

    public IReadOnlyList<CodexChatMessage> Messages
    {
        get
        {
            if (Current is not { } current) return [];
            var messages = new List<CodexChatMessage>();
            foreach (var turn in current.Turns)
            {
                var firstUser = true;
                foreach (var message in turn.Messages)
                {
                    if (message.Author == "YOU" && firstUser && _originalText.TryGetValue((current.Target.Id, turn.Id), out var original))
                        messages.Add(message with { Text = original });
                    else messages.Add(message);
                    if (message.Author == "YOU") firstUser = false;
                }
            }
            return messages;
        }
    }

    public void StopWaiting()
    {
        if (_pending is { } pending) SetIssue(pending.Target, "Stopped waiting. Codex may still be working.");
        _pending = null;
    }

    private void Touch(CodexChatTarget? target)
    {
        if (target is null) return;
        _access[target.Id] = ++_sequence;
        while (_access.Count > _capacity)
        {
            var oldest = _access.Where(entry => entry.Key != Selected?.Id && entry.Key != _pending?.Target.Id)
                .MinBy(entry => entry.Value).Key;
            _access.Remove(oldest);
            _history.Remove(oldest);
            _issues.Remove(oldest);
            foreach (var key in _originalText.Keys.Where(key => key.Chat == oldest).ToArray()) _originalText.Remove(key);
        }
    }
}
