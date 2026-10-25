using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Habitat.Services;

internal static class Program
{
    private const int ExStyle = -20;
    private const int GhostStyles = 0x00080020;

    private static int Main(string[] args)
    {
        if (args is ["--underlay", var name]) return Underlay(name);
        var nativeDesktop = args.Contains("--native-desktop");
        // No visible desktop switch, input injection, real icons or clipboard.
        var original = GetThreadDesktop(GetCurrentThreadId());
        var desktopName = "HabitatWindowChecks" + Guid.NewGuid().ToString("N");
        var desktop = nativeDesktop ? CreateDesktop(desktopName, 0, 0, 0, 0x01FF, 0) : 0;
        if (nativeDesktop && desktop == 0) throw Error("Create private desktop");
        Process? child = null;
        nint window = 0;
        try
        {
            nint underlay = 0;
            if (nativeDesktop)
            {
            if (!SetThreadDesktop(desktop)) throw Error("Select private desktop");
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                ArgumentList = { "--underlay", desktopName }
            };
            child = Process.Start(start)!;
            var ready = child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            if (!long.TryParse(ready, out var handle)) throw new Exception("Underlay fixture did not start: " + ready);
            underlay = (nint)handle;
            }
            window = CreateWindowEx(0x00040000 /* APPWINDOW */, "BUTTON", "HABITAT synthetic overlay",
                0x80000000 /* POPUP */, 200, 200, 400, 300, 0, 0, 0, 0);
            if (window == 0) throw Error("Create overlay");
            var content = CreateWindowEx(0, "BUTTON", "Synthetic WinUI child", 0x50000000 /* CHILD | VISIBLE */,
                0, 0, 400, 300, window, 0, 0, 0);
            if (content == 0) throw Error("Create content child");
            if (nativeDesktop)
            {
                ShowWindow(window, 4 /* SHOWNOACTIVATE */);
                SetWindowPos(window, 0, 0, 0, 0, 0, 0x0013 /* NOACTIVATE | NOMOVE | NOSIZE */);
            }
            var originalStyle = GetWindowLong(window, ExStyle);
            var errors = new List<Exception>();
            using (var ghost = new InactiveWindowPassthrough(window, errors.Add))
            {
                if (nativeDesktop) Check(RootAtPoint() == window, "Active overlay receives input, including its child content");
                for (var cycle = 0; cycle < 3; cycle++)
                {
                    SendMessage(window, 0x001C /* ACTIVATEAPP */, 0, 0);
                    Check((GetWindowLong(window, ExStyle) & GhostStyles) == GhostStyles,
                        "Inactive overlay enables native cross-process passthrough");
                    if (nativeDesktop) Check(RootAtPoint() == underlay,
                        "Windows targets the window in another process beneath the overlay");
                    if (nativeDesktop) Check(IsWindowVisible(window), "Ghost window remains visible");
                    Check((GetWindowLong(window, ExStyle) & 0x08000080 /* NOACTIVATE | TOOLWINDOW */) == 0 &&
                        (GetWindowLong(window, ExStyle) & 0x00040000) != 0,
                        "Taskbar and normal activation styles are preserved");
                    SendMessage(window, 0x001C, 0, 0);
                    Check((GetWindowLong(window, ExStyle) & GhostStyles) == GhostStyles, "Repeated deactivation keeps passthrough stable");
                    SendMessage(window, 0x001C, 1, 0);
                    Check(GetWindowLong(window, ExStyle) == originalStyle && (!nativeDesktop || RootAtPoint() == window),
                        "Reactivation restores interactive content and original styles");
                }
                SendMessage(window, 0x0006 /* ACTIVATE */, 0, 0);
                Check(GetWindowLong(window, ExStyle) == originalStyle,
                    "Window deactivation alone does not strand owned menus and dialogs");
                foreach (var message in new uint[] { 0x0006 /* WM_ACTIVATE */, 0x0007 /* WM_SETFOCUS */ })
                {
                    SendMessage(window, 0x001C /* WM_ACTIVATEAPP */, 0, 0);
                    SendMessage(window, message, 1, 0);
                    Check(GetWindowLong(window, ExStyle) == originalStyle,
                        $"Window activation/focus restores input without relying on WM_ACTIVATEAPP: {message:X}");
                }
                SendMessage(window, 0x001C, 0, 0);
                ghost.RestoreInput();
                ghost.RestoreInput();
                Check(GetWindowLong(window, ExStyle) == originalStyle,
                    "Explicit activation restores input before foreground acquisition and is idempotent");
                SendMessage(window, 0x001C, 0, 0);
                SetWindowLong(window, ExStyle, GetWindowLong(window, ExStyle) | 0x00000010 /* ACCEPTFILES */);
            }
            Check((GetWindowLong(window, ExStyle) & GhostStyles) == 0 &&
                (GetWindowLong(window, ExStyle) & 0x00000010) != 0 && (!nativeDesktop || RootAtPoint() == window),
                "Disposal restores input and retains unrelated style changes");
            SetWindowLong(window, ExStyle, originalStyle | 0x00080000);
            if (!SetLayeredWindowAttributes(window, 0, 210, 2)) throw Error("Set existing opacity");
            using (var ghost = new InactiveWindowPassthrough(window, errors.Add))
            {
                SendMessage(window, 0x001C, 0, 0);
                SendMessage(window, 0x001C, 1, 0);
                Check(GetLayeredWindowAttributes(window, out _, out var alpha, out _) && alpha == 210 &&
                    GetWindowLong(window, ExStyle) == (originalStyle | 0x00080000),
                    "An existing layered style and opacity are preserved");
            }
            SetWindowLong(window, ExStyle, originalStyle);
            using (var ghost = new InactiveWindowPassthrough(window, errors.Add))
            {
                SendMessage(window, 0x001C, 0, 0);
                DestroyWindow(window);
                window = 0;
            }
            Check(errors.Count == 0, "Activation, cleanup and destruction report no native errors");
            return 0;
        }
        finally
        {
            if (window != 0) DestroyWindow(window);
            if (child is not null)
            {
                if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); }
                child.Dispose();
            }
            if (desktop != 0) { SetThreadDesktop(original); CloseDesktop(desktop); }
        }
    }

    private static int Underlay(string desktopName)
    {
        var desktop = OpenDesktop(desktopName, 0, false, 0x01FF);
        if (desktop == 0 || !SetThreadDesktop(desktop)) throw Error("Open fixture desktop");
        var window = CreateWindowEx(0, "BUTTON", "Synthetic desktop target", 0x80000000,
            200, 200, 400, 300, 0, 0, 0, 0);
        if (window == 0) throw Error("Create underlay");
        ShowWindow(window, 4);
        Console.WriteLine((long)window);
        Console.Out.Flush();
        while (GetMessage(out var message, 0, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
        return 0;
    }

    private static nint RootAtPoint() => GetAncestor(WindowFromPoint(new Point { X = 240, Y = 240 }), 2);
    private static Win32Exception Error(string action) => new(Marshal.GetLastWin32Error(), action);
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("PASS: " + name);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    {
        public nint Window; public uint Id; public nuint WParam; public nint LParam;
        public uint Time; public Point Position; public uint Private;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateDesktop(string name, nint device, nint mode, uint flags, uint access, nint security);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint OpenDesktop(string name, uint flags, bool inherit, uint access);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetThreadDesktop(nint desktop);
    [DllImport("user32.dll")] private static extern nint GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(int exStyle, string className, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint data);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(nint window, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(nint window, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "GetMessageW")] private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref Message message);
}
