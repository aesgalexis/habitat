namespace Habitat.Features.Codex;

// UI-independent identity and turn tracking. A late reply can update its own
// cached chat, but can never appear under a different selected chat.
internal sealed class CodexChatSession
{
    private sealed record Pending(CodexChatTarget Target, string? PreviousTurnId, string Text, string? TurnId = null);
    private readonly Dictionary<string, CodexChatHistory> _history = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Chat, string Turn), string> _originalText = new();
    private readonly Dictionary<string, string> _issues = new(StringComparer.Ordinal);
    private Pending? _pending;
    public CodexChatTarget? Selected { get; private set; }
    public CodexChatTarget? PendingTarget => _pending?.Target;
    public bool IsWaiting => _pending is not null;
    public CodexChatHistory? Current => Selected is { } target ? _history.GetValueOrDefault(target.Id) : null;
    public string Issue => _issues.GetValueOrDefault(Selected?.Id ?? "", "");

    public void Select(CodexChatTarget? target) => Selected = target;
    public void SetIssue(CodexChatTarget? target, string message) => _issues[target?.Id ?? ""] = message;

    public void Accept(CodexChatSubmission submission, string text)
    {
        if (submission.Result != Services.CodexDeliveryResult.Sent || submission.Target is not { } target) return;
        _pending = new(target, submission.PreviousTurnId, text);
        _issues.Remove(target.Id);
        if (Selected is null) Selected = target;
    }

    public void Observe(CodexChatHistory history)
    {
        _history[history.Target.Id] = history;
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
}
