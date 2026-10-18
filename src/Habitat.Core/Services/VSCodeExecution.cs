using System.Text.RegularExpressions;

namespace Habitat.Services;

internal sealed record VSCodeExecution(string Id, string Command, string Path, string Terminal, string State,
    long StartedAt, long? EndedAt, int? ExitCode, long ElapsedMs, string Output, bool Truncated)
{
    public bool IsRunning => State is "starting" or "running";
    public string Label => State switch
    {
        "starting" => "Starting",
        "running" => "Running",
        "completed" => "Completed",
        "failed" => "Failed",
        "closed" => "Closed",
        _ => "Status unavailable"
    };
    public string Summary => Label + " · " + TimeSpan.FromMilliseconds(Math.Max(0, ElapsedMs)).ToString(@"hh\:mm\:ss") +
        (State == "failed" && ExitCode.HasValue ? $" · Exit code {ExitCode}" : "");
    public string Preview
    {
        get
        {
            var text = string.Join('\n', Output.TrimEnd().Split('\n').TakeLast(State == "failed" ? 12 : 6));
            return text.Length > 2000 ? text[^2000..] : text;
        }
    }
    public IReadOnlyList<Uri> Links => Regex.Matches(Output, @"https?://[^\s<>""\x00-\x1f]+", RegexOptions.None, TimeSpan.FromMilliseconds(100))
        .Select(match => match.Value.TrimEnd('.', ',', ';', ')', ']', '\''))
        .Distinct().TakeLast(3).Select(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) ? uri : null)
        .Where(uri => uri is not null).Cast<Uri>().ToArray();
}
