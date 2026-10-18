namespace Habitat.Activation;

public interface IClipboardBackup : IDisposable { uint Sequence { get; } }
public interface ISelectionDesktop
{
    nint Foreground { get; }
    bool IsHabitat(nint window);
    bool WaitForKeyRelease(nint source);
    bool HasSelection(nint source);
    IClipboardBackup? Backup();
    uint Sequence { get; }
    bool Copy(nint source);
    // Returns true only for a fresh clipboard owned by the original process.
    bool ReadCopy(nint source, uint before, out uint sequence, out string? text);
    bool Restore(IClipboardBackup backup, uint expectedSequence);
    void Pause();
    void Report(string message);
}

public sealed class SelectionCapture(ISelectionDesktop desktop)
{
    public string? Capture()
    {
        var source = desktop.Foreground;
        if (source == 0 || desktop.IsHabitat(source) || !desktop.WaitForKeyRelease(source) || !desktop.HasSelection(source)) return null;
        using var backup = desktop.Backup();
        if (backup is null) return null; // Never risk a clipboard we cannot restore.
        var before = backup.Sequence;
        uint copied = 0;
        try
        {
            if (desktop.Foreground != source || desktop.Sequence != before || !desktop.Copy(source)) return null;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                desktop.Pause();
                if (desktop.ReadCopy(source, before, out copied, out var text))
                    return desktop.Foreground == source && text is { Length: > 0 and <= ActivationChannel.MaxTextLength } ? text : null;
                if (desktop.Foreground != source || desktop.Sequence != before) break;
            }
            return null;
        }
        finally
        {
            if (copied == 0 && desktop.Sequence != before)
                desktop.ReadCopy(source, before, out copied, out _);
            // A second clipboard write by the user/app wins over our old backup.
            if (copied != 0 && !desktop.Restore(backup, copied))
                desktop.Report("The clipboard changed or could not be restored.");
        }
    }
}
