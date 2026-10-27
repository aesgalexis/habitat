using System.Text.Json;
using System.Text.RegularExpressions;

namespace Habitat.Services;

internal sealed class UpdateDiagnosticLog(string path)
{
    private readonly object _gate = new();
    internal string Path { get; } = System.IO.Path.GetFullPath(path);
    internal static UpdateDiagnosticLog ForDirectory(string directory) => new(System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(directory)!, "diagnostics", System.IO.Path.GetFileName(directory) + ".jsonl"));

    internal void Record(string phase, Exception? exception = null, string? output = null, string? error = null, int? exitCode = null)
    {
        // Never let a diagnostic failure replace the actual update result.
        try
        {
            var entry = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, phase,
                exception = exception?.GetType().FullName, hresult = exception?.HResult,
                message = exception is null ? null : Redact(exception.Message),
                stdout = output is null ? null : Redact(output), stderr = error is null ? null : Redact(error), exitCode });
            lock (_gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, entry + Environment.NewLine);
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
    }

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
