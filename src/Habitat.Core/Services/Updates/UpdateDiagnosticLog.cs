using System.Text.Json;
using System.Text.RegularExpressions;

namespace Habitat.Services;

internal sealed class UpdateDiagnosticLog(string path)
{
    private readonly BoundedDiagnosticLog _sink = new(path);
    internal string Path { get; } = System.IO.Path.GetFullPath(path);
    internal static UpdateDiagnosticLog ForDirectory(string directory) => new(System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(directory)!, "diagnostics", System.IO.Path.GetFileName(directory) + ".jsonl"));

    internal void Record(string phase, Exception? exception = null, string? output = null, string? error = null, int? exitCode = null)
    {
        // Never let a diagnostic failure replace the actual update result.
        try
        {
            var entry = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, phase,
                version = typeof(UpdateDiagnosticLog).Assembly.GetName().Version?.ToString(),
                exception = exception?.GetType().FullName, hresult = exception?.HResult,
                message = exception is null ? null : Redact(exception.Message),
                stdout = output is null ? null : Redact(output), stderr = error is null ? null : Redact(error), exitCode });
            _sink.Append(entry);
            TrimAttempts();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
    }

    private void TrimAttempts()
    {
        if (!IsAttemptLog(System.IO.Path.GetFileName(Path))) return;
        var directory = new DirectoryInfo(System.IO.Path.GetDirectoryName(Path)!);
        if (!directory.Exists || directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
        var retired = directory.EnumerateFiles("update-*.jsonl")
            .Where(file => IsAttemptLog(file.Name) && !file.Attributes.HasFlag(FileAttributes.ReparsePoint) &&
                !file.FullName.Equals(Path, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.LastWriteTimeUtc).ThenBy(file => file.Name, StringComparer.Ordinal)
            .Skip(11).ToArray();
        foreach (var file in retired)
        {
            file.Delete();
            var previous = new FileInfo(file.FullName + ".previous");
            if (previous.Exists && !previous.Attributes.HasFlag(FileAttributes.ReparsePoint)) previous.Delete();
        }
    }

    private static bool IsAttemptLog(string name) =>
        name.StartsWith("update-", StringComparison.Ordinal) && name.EndsWith(".jsonl", StringComparison.Ordinal) &&
        Guid.TryParseExact(name.AsSpan(7, name.Length - 13), "N", out _);

    internal static string Redact(string text)
    {
        text = Regex.Replace(text, @"https?://[^\s'""<>]+", "<url>", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"[A-Za-z]:[\\/][^\r\n'""<>]*|\\\\[^\r\n'""<>]+", "<path>");
        text = Regex.Replace(text, @"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", "<email>");
        text = Regex.Replace(text, "(?i)(authorization|password|token|secret|key)[\\\"']*\\s*[:=]\\s*([\\\"']).*?\\2", "$1=<redacted>");
        text = Regex.Replace(text, "(?i)(authorization|password|token|secret|key)[\\\"' ]*[:=][\\\"' ]*[^,\\s}]+", "$1=<redacted>");
        return text.Length <= 4096 ? text : text[..4096] + " [truncated]";
    }
}
