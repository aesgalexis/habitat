using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Habitat.Services;

// Windows must skip the whole HWND, including its WinUI children, when finding
// a mouse/drag target in another process. HTTRANSPARENT alone only skips windows
// on the same thread. A layered, transparent HWND also skips cross-process input.
internal sealed class InactiveWindowPassthrough : IDisposable
{
    private const int ExtendedStyle = -20;
    private const int Layered = 0x00080000;
    private const int Transparent = 0x00000020;
    private const uint ActivateApp = 0x001C;
    private const uint Activate = 0x0006;
    private const uint SetFocus = 0x0007;
    private const uint NcDestroy = 0x0082;
    private const nuint SubclassId = 0x484147;
    private readonly SubclassProc _procedure;
    private readonly Action<Exception> _reportError;
    private nint _window;
    private int _addedStyles;
    private bool _passthrough;

    internal InactiveWindowPassthrough(nint window, Action<Exception> reportError)
    {
        _reportError = reportError;
        _procedure = WindowProcedure;
        if (!SetWindowSubclass(window, _procedure, SubclassId, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Install inactive window passthrough");
        _window = window;
    }

    private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nint data)
    {
        // App activation keeps owned menus, pickers and dialogs interactive.
        // No polling or global mouse hook is needed, and taskbar/Alt+Tab/IPC
        // activation is still handled by Windows normally.
        if (message == ActivateApp) SetActive(wParam != 0);
        // Window/focus activation can arrive without another app-activation
        // notification, for example through an owned window or an input helper.
        // Only application deactivation enables passthrough; every activation
        // route must restore input immediately.
        if (message == SetFocus || message == Activate && (wParam & 0xffff) != 0) RestoreInput();
        if (message == NcDestroy)
        {
            RemoveWindowSubclass(window, _procedure, SubclassId);
            _window = 0;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void SetActive(bool active)
    {
        if (_window == 0 || _passthrough == !active) return;
        try
        {
            var style = GetWindowLong(_window, ExtendedStyle);
            if (active)
            {
                WriteStyle(style & ~_addedStyles);
                _addedStyles = 0;
                _passthrough = false;
            }
            else
            {
                _addedStyles = (Layered | Transparent) & ~style;
                WriteStyle(style | Layered | Transparent);
                // Keep the current appearance. Only initialise the layered state
                // we introduced; an existing layer may have its own opacity.
                if ((_addedStyles & Layered) != 0 && !SetLayeredWindowAttributes(_window, 0, 255, 2 /* LWA_ALPHA */))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Initialise inactive window layer");
                _passthrough = true;
            }
            RefreshFrame();
        }
        catch (Exception exception)
        {
            // Failure must leave HABITAT reachable as a normal window.
            try
            {
                WriteStyle(GetWindowLong(_window, ExtendedStyle) & ~_addedStyles);
                _addedStyles = 0;
                _passthrough = false;
                RefreshFrame();
            }
            catch (Exception recovery) { _reportError(recovery); }
            _reportError(exception);
        }
    }

    internal void RestoreInput() => SetActive(true);

    private void WriteStyle(int style)
    {
        Marshal.SetLastPInvokeError(0);
        if (SetWindowLong(_window, ExtendedStyle, style) == 0 && Marshal.GetLastWin32Error() != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Set inactive window style");
    }

    private void RefreshFrame()
    {
        if (!SetWindowPos(_window, 0, 0, 0, 0, 0, 0x0037 /* FRAMECHANGED | NOACTIVATE | NOZORDER | NOMOVE | NOSIZE */))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Refresh inactive window frame");
    }

    public void Dispose()
    {
        if (_window == 0) return;
        SetActive(true);
        RemoveWindowSubclass(_window, _procedure, SubclassId);
        _window = 0;
        GC.KeepAlive(_procedure);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nint data);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassProc procedure, nuint id, nint data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProc procedure, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint window, uint colorKey, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
