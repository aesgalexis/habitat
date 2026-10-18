using System.Diagnostics;

namespace Habitat.Services;

internal sealed class RunLauncher
{
    private readonly Func<ProcessStartInfo, bool> _start;
    private readonly string _workingDirectory;

    public RunLauncher(string? workingDirectory = null, Func<ProcessStartInfo, bool>? start = null)
    {
        _workingDirectory = workingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _start = start ?? (info =>
        {
            // The Windows shell can accept an action without returning a new process.
            using var process = Process.Start(info);
            return true;
        });
    }

    public bool Run(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.IndexOfAny(['\0', '\r', '\n']) >= 0) return false;
        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        if (string.IsNullOrWhiteSpace(text) || text.IndexOfAny(['\0', '\r', '\n']) >= 0) return false;
        string target;
        string arguments;
        if (text.StartsWith('"'))
        {
            var closingQuote = text.IndexOf('"', 1);
            if (closingQuote <= 1 || closingQuote + 1 < text.Length && !char.IsWhiteSpace(text[closingQuote + 1])) return false;
            target = text[1..closingQuote];
            arguments = text[(closingQuote + 1)..].TrimStart();
        }
        else if (File.Exists(text) || Directory.Exists(text))
        {
            target = text;
            arguments = "";
        }
        else
        {
            var separator = 0;
            while (separator < text.Length && !char.IsWhiteSpace(text[separator])) separator++;
            target = text[..separator];
            arguments = text[separator..].TrimStart();
            if (target.Contains('"')) return false;
        }
        return _start(new ProcessStartInfo(target)
        {
            Arguments = arguments, UseShellExecute = true, WorkingDirectory = _workingDirectory
        });
    }

    public bool Open() => _start(new ProcessStartInfo(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "rundll32.exe"))
    {
        Arguments = "shell32.dll,#61", UseShellExecute = true, WorkingDirectory = _workingDirectory
    });
}
