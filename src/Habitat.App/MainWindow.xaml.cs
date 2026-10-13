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
        Closed += OnClosed;
    }

    public void SetBackgroundAppearance(double percent, bool dark)
    {
        var opacity = (float)Math.Clamp(percent / 100d, 0, 1);
        var tint = dark
            ? Color.FromArgb(0xFF, 0x10, 0x16, 0x23)
            : Color.FromArgb(0xFF, 0xF2, 0xF6, 0xFB);
        var foreground = dark
            ? Color.FromArgb(0xFF, 0xEE, 0xF3, 0xFB)
            : Color.FromArgb(0xFF, 0x1B, 0x24, 0x34);
        var transparent = Color.FromArgb(0, 0, 0, 0);
        var hover = dark
            ? Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x22, 0, 0, 0);

        var titleBar = AppWindow.TitleBar;
        titleBar.BackgroundColor = tint;
        titleBar.InactiveBackgroundColor = tint;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveForegroundColor = foreground;
        titleBar.ButtonBackgroundColor = transparent;
        titleBar.ButtonInactiveBackgroundColor = transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = hover;
        titleBar.ButtonPressedForegroundColor = foreground;

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

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        _acrylic?.Dispose();
        await _bridge.DisposeAsync();
    }
}
