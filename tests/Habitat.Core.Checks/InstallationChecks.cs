using Habitat;
using Habitat.Services;

internal static class InstallationChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        foreach (var layout in new[] { "exe", "zip", "development" })
        {
            var install = Path.Combine(root, "install with spaces", layout);
            var app = layout == "exe" ? install : Path.Combine(install, "artifacts", "releases", "release-test");
            Directory.CreateDirectory(app);
            var empty = InstallationFiles.Find(app);
            check(empty.ChromeExtensionDirectory is null && empty.VSCodeExtensionDirectory is null, "Incomplete installation offers recovery instead of fabricated asset paths.");
            var chrome = Path.Combine(install, layout == "exe" ? "browser-extension" : "extension");
            var vscode = Path.Combine(install, "vscode-extension");
            Directory.CreateDirectory(chrome);
            Directory.CreateDirectory(vscode);
            File.WriteAllText(Path.Combine(chrome, "manifest.json"), "{}");
            File.WriteAllText(Path.Combine(chrome, "welcome.html"), "synthetic");
            File.WriteAllText(Path.Combine(vscode, "habitat-terminal-0.2.0.vsix"), "synthetic");
            var files = InstallationFiles.Find(app);
            check(files.ChromeExtensionDirectory == chrome && files.VSCodeExtensionDirectory == vscode, "Installed integration discovery supports spaces and " + layout);
        }
        var path = Path.Combine(root, "first-run-preferences.json");
        var initial = Preferences.Load(path);
        check(!initial.SetupGuideSeen, "A new installation offers the setup guide.");
        check((initial with { SetupGuideSeen = true, WeatherCity = "Ciudad original" }).Save(path) &&
            Preferences.Load(path) is { SetupGuideSeen: true, WeatherCity: "Ciudad original" }, "Closing setup is remembered without losing user preferences.");
        File.WriteAllText(path, "{\"Theme\":\"night\",\"Opacity\":37}");
        check(Preferences.Load(path) is { SetupGuideSeen: false, Theme: "night", Opacity: 37 }, "An upgrade offers setup once and preserves existing preferences.");
    }
}
