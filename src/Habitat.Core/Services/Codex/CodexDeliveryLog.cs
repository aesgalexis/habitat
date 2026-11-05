namespace Habitat.Services;

// Callers supply phase names, counts and exception types/HRESULTs only.
// Never pass instructions, chat titles, credentials or server response bodies.
internal sealed class CodexDeliveryLog(string path)
{
    private const int MaximumBytes = 1024 * 1024;
    private readonly object _gate = new();

    internal void Write(string message)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length >= MaximumBytes)
                    File.Move(path, path + ".previous", overwrite: true);
                var line = message.Replace('\r', ' ').Replace('\n', ' ');
                if (line.Length > 1000) line = line[..1000];
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {line}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
