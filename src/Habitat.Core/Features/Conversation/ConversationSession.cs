namespace Habitat;

internal enum MessageTone { User, Assistant, Muted }
internal sealed record ConversationMessage(string Author, string Body, MessageTone Tone);

internal sealed class ConversationSession(int maxMessages = 200, int maxCharacters = 2_000_000)
{
    private readonly int _messageLimit = maxMessages > 0 ? maxMessages : throw new ArgumentOutOfRangeException(nameof(maxMessages));
    private readonly int _characterLimit = maxCharacters > 0 ? maxCharacters : throw new ArgumentOutOfRangeException(nameof(maxCharacters));
    private readonly List<ConversationMessage> _messages = [];
    private long _characters;
    public IReadOnlyList<ConversationMessage> Messages => _messages;
    public bool HasActiveConversation { get; private set; }
    public bool HasTrimmedMessages { get; private set; }
    public int Add(string author, string body, MessageTone tone)
    {
        _messages.Add(new(author, body, tone));
        _characters += (long)author.Length + body.Length;
        HasActiveConversation |= tone is MessageTone.User or MessageTone.Assistant;
        var removed = 0;
        // Keep the newest message intact, even when that message alone exceeds
        // the budget. The original conversation remains available in ChatGPT.
        while (_messages.Count > _messageLimit || _messages.Count > 1 && _characters > _characterLimit)
        {
            _characters -= (long)_messages[0].Author.Length + _messages[0].Body.Length;
            _messages.RemoveAt(0);
            removed++;
        }
        HasTrimmedMessages |= removed > 0;
        return removed;
    }
    public void Clear() { _messages.Clear(); _characters = 0; HasActiveConversation = false; HasTrimmedMessages = false; }
}
