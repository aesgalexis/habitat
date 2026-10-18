using Microsoft.Win32;
using System.Runtime.Versioning;

namespace Habitat.Services;

[SupportedOSPlatform("windows")]
internal static class WindowsBrowsers
{
    public static IReadOnlyList<InstalledBrowser> Discover()
    {
        var browsers = new Dictionary<string, InstalledBrowser>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var clients = root.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet");
                if (clients is null) continue;
                foreach (var name in clients.GetSubKeyNames())
                {
                    using var client = clients.OpenSubKey(name);
                    using var command = client?.OpenSubKey(@"shell\open\command");
                    if (FromCommand(client?.GetValue(null) as string ?? name, command?.GetValue(null) as string) is { } browser)
                        browsers.TryAdd(browser.Id, browser);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return browsers.Values.OrderBy(browser => browser.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static InstalledBrowser? SystemDefault()
    {
        try
        {
            using var choice = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            if (choice?.GetValue("ProgId") is not string progId) return null;
            using var command = Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command");
            return FromCommand("System default", command?.GetValue(null) as string);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
    }

    private static InstalledBrowser? FromCommand(string name, string? command)
    {
        var path = ExecutableFromCommand(command);
        return path is not null && File.Exists(path) ? new(path, name, path, Family(path)) : null;
    }

    internal static string? ExecutableFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        var end = command.StartsWith('"') ? command.IndexOf('"', 1) : command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) + 4;
        var path = command.StartsWith('"') ? end > 1 ? command[1..end] : null : end >= 4 ? command[..end] : null;
        return path is not null && Path.IsPathFullyQualified(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    internal static string Family(string path) => Path.GetFileName(path).ToLowerInvariant() switch
    {
        "msedge.exe" => "edge", "chrome.exe" => "chrome", "firefox.exe" => "firefox",
        "brave.exe" => "brave", "vivaldi.exe" => "vivaldi", "opera.exe" => "opera",
        "launcher.exe" when path.Contains("opera", StringComparison.OrdinalIgnoreCase) => "opera",
        _ => "unavailable"
    };
}
