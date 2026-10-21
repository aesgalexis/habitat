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
        var dialog = UiLanguage.Watch(new ContentDialog
        {
            XamlRoot = XamlRoot, Title = UiText.Get("AboutTitle"), Content = view,
            CloseButtonText = UiText.Get("Close"), DefaultButton = ContentDialogButton.Close
        }, ("Title", () => (object?)(UiText.Get("AboutTitle"))), ("CloseButtonText", () => (object?)(UiText.Get("Close"))));
        StyleDialog(dialog);
        foreach (var link in view.Links)
        {
            link.Click += async (_, _) => await RunOperationAsync(async () =>
            {
                link.IsEnabled = false;
                try
                {
                    view.ActionStatus.Text = await _launchBrowser((Uri)link.Tag)
                        ? UiText.Get("OpenedInChrome") : UiText.Get("ChromeOpenFailed");
                }
                finally { link.IsEnabled = true; }
            });
        }
        view.CopyDetails.Click += (_, _) => view.ActionStatus.Text = _services.CopyText(view.AppDetails)
            ? UiText.Get("AppDetailsCopied") : UiText.Get("CopyAppDetailsFailed");
        _appearanceDialogOpen = true;
        try { await ShowDialogAsync(dialog); }
        finally { _appearanceDialogOpen = false; FocusComposer(); }
    }
}
