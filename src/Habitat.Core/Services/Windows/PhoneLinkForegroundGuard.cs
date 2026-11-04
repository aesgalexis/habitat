using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Habitat.Features.Contacts;

namespace Habitat.Services;

[SupportedOSPlatform("windows")]
internal sealed class PhoneLinkForegroundGuard : IDisposable
{
    private readonly PhoneLinkFocusKeeper _keeper;
    private readonly WinEventCallback _callback;
    private GCHandle _callbackHandle;
    private nint _hook;
    private bool _disposed;
    private PhoneLinkForegroundGuard(nint owner)
    {
        _keeper = new(owner, GetForegroundWindow, PhoneLinkDesktopAutomation.IsPhoneLinkWindow, PhoneLinkDesktopAutomation.ReturnFocus);
        _callback = (_, _, _, _, _, _, _) => { if (!_disposed) _keeper.ForegroundChanged(); };
        _callbackHandle = GCHandle.Alloc(_callback);
        // Called on the HABITAT UI thread, which has the required message loop.
        _hook = SetWinEventHook(3, 3, 0, _callback, 0, 0, 0);
        if (_hook == 0)
        {
            _callbackHandle.Free();
            throw new InvalidOperationException("Could not keep HABITAT active. No message was sent.");
        }
    }
    internal static IDisposable Start(nint owner) => owner == 0 ? Disabled : new PhoneLinkForegroundGuard(owner);
    internal static IDisposable Disabled { get; } = new EmptyScope();
    private sealed class EmptyScope : IDisposable { public void Dispose() { } }
    public void Dispose()
    {
        if (_disposed) return;
        _keeper.ForegroundChanged();
        _disposed = true;
        if (_hook != 0) { UnhookWinEvent(_hook); _hook = 0; }
        if (_callbackHandle.IsAllocated) _callbackHandle.Free();
    }
    private delegate void WinEventCallback(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
}
