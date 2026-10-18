namespace Habitat.Services;

internal static class AtomicFile
{
    public static void Write(string path, ReadOnlySpan<byte> bytes)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) Replace(temporary, path);
            else File.Move(temporary, path);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static void Replace(string temporary, string destination, Action<string, string>? replace = null)
    {
        replace ??= (source, target) => File.Replace(source, target, null);
        for (var attempt = 0; ; attempt++)
        {
            try { replace(temporary, destination); return; }
            catch (IOException exception) when (attempt < 4 &&
                (exception.HResult & 0xFFFF) is 32 or 33 or 1175 &&
                File.Exists(temporary) && File.Exists(destination))
            {
                // Windows scanners may briefly deny deletion of the replaced
                // file. Retry only errors that leave both originals in place.
                Thread.Sleep(10 << attempt);
            }
        }
    }
}
