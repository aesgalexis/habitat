using System.Diagnostics;

namespace Habitat.Services;

internal static class UpdateHelperLauncher
{
    internal static ProcessStartInfo CreateStartInfo(string helper, string requestOption, string requestPath)
    {
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(helper)!,
            ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", helper, requestOption, requestPath }
        };
        // PowerShell 7 module paths are incompatible with Windows PowerShell.
        // Let the installation helper initialise its own compatible module path.
        start.Environment.Remove("PSModulePath");
        return start;
    }
}
