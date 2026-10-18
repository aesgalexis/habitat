using System.ComponentModel;
using System.Diagnostics;

namespace Habitat.Services;

internal sealed record InstalledBrowser(string Id, string Name, string Executable, string Family);

// One policy for every desktop web link. Saved paths are only used after matching
// a current Windows browser registration; URLs are always a single argument.
internal sealed class BrowserLauncher(
    Func<IReadOnlyList<InstalledBrowser>> discover,
    Func<InstalledBrowser?> systemDefault,
    Func<ProcessStartInfo, bool>? start = null)
{
    public const string Default = "default";
    public string SelectedBrowser { get; set; } = Default;
    public IReadOnlyList<InstalledBrowser> Installed => discover();
    public InstalledBrowser? Selected => SelectedBrowser == Default ? systemDefault() :
        Installed.FirstOrDefault(browser => string.Equals(browser.Id, SelectedBrowser, StringComparison.OrdinalIgnoreCase));
    public string BridgeFamily => Selected?.Family ?? "unavailable";

    public bool Open(Uri uri)
    {
        if (!uri.IsAbsoluteUri) return false;
        ProcessStartInfo info;
        if (uri.Scheme is not ("http" or "https") || SelectedBrowser == Default)
            info = new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true };
        else
        {
            if (Selected is not { } browser) return false;
            info = new ProcessStartInfo(browser.Executable) { UseShellExecute = false };
            info.ArgumentList.Add(uri.AbsoluteUri);
        }
        try
        {
            if (start is not null) return start(info);
            using var process = Process.Start(info);
            return info.UseShellExecute || process is not null;
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        { return false; }
    }
}
