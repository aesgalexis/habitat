using Microsoft.Win32;
using System.Runtime.Versioning;

namespace Habitat.Services;

[SupportedOSPlatform("windows")]
internal static class WindowsChrome
{
    public static string? FindExecutable()
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var app = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
                if (ExecutableFromCommand(app?.GetValue(null) as string) is { } path && File.Exists(path)) return path;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.LocalApplicationData })
        {
            var root = Environment.GetFolderPath(folder);
            if (string.IsNullOrWhiteSpace(root)) continue;
            var path = Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    internal static string? ExecutableFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        var end = command.StartsWith('"') ? command.IndexOf('"', 1) : command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) + 4;
        var path = command.StartsWith('"') ? end > 1 ? command[1..end] : null : end >= 4 ? command[..end] : null;
        return path is not null && Path.IsPathFullyQualified(path) &&
            Path.GetFileName(path).Equals("chrome.exe", StringComparison.OrdinalIgnoreCase) ? path : null;
    }
}
