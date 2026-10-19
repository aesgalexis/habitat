#if HABITAT_UI_CHECKS
using Habitat.Features.Setup;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckSetupUiAsync(string log)
    {
        var original = _preferences;
        Task? showing = null;
        try
        {
            var draft = Composer.Text;
            showing = ShowSetupAsync();
            await Task.Delay(120);
            if (_activeDialog?.Content is not SetupView view || !HasAppearanceOverlay || !_setupDialogOpen ||
                _activeDialog.Title as string != "Set up HABITAT")
                throw new InvalidOperationException("Setup did not open as a separate accessible dialog.");
            await ShowSetupAsync();
            if (!ReferenceEquals(view, _activeDialog.Content)) throw new InvalidOperationException("Setup opened twice.");
            view.Refresh(false, false, false, false, false);
            if (view.GetChrome.Visibility != Visibility.Visible || view.InstallExtension.IsEnabled || view.OpenChat.IsEnabled ||
                view.OpenExtensions.IsEnabled || !view.ConnectionStatus.Text.Contains("Waiting"))
                throw new InvalidOperationException("Missing Chrome did not offer recoverable setup.");
            view.Refresh(true, true, false, true, false);
            if (view.GetChrome.Visibility != Visibility.Collapsed || !view.OpenExtensions.IsEnabled ||
                view.ConnectionStatus.Text != "Google: Connected · YouTube: Waiting · Translate: Connected" ||
                view.ChatStatus.Text != "Waiting for a linked ChatGPT conversation.")
                throw new InvalidOperationException("Independent extension channels were reported incorrectly.");
            view.Refresh(true, true, true, true, true);
            if (view.ChatStatus.Text != "ChatGPT connected." || _services.OpenSetupFolder(AppContext.BaseDirectory) || _services.OpenChromeDownload())
                throw new InvalidOperationException("Setup checks escaped isolated launchers.");
            _activeDialog.Hide();
            await showing;
            showing = null;
            if (_setupDialogOpen || HasAppearanceOverlay || !Preferences.Load(_services.Paths.PreferencesPath).SetupGuideSeen || Composer.Text != draft)
                throw new InvalidOperationException("Closing setup lost a draft or failed to save first-run state.");
            if (!OptionsMenu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Set up HABITAT…"))
                throw new InvalidOperationException("Setup must remain available from the menu.");
            File.AppendAllText(log, "PASS: setup first-run persistence, missing Chrome recovery, independent connection states, menu access, retained draft and isolated launchers\n");
        }
        finally
        {
            _activeDialog?.Hide();
            if (showing is not null) await showing;
            _preferences = original;
            SavePreferences();
        }
    }
}
#endif
