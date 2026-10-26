using System.Text.Json;

namespace Habitat.Features.Facebook;

internal sealed record FacebookMessageDiagnostic(string? Page = null, int? Mains = null, int? Headings = null,
    int? NameMatches = null, int? MessageButtons = null, int? MessageLinks = null, int? Popups = null,
    int? IdentityMatches = null, int? Editors = null, int? SendButtons = null,
    int? ProfileAliases = null, int? ChatHeadings = null, int? MenuItems = null);
internal sealed record FacebookMessageTraceEntry(DateTimeOffset Time, string Operation, string Stage,
    string? Status = null, string? Error = null, FacebookMessageDiagnostic? Details = null);

// Small local JSON-lines trace. Never accepts names, URLs, message text or
// arbitrary page/exception strings. Logging cannot prevent a user operation.
internal sealed class FacebookMessageTrace(TimeProvider clock, string? path)
{
    private readonly object _gate = new();
    private FacebookMessageTraceEntry? _last;
    internal FacebookMessageTraceEntry? Last { get { lock (_gate) return _last; } }
    internal void Write(string operation, string stage, string? status = null, string? error = null, FacebookMessageDiagnostic? details = null)
    {
        lock (_gate)
        {
            _last = new(clock.GetUtcNow(), operation, stage, status, error, details);
            if (path is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Move(path, path + ".previous", true);
                File.AppendAllText(path, JsonSerializer.Serialize(_last, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + "\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
