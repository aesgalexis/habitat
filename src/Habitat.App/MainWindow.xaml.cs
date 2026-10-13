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
        if (_acrylic is not null && _backdropConfiguration is not null)
        {
            _backdropConfiguration.Theme = dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
            _acrylic.TintColor = dark
                ? Color.FromArgb(0xFF, 0x10, 0x16, 0x23)
                : Color.FromArgb(0xFF, 0xF2, 0xF6, 0xFB);
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
