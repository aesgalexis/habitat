using System.Runtime.InteropServices;

namespace Habitat.Services;

// Resolve paths without creating folders or reading user data. Existing desktop
// and runtime locations deliberately remain distinct for compatibility.
internal sealed class AppDataPaths
{
    private readonly string? _isolatedRoot;
    private AppDataPaths(string? isolatedRoot) => _isolatedRoot = isolatedRoot is null ? null : Path.GetFullPath(isolatedRoot);
    public static AppDataPaths Production { get; } = new(null);
    public static AppDataPaths Isolated(string root) => new(root);
    public bool IsIsolated => _isolatedRoot is not null;
    public string LocalDirectory => _isolatedRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HABITAT");
    public string DesktopDirectory => _isolatedRoot ?? Path.Combine(DesktopLocalDirectory(), "HABITAT");
    public string PreferencesPath => Path.Combine(LocalDirectory, "preferences.json");
    public string WallpapersDirectory => Path.Combine(LocalDirectory, "Wallpapers");
    public string TasksPath => Path.Combine(DesktopDirectory, "tasks.json");
    public string CodexChatsPath => Path.Combine(DesktopDirectory, "codex-chats.json");
    public string GmailLegacyPath => Path.Combine(DesktopDirectory, "gmail-account.bin");

    internal static string DesktopLocalDirectory()
    {
        if (!OperatingSystem.IsWindows()) return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");
        Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(ref folder, 0x00010000 | 0x00004000, IntPtr.Zero, out var pointer));
        try { return Marshal.PtrToStringUni(pointer) ?? throw new IOException("Windows could not locate the desktop account folder."); }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    internal static bool HasPackageIdentity()
    {
        if (!OperatingSystem.IsWindows()) return false;
        uint length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) != 15700;
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, IntPtr token, out IntPtr path);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint length, IntPtr packageName);
}
