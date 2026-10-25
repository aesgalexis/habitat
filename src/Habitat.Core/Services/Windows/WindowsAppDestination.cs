using System.Diagnostics;

namespace Habitat.Services;

internal sealed record WindowsAppDestination(string Id, string Name, string Target, bool Packaged = false, bool PassText = false)
{
    public static bool IsId(string? id) => id is not null && id.StartsWith("windows:", StringComparison.Ordinal) &&
        Guid.TryParseExact(id[8..], "N", out _);
    public static string NewId() => "windows:" + Guid.NewGuid().ToString("N");
    public bool CanPassText => !Packaged && Path.GetExtension(Target).Equals(".exe", StringComparison.OrdinalIgnoreCase);
    public bool IsValid => IsId(Id) && !string.IsNullOrWhiteSpace(Name) && Name.Length <= 80 &&
        !Name.Any(char.IsControl) && IsValidTarget(Target, Packaged) && (!PassText || CanPassText);

    public static bool IsValidTarget(string? target, bool packaged)
    {
        if (string.IsNullOrWhiteSpace(target) || target.IndexOfAny(['\0', '\r', '\n', '"']) >= 0) return false;
        if (packaged)
            return target.Contains('!') && target.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '!');
        return Path.IsPathFullyQualified(target) &&
            (Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
             Path.GetExtension(target).Equals(".lnk", StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class WindowsAppLauncher(Func<ProcessStartInfo, bool>? start = null)
{
    private readonly Func<ProcessStartInfo, bool> _start = start ?? (info => { using var process = Process.Start(info); return true; });

    public bool Open(WindowsAppDestination app, string text)
    {
        if (!app.IsValid || text.Contains('\0')) return false;
        ProcessStartInfo info;
        if (app.Packaged)
        {
            info = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
            info.ArgumentList.Add("shell:AppsFolder\\" + app.Target);
        }
        else
        {
            if (!File.Exists(app.Target)) return false;
            info = new(app.Target) { WorkingDirectory = Path.GetDirectoryName(app.Target)! };
            // Text is one literal argument. It is never interpreted as a command.
            if (app.PassText && text.Length > 0) info.ArgumentList.Add(text);
        }
        info.UseShellExecute = true;
        return _start(info);
    }
}
