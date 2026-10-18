using Habitat;
using Habitat.Services;
using System.Diagnostics;

internal static class BrowserChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var chrome = new InstalledBrowser("chrome", "Google Chrome", @"C:\Browser with spaces\chrome.exe", "chrome");
        var edge = new InstalledBrowser("edge", "Microsoft Edge", @"C:\Browser\msedge.exe", "edge");
        IReadOnlyList<InstalledBrowser> installed = [chrome, edge];
        InstalledBrowser currentDefault = edge;
        ProcessStartInfo? launched = null;
        var launcher = new BrowserLauncher(() => installed, () => currentDefault, info => { launched = info; return true; });
        var url = new Uri("https://example.org/search?q=caf%C3%A9%20%26%20snow&text=%22quoted%22");
        check(launcher.Open(url) && launched!.UseShellExecute && launched.FileName == url.AbsoluteUri && launcher.BridgeFamily == "edge",
            "System default opens through Windows and selects the same browser for the bridge.");
        currentDefault = chrome;
        check(launcher.BridgeFamily == "chrome", "Default browser resolution follows changes to Windows preferences.");
        launcher.SelectedBrowser = chrome.Id;
        check(launcher.Open(url) && !launched!.UseShellExecute && launched.FileName == chrome.Executable &&
            launched.ArgumentList.Count == 1 && launched.ArgumentList[0] == url.AbsoluteUri,
            "Explicit browsers receive one exact URL argument, including quotes, Unicode and query delimiters.");
        var applicationUri = new Uri("vscode://");
        check(launcher.Open(applicationUri) && launched!.UseShellExecute && launched.FileName == applicationUri.AbsoluteUri,
            "Application protocols retain their Windows handler rather than opening in a browser.");
        installed = [edge];
        launched = null;
        check(!launcher.Open(url) && launched is null && launcher.BridgeFamily == "unavailable",
            "A removed browser cannot launch a saved executable or silently switch to another browser.");
        check(!new BrowserLauncher(() => [chrome], () => chrome, _ => throw new System.ComponentModel.Win32Exception()).Open(url),
            "Browser startup failures are recoverable.");
        var path = Path.Combine(root, "browser-preferences.json");
        File.WriteAllText(path, "{\"Theme\":\"hybrid\",\"Opacity\":15}");
        check(Preferences.Load(path).Browser == BrowserLauncher.Default, "Existing preferences migrate to the system default browser.");
        var preferences = Preferences.Load(path) with { Browser = edge.Id, WeatherCity = "Ciudad original", GlobalHotkeyEnabled = false };
        check(preferences.Save(path) && Preferences.Load(path) is { Browser: "edge", WeatherCity: "Ciudad original", GlobalHotkeyEnabled: false, Theme: "hybrid", Opacity: 15 },
            "Browser preferences persist alongside the existing appearance, location and shortcut settings.");
        File.WriteAllText(path, "{\"Theme\":\"night\",\"Opacity\":15,\"Browser\":null}");
        check(Preferences.Load(path).Browser == BrowserLauncher.Default, "A missing browser value recovers to the system default.");
        check(BridgeService.AllowsBrowser("edge", null, "Mozilla Chrome/120 Edg/120") &&
            !BridgeService.AllowsBrowser("chrome", null, "Mozilla Chrome/120 Edg/120") &&
            BridgeService.AllowsBrowser("brave", "brave", "Chrome/120") &&
            !BridgeService.AllowsBrowser("chrome", "brave", "Chrome/120") &&
            !BridgeService.AllowsBrowser("unavailable", "chrome", "Chrome/120"),
            "Bridge routing distinguishes Chromium browsers and fails closed for an unavailable browser.");
        if (OperatingSystem.IsWindows())
        {
            check(WindowsBrowsers.ExecutableFromCommand("\"C:\\Program Files\\Browser\\chrome.exe\" --single-argument %1") == @"C:\Program Files\Browser\chrome.exe" &&
                WindowsBrowsers.ExecutableFromCommand(@"C:\Program Files\Browser\msedge.exe -- %1") == @"C:\Program Files\Browser\msedge.exe" &&
                WindowsBrowsers.ExecutableFromCommand("chrome.exe %1") is null && WindowsBrowsers.ExecutableFromCommand("\"broken") is null,
                "Windows browser discovery extracts only absolute executable paths from registered commands.");
        }
    }
}
