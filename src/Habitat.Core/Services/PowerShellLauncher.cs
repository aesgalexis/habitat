using System.Diagnostics;
using System.Text;

namespace Habitat.Services;

internal sealed class PowerShellLauncher
{
    private readonly Func<ProcessStartInfo, bool> _start;
    public string WorkingDirectory { get; }

    public PowerShellLauncher(string? workingDirectory = null, Func<ProcessStartInfo, bool>? start = null)
    {
        WorkingDirectory = workingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _start = start ?? (info =>
        {
            using var process = Process.Start(info);
            return process is not null;
        });
    }

    // Only called after the user confirms the exact command in HABITAT.
    public bool Run(string command)
    {
        var info = new ProcessStartInfo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = true,
            WorkingDirectory = WorkingDirectory
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NoExit" })
            info.ArgumentList.Add(argument);
        if (!string.IsNullOrWhiteSpace(command))
        {
            info.ArgumentList.Add("-EncodedCommand");
            info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
        }
        return _start(info);
    }
}
