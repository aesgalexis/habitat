using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Habitat.Services;

internal sealed class NotepadLauncher
{
    private readonly Func<Task<nint>> _openBlankDocument;
    private readonly Func<nint, string, bool> _insertText;

    public NotepadLauncher(Func<Task<nint>>? openBlankDocument = null, Func<nint, string, bool>? insertText = null)
    {
        // UI Automation runs on a worker, away from HABITAT's UI apartment.
        _openBlankDocument = openBlankDocument ?? (() => Task.Run(OpenBlankDocument));
        _insertText = insertText ?? InsertText;
    }

    public async Task<bool> OpenAsync(string text)
    {
        if (text.Contains('\0')) return false;
        var editor = await _openBlankDocument();
        return editor != 0 && _insertText(editor, text);
    }

    internal static ProcessStartInfo StartInfo() => new("notepad.exe") { UseShellExecute = true };

    private static nint OpenBlankDocument()
    {
        // Reuse the first Notepad window in Z order instead of launching a process
        // that may open another window.
        var existingWindows = NotepadWindows();
        var windowToReuse = existingWindows.FirstOrDefault();
        if (windowToReuse != 0)
        {
            if (IsIconic(windowToReuse)) ShowWindowAsync(windowToReuse, 9); // SW_RESTORE
            SetForegroundWindow(windowToReuse);
        }
        // Launch only when Notepad has no open window. Never pass a file path.
        using var process = windowToReuse == 0 ? Process.Start(StartInfo()) : null;
        var openedClassicWindow = false;
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            var window = windowToReuse != 0 ? windowToReuse :
                NotepadWindows().FirstOrDefault(handle => !existingWindows.Contains(handle));
            if (!IsNotepadWindow(window))
            {
                if (windowToReuse != 0) return 0;
                var newWindow = NotepadWindows().FirstOrDefault(handle => !existingWindows.Contains(handle));
                if (newWindow != 0) SetForegroundWindow(newWindow);
                Thread.Sleep(100);
                continue;
            }
            if (windowToReuse == 0) SetForegroundWindow(window);
            var editor = FindEditor(window);
            if (editor == 0) { Thread.Sleep(100); continue; }
            if (ClassName(editor).Equals("Edit", StringComparison.OrdinalIgnoreCase))
            {
                // Classic Notepad starts a separate window. Never replace a previous document.
                if (!existingWindows.Contains(window)) return TextLength(editor) == 0 ? editor : 0;
                if (!openedClassicWindow)
                {
                    using var classicProcess = Process.Start(StartInfo());
                    openedClassicWindow = true;
                }
                windowToReuse = 0;
                var newWindow = NotepadWindows().FirstOrDefault(handle => !existingWindows.Contains(handle));
                if (newWindow != 0) SetForegroundWindow(newWindow);
                Thread.Sleep(100);
                continue;
            }
            // Modern Notepad may restore previous tabs at startup. Always create
            // a tab through its button and verify its identity before inserting.
            return NotepadTabs.OpenNewTab(window, () =>
            {
                var blank = FindEditor(window);
                return blank != 0 && TextLength(blank) == 0 ? blank : 0;
            });
        }
        return 0;
    }

    internal static bool InsertText(nint editor, string text)
    {
        if (!IsWindow(editor) || TextLength(editor) != 0) return false;
        // EM_REPLACESEL inserts Unicode text with undo support and notifies Notepad
        // that the new document has changed. The clipboard remains untouched.
        if (SendMessageTimeout(editor, 0x00C2, 1, text, 2, 2000, out _) == 0) return false;
        if (SendMessageTimeout(editor, 0x000E, 0, 0, 2, 2000, out var length) == 0 || length > int.MaxValue - 1)
            return false;
        var actual = new StringBuilder((int)length + 1);
        if (SendMessageTimeout(editor, 0x000D, (nuint)actual.Capacity, actual, 2, 2000, out _) == 0) return false;
        if (NormalizeLineEndings(actual.ToString()) != NormalizeLineEndings(text)) return false;
        SendMessageTimeout(editor, 0x00B9, 1, 0, 2, 2000, out _); // EM_SETMODIFY
        return true;
    }

    internal static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static int TextLength(nint editor) =>
        SendMessageTimeout(editor, 0x000E, 0, 0, 2, 1000, out var length) != 0 && length <= int.MaxValue
            ? (int)length : -1;

    private static nint FindEditor(nint window)
    {
        nint editor = 0;
        nint classicEditor = 0;
        EnumChildWindows(window, (child, _) =>
        {
            if (!IsWindowVisible(child)) return true;
            var name = ClassName(child);
            if (name.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase))
            {
                editor = child;
                return false;
            }
            if (name.Equals("Edit", StringComparison.OrdinalIgnoreCase) && (GetWindowLong(child, -16) & 4) != 0)
                classicEditor = child;
            return true;
        }, 0);
        return editor != 0 ? editor : classicEditor;
    }

    private static List<nint> NotepadWindows()
    {
        var windows = new List<nint>();
        EnumWindows((window, _) => { if (IsNotepadWindow(window)) windows.Add(window); return true; }, 0);
        return windows;
    }

    private static bool IsNotepadWindow(nint window)
    {
        if (window == 0 || !IsWindowVisible(window) || !ClassName(window).Equals("Notepad", StringComparison.OrdinalIgnoreCase))
            return false;
        GetWindowThreadProcessId(window, out var id);
        try { using var process = Process.GetProcessById((int)id); return process.ProcessName.Equals("Notepad", StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static string ClassName(nint window)
    {
        var name = new StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    private delegate bool WindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint window, WindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, string text, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, StringBuilder text, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint parameter, uint flags, uint timeout, out nuint result);
}
