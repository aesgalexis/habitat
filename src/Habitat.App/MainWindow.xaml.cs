using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Habitat;

public sealed class MainWindow : Window
{
    private readonly BridgeService _bridge;
    private readonly MainPage _page;

    public MainWindow(BridgeService bridge)
    {
        _bridge = bridge;
        Title = "HABITAT";
        _page = new MainPage(bridge);
        Content = _page;
        SystemBackdrop = new DesktopAcrylicBackdrop();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(860, 720));
        Closed += OnClosed;
    }

    public void SetBackgroundOpacity(double percent) => _page.SetBackgroundOpacity(percent);

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        await _bridge.DisposeAsync();
    }
}
