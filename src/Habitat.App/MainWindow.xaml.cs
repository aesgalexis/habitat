using Habitat.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Windows.UI;
using WinRT;

namespace Habitat;

public sealed class MainWindow : Window
{
    private readonly BridgeService _bridge;
    private readonly MainPage _page;
    private readonly DesktopAcrylicController? _acrylic;
    private readonly SystemBackdropConfiguration? _backdropConfiguration;
    private string _statusIcon = string.Empty;

    public MainWindow(BridgeService bridge)
    {
        _bridge = bridge;
        Title = "HABITAT";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
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
        Title = string.IsNullOrWhiteSpace(text) ? "HABITAT" : $"HABITAT — {text}";
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

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        _page.SaveWindowPlacement(AppWindow);
        _acrylic?.Dispose();
        await _bridge.DisposeAsync();
    }
}
