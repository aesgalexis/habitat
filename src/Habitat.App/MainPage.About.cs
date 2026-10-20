using Habitat.Features.About;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task ShowAboutAsync()
    {
        if (HasAppearanceOverlay || _activeDialog is not null || _operations.IsStopping) return;
        var view = new AboutView
        {
            Width = Math.Max(240, Math.Min(480, XamlRoot.Size.Width - 96)),
            MaxHeight = Math.Max(160, Math.Min(520, XamlRoot.Size.Height - 180))
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "About HABITAT", Content = view,
            CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close
        };
        StyleDialog(dialog);
        foreach (var link in view.Links)
        {
            link.Click += async (_, _) => await RunOperationAsync(async () =>
            {
                link.IsEnabled = false;
                try
                {
                    view.ActionStatus.Text = await _launchBrowser((Uri)link.Tag)
                        ? "Opened in Chrome." : "Could not open Chrome. Check that Google Chrome is installed, then try again.";
                }
                finally { link.IsEnabled = true; }
            });
        }
        view.CopyDetails.Click += (_, _) => view.ActionStatus.Text = _services.CopyText(view.AppDetails)
            ? "App details copied." : "Could not copy app details. Try again.";
        _appearanceDialogOpen = true;
        try { await ShowDialogAsync(dialog); }
        finally { _appearanceDialogOpen = false; FocusComposer(); }
    }
}
