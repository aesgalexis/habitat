namespace Habitat.Services;

internal sealed class ComposerDrafts
{
    private readonly record struct Draft(string Text, long Revision);
    internal readonly record struct Submission(ComposerDestination Destination, long Revision);

    private readonly Dictionary<ComposerDestination, Draft> _local = new();
    private Draft _shared = new("", 0);
    private long _revision;

    private Draft Get(ComposerDestination destination) => _local.GetValueOrDefault(destination, _shared);
    public string Text(ComposerDestination destination) => Get(destination).Text;

    public void Edit(ComposerDestination destination, string text)
    {
        if (Get(destination).Text == text) return;
        var draft = new Draft(text, ++_revision);
        if (!_local.ContainsKey(destination)) _shared = draft;
        // Clearing a used field starts a shared draft if no other draft is waiting.
        else if (text.Length == 0 && _shared.Text.Length == 0) _local.Remove(destination);
        else _local[destination] = draft;
    }

    public Submission Capture(ComposerDestination destination) => new(destination, Get(destination).Revision);

    public void Accept(Submission submission)
    {
        var draft = Get(submission.Destination);
        if (draft.Revision != submission.Revision || _local.ContainsKey(submission.Destination)) return;
        _local[submission.Destination] = draft;
        _shared = new("", ++_revision);
    }

    public void Clear(Submission submission)
    {
        // An old action must never clear newer input, even if its text is identical.
        if (_local.TryGetValue(submission.Destination, out var draft) && draft.Revision == submission.Revision)
            _local.Remove(submission.Destination);
    }
}
