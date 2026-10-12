using Habitat.Services;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace Habitat;

public sealed class MainWindow : Window
{
    private readonly BridgeService _bridge;

    public MainWindow(BridgeService bridge)
    {
        _bridge = bridge;
        Title = "HABITAT";
        Content = new MainPage(bridge);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(860, 720));
        Closed += OnClosed;
    }

    public void SetWindowOpacity(double percent)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | WsExLayered));
        var alpha = (byte)Math.Clamp(Math.Round(percent * 2.55), 13, 255);
        SetLayeredWindowAttributes(hwnd, 0, alpha, LwaAlpha);
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        await _bridge.DisposeAsync();
    }

    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x00080000L;
    private const uint LwaAlpha = 0x00000002;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);
}
