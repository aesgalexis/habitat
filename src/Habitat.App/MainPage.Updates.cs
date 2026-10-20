using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Habitat;

public sealed partial class MainPage
{
    private readonly Button UpdateButton = new()
    {
        Content = new FontIcon { Glyph = "\uE72C", FontSize = 20 },
        Width = 40, Height = 40, MinWidth = 0, MinHeight = 0,
        Padding = new Thickness(0), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 0, 24), Visibility = Visibility.Collapsed
    };
    private bool _openingUpdate;
    private bool _checkingUpdate;

    private void BuildUpdateInterface(Grid surface)
    {
        UseStableWidgetButton(UpdateButton);
        AutomationProperties.SetName(UpdateButton, "Update HABITAT");
        AutomationProperties.SetLiveSetting(UpdateButton, AutomationLiveSetting.Polite);
        UpdateButton.Click += async (_, _) => await RunOperationAsync(OpenUpdateAsync);
        surface.Children.Add(UpdateButton);
        RenderUpdateNotice();
    }

    private void TickUpdates(DateTimeOffset now)
    {
        if (_services.Paths.IsIsolated || _operations.IsStopping || _checkingUpdate || now < _services.Updates.NextCheck) return;
        _ = RunOperationAsync(async () =>
        {
            _checkingUpdate = true;
            try
            {
                await _services.Updates.CheckIfDueAsync(now, _operations.Token);
                if (!_operations.IsStopping) RenderUpdateNotice();
            }
            finally { _checkingUpdate = false; }
        });
    }

    private void RenderUpdateNotice(bool openFailed = false)
    {
        var available = _services.Updates.Available;
        UpdateButton.Visibility = available is null ? Visibility.Collapsed : Visibility.Visible;
        var text = available is null ? "Update HABITAT" : openFailed
            ? $"Could not open Chrome. Check that Google Chrome is installed, then retry downloading HABITAT {available.Version}."
            : $"HABITAT {available.Version} is available. Open GitHub to download the update using the same installer format (EXE or ZIP).";
        AutomationProperties.SetName(UpdateButton, available is null ? "Update HABITAT" : $"Update HABITAT to {available.Version}");
        AutomationProperties.SetHelpText(UpdateButton, text);
        AutomationProperties.SetItemStatus(UpdateButton, openFailed ? "Could not open download. Retry." : available is null ? "" : "Update available");
        ToolTipService.SetToolTip(UpdateButton, text);
        if (Content is Grid surface) SizeTopWidgets(surface);
    }

    private async Task OpenUpdateAsync()
    {
        if (_openingUpdate || _services.Updates.Available is not { } update) return;
        _openingUpdate = true;
        UpdateButton.IsEnabled = false;
        try
        {
            var opened = await _services.LaunchBrowser(update.ReleasePage);
            if (!_operations.IsStopping) RenderUpdateNotice(openFailed: !opened);
        }
        finally { _openingUpdate = false; UpdateButton.IsEnabled = true; }
    }

    private void PaintUpdateButton()
    {
        var dark = RequestedTheme == ElementTheme.Dark;
        var accent = new SolidColorBrush(dark ? Color(0xFF, 0x89, 0x95, 0xFF) : Color(0xFF, 0x66, 0x74, 0xF4));
        UpdateButton.Foreground = accent;
        UpdateButton.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var key in new[] { "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed" })
            UpdateButton.Resources[key] = accent;
        UpdateButton.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(dark ? Color(0x24, 255, 255, 255) : Color(0x12, 0, 0, 0));
    }
}
