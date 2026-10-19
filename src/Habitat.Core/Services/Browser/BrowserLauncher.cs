using System.ComponentModel;
using System.Diagnostics;

namespace Habitat.Services;

// Web destinations share Chrome with the extension. Windows still handles
// application protocols; web URLs are always passed as one exact argument.
internal sealed class BrowserLauncher(
    Func<string?> chromePath,
    Func<ProcessStartInfo, bool>? start = null)
{
    public bool Open(Uri uri)
    {
        if (!uri.IsAbsoluteUri) return false;
        ProcessStartInfo info;
        if (uri.Scheme is not ("http" or "https" or "chrome"))
            info = new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true };
        else
        {
            if (chromePath() is not { } executable) return false;
            info = new ProcessStartInfo(executable) { UseShellExecute = false };
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
