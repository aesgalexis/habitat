namespace Habitat.Services;

// Both supported installations keep their integrations outside user data.
internal sealed record InstallationFiles(string? ChromeExtensionDirectory, string? VSCodeExtensionDirectory)
{
    internal static readonly Uri ChromeStore = new("https://chromewebstore.google.com/detail/ennnldolgdbbkgmpjjpkafeajihpimii");
    internal static readonly Uri DownloadChrome = new("https://www.google.com/chrome/");
    internal static readonly Uri SetupGuide = new("https://github.com/aesgalexis/habitat/blob/main/docs/GETTING_STARTED.md");

    internal static InstallationFiles Find(string baseDirectory)
    {
        var root = Path.GetFullPath(baseDirectory);
        // EXE, ZIP release/stage and the development UI-check output.
        var candidates = new[] { root, Path.GetFullPath(Path.Combine(root, "../../..")), Path.GetFullPath(Path.Combine(root, "../..")) };
        var chrome = candidates.SelectMany(path => new[] { Path.Combine(path, "browser-extension"), Path.Combine(path, "extension") })
            .FirstOrDefault(path => File.Exists(Path.Combine(path, "manifest.json")) && File.Exists(Path.Combine(path, "welcome.html")));
        var vscode = candidates.Select(path => Path.Combine(path, "vscode-extension"))
            .FirstOrDefault(path => Directory.Exists(path) && Directory.EnumerateFiles(path, "*.vsix").Any());
        return new(chrome, vscode);
    }
}
