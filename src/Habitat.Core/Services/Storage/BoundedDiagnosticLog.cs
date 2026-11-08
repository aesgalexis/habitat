using System.Text;

namespace Habitat.Services;

internal sealed class BoundedDiagnosticLog(string path, int maxBytes = BoundedDiagnosticLog.FileLimit)
{
    internal const int FileLimit = 512 * 1024;
    private static readonly object Gate = new();
    private string? _lastKey;
    private DateTimeOffset _lastWrite;

    internal void Append(string entry, string? repeatKey = null)
    {
        var line = entry + Environment.NewLine;
        var bytes = Encoding.UTF8.GetByteCount(line);
        if (bytes > maxBytes) return;
        lock (Gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (repeatKey is not null && repeatKey == _lastKey && now - _lastWrite < TimeSpan.FromSeconds(30)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                var current = new FileInfo(path);
                if (current.Exists && current.Length + bytes > maxBytes)
                {
                    if (current.Length <= maxBytes) File.Move(path, path + ".previous", overwrite: true);
                    else RotateOversizedLog();
                }
                var previous = new FileInfo(path + ".previous");
                if (previous.Exists && previous.Length > maxBytes) previous.Delete();
                File.AppendAllText(path, line);
                _lastKey = repeatKey;
                _lastWrite = now;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private void RotateOversizedLog()
    {
        var tail = new byte[maxBytes];
        using (var input = File.OpenRead(path))
        {
            input.Seek(-maxBytes, SeekOrigin.End);
            input.ReadExactly(tail);
        }
        // Keep only complete lines when migrating an old, oversized file.
        var firstBreak = Array.IndexOf(tail, (byte)'\n');
        AtomicFile.Write(path + ".previous", tail.AsSpan(firstBreak < 0 ? tail.Length : firstBreak + 1));
        File.Delete(path);
    }
}
