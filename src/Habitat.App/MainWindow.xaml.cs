using Habitat.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Windows.UI;
using WinRT;

namespace Habitat;

public sealed class MainWindow : Window
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private readonly BridgeService _bridge;
    private readonly MainPage _page;
    private readonly DesktopAcrylicController? _acrylic;
    private readonly SystemBackdropConfiguration? _backdropConfiguration;
    private string _statusIcon = string.Empty;
    public bool IsActive { get; private set; }

    public MainWindow(BridgeService bridge)
    {
        _bridge = bridge;
        Title = "HABITAT";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "HabitatDot.ico");
        if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
        _page = new MainPage(bridge);
        Content = _page;
        if (DesktopAcrylicController.IsSupported())
        {
            _backdropConfiguration = new SystemBackdropConfiguration { IsInputActive = true };
            _acrylic = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
            _acrylic.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
            _acrylic.SetSystemBackdropConfiguration(_backdropConfiguration);
        }
        AppWindow.Resize(new Windows.Graphics.SizeInt32(860, 720));
        _page.RestoreWindowPlacement(AppWindow);
        Activated += OnActivated;
        Closed += OnClosed;
    }

    public void SetBackgroundAppearance(double percent, bool dark)
    {
        var opacity = (float)Math.Clamp(percent / 100d, 0, 1);
        var tint = dark
            ? Color.FromArgb(0xFF, 0x10, 0x16, 0x23)
            : Color.FromArgb(0xFF, 0xF2, 0xF6, 0xFB);
        var titleBackground = dark
            ? Color.FromArgb(0xFF, 0x00, 0x00, 0x00)
            : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
        var titleForeground = dark
            ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0xFF, 0x00, 0x00, 0x00);
        var hover = dark
            ? Color.FromArgb(0xFF, 0x28, 0x28, 0x28)
            : Color.FromArgb(0xFF, 0xE8, 0xE8, 0xE8);
        var pressed = dark
            ? Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A)
            : Color.FromArgb(0xFF, 0xD5, 0xD5, 0xD5);

        var titleBar = AppWindow.TitleBar;
        titleBar.BackgroundColor = titleBackground;
        titleBar.InactiveBackgroundColor = titleBackground;
        titleBar.ForegroundColor = titleForeground;
        titleBar.InactiveForegroundColor = titleForeground;
        titleBar.ButtonBackgroundColor = titleBackground;
        titleBar.ButtonInactiveBackgroundColor = titleBackground;
        titleBar.ButtonForegroundColor = titleForeground;
        titleBar.ButtonInactiveForegroundColor = titleForeground;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = titleForeground;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonPressedForegroundColor = titleForeground;

        if (_acrylic is not null && _backdropConfiguration is not null)
        {
            _backdropConfiguration.Theme = dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
            _acrylic.TintColor = tint;
            _acrylic.FallbackColor = _acrylic.TintColor;
            _acrylic.TintOpacity = opacity;
            _acrylic.LuminosityOpacity = opacity;
        }
        _page.SetBackgroundOpacity(percent);
    }

    public void SetStatusPresentation(string text, string status)
    {
        Title = string.IsNullOrWhiteSpace(text) ? "HABITAT" : $"HABITAT - {text}";
        var normalized = status switch
        {
            "Success" => "Success",
            "Warning" => "Warning",
            "Danger" => "Danger",
            _ => "Active"
        };
        if (_statusIcon == normalized)
        {
            return;
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", $"Status{normalized}.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
            _statusIcon = normalized;
        }
    }

    public bool DetectBackdropIsDark(bool current)
    {
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        const int gap = 20;
        var xs = new[] { position.X + size.Width / 5, position.X + size.Width / 2, position.X + size.Width * 4 / 5 };
        var ys = new[] { position.Y + size.Height / 5, position.Y + size.Height / 2, position.Y + size.Height * 4 / 5 };
        var points = new List<(int X, int Y)>();
        foreach (var x in xs)
        {
            points.Add((x, position.Y - gap));
            points.Add((x, position.Y + size.Height + gap));
        }
        foreach (var y in ys)
        {
            points.Add((position.X - gap, y));
            points.Add((position.X + size.Width + gap, y));
        }

        var virtualLeft = GetSystemMetrics(SmXVirtualScreen);
        var virtualTop = GetSystemMetrics(SmYVirtualScreen);
        var virtualRight = virtualLeft + GetSystemMetrics(SmCxVirtualScreen);
        var virtualBottom = virtualTop + GetSystemMetrics(SmCyVirtualScreen);
        var luminances = new List<double>();
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            return current;
        }

        try
        {
            foreach (var point in points)
            {
                if (point.X < virtualLeft || point.X >= virtualRight || point.Y < virtualTop || point.Y >= virtualBottom)
                {
                    continue;
                }

                var pixel = GetPixel(screen, point.X, point.Y);
                if (pixel == 0xFFFFFFFF)
                {
                    continue;
                }

                var red = pixel & 0xFF;
                var green = (pixel >> 8) & 0xFF;
                var blue = (pixel >> 16) & 0xFF;
                luminances.Add((0.2126 * red + 0.7152 * green + 0.0722 * blue) / 255d);
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }

        if (luminances.Count == 0)
        {
            return current;
        }

        luminances.Sort();
        var median = luminances[luminances.Count / 2];
        return current ? median < 0.58 : median < 0.42;
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        IsActive = args.WindowActivationState != WindowActivationState.Deactivated;
        if (IsActive)
        {
            _page.OnWindowActivated();
            _page.RefreshHybridAppearance(force: true);
            await Task.Delay(80);
            _page.FocusComposer();
        }
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        _page.SaveWindowPlacement(AppWindow);
        _acrylic?.Dispose();
        await _bridge.DisposeAsync();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr deviceContext, int x, int y);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
