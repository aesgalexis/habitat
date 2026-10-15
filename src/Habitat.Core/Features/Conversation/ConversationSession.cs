namespace Habitat;

internal enum MessageTone { User, Assistant, Muted }
internal sealed record ConversationMessage(string Author, string Body, MessageTone Tone);

internal sealed class ConversationSession
{
    private readonly List<ConversationMessage> _messages = [];
    public IReadOnlyList<ConversationMessage> Messages => _messages;
    public bool HasActiveConversation { get; private set; }
    public void Add(string author, string body, MessageTone tone)
    {
        _messages.Add(new(author, body, tone));
        HasActiveConversation |= tone is MessageTone.User or MessageTone.Assistant;
    }
    public void Clear() { _messages.Clear(); HasActiveConversation = false; }
}
