using Habitat;
using Habitat.Services;
using System.Diagnostics;

internal static class BrowserChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        string? chromePath = @"C:\Browser with spaces\chrome.exe";
        ProcessStartInfo? launched = null;
        var launcher = new BrowserLauncher(() => chromePath, info => { launched = info; return true; });
        foreach (var url in new[] { new Uri("https://www.google.com/search?q=caf%C3%A9%20%26%20snow&text=%22quoted%22"),
            new Uri("https://www.youtube.com/results?search_query=cats%20%26%20dogs"), new Uri("http://example.org/"), new Uri("chrome://extensions/") })
            check(launcher.Open(url) && !launched!.UseShellExecute && launched.FileName == chromePath &&
                launched.ArgumentList.Count == 1 && launched.ArgumentList[0] == url.AbsoluteUri,
                "Web destinations always receive one exact URL argument in Chrome: " + url.Host);
        var applicationUri = new Uri("vscode://");
        check(launcher.Open(applicationUri) && launched!.UseShellExecute && launched.FileName == applicationUri.AbsoluteUri,
            "Application protocols retain their Windows handler.");
        chromePath = null;
        launched = null;
        check(!launcher.Open(new Uri("https://example.org/")) && launched is null,
            "Missing Chrome does not silently open a different Windows default browser.");
        check(!launcher.Open(new Uri("relative", UriKind.Relative)), "Relative URLs cannot launch a process.");
        check(!new BrowserLauncher(() => @"C:\Chrome\chrome.exe", _ => throw new System.ComponentModel.Win32Exception()).Open(new Uri("https://example.org/")),
            "Chrome startup failures are recoverable.");
        var path = Path.Combine(root, "browser-preferences.json");
        foreach (var browser in new[] { "\"default\"", "\"edge\"", "null" })
        {
            File.WriteAllText(path, "{\"Theme\":\"night\",\"Opacity\":37,\"WeatherCity\":\"Ciudad original\",\"Browser\":" + browser + "}");
            var preferences = Preferences.Load(path);
            check(preferences is { Theme: "night", Opacity: 37, WeatherCity: "Ciudad original" } && preferences.Save(path) &&
                !File.ReadAllText(path).Contains("\"Browser\""),
                "Legacy browser choices are ignored and removed without losing other preferences: " + browser);
        }
        if (OperatingSystem.IsWindows())
            check(WindowsChrome.ExecutableFromCommand("\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\" --single-argument %1") == @"C:\Program Files\Google\Chrome\Application\chrome.exe" &&
                WindowsChrome.ExecutableFromCommand(@"C:\Program Files\Google\Chrome\Application\chrome.exe") == @"C:\Program Files\Google\Chrome\Application\chrome.exe" &&
                WindowsChrome.ExecutableFromCommand(@"C:\Other\msedge.exe") is null && WindowsChrome.ExecutableFromCommand("chrome.exe %1") is null &&
                WindowsChrome.ExecutableFromCommand("\"broken") is null,
                "Chrome discovery accepts only absolute Chrome executable paths from Windows registration.");
    }
}
