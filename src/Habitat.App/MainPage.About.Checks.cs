#if HABITAT_UI_CHECKS
using Habitat.Features.About;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckAboutUiAsync(string log)
    {
        var originalTheme = RequestedTheme;
        Task? showing = null;
        var draft = Composer.Text;
        try
        {
            if (OptionsMenu.Items.Last() is not MenuFlyoutItem last || last.Text != UiText.Get("AboutMenu") ||
                OptionsMenu.Items[^2] is not MenuFlyoutSeparator)
                throw new InvalidOperationException($"About must be the final separated menu entry. Language={UiText.Language}, preference={_preferences.InterfaceLanguage}, final={(OptionsMenu.Items.Last() as MenuFlyoutItem)?.Text}, previous={OptionsMenu.Items[^2].GetType().Name}");
            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            {
                RequestedTheme = theme;
                showing = ShowAboutAsync();
                await Task.Delay(150);
                if (_activeDialog?.Content is not AboutView view || !HasAppearanceOverlay ||
                    _activeDialog.Title as string != "About HABITAT" || _activeDialog.RequestedTheme != theme ||
                    !view.AppDetails.StartsWith($"HABITAT {view.AppVersion}") || view.AppVersion == "Unknown")
                    throw new InvalidOperationException("About lost its version, theme or modal state.");
                await ShowAboutAsync();
                await ShowSetupAsync();
                await ShowPreferencesAsync();
                if (!ReferenceEquals(_activeDialog.Content, view))
                    throw new InvalidOperationException("Another dialog replaced About.");
                ((IInvokeProvider)new HyperlinkButtonAutomationPeer(view.Links[0]).GetPattern(PatternInterface.Invoke)).Invoke();
                await Task.Delay(100);
                if (!view.ActionStatus.Text.StartsWith("Could not open Chrome."))
                    throw new InvalidOperationException("About links bypassed the isolated launcher or lost failure recovery.");
                FocusComposer();
                await Task.Delay(50);
                if (ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot), Composer))
                    throw new InvalidOperationException("The composer stole focus from About.");
                _activeDialog.Hide();
                await showing;
                showing = null;
                if (HasAppearanceOverlay || _activeDialog is not null || Composer.Text != draft)
                    throw new InvalidOperationException("Closing About lost a draft or left the overlay active.");
            }
            File.AppendAllText(log, "PASS: About menu position, dynamic version, both themes, exclusive dialog, isolated links, focus and retained draft\n");
        }
        finally
        {
            _activeDialog?.Hide();
            if (showing is not null) await showing;
            RequestedTheme = originalTheme;
        }
    }
}
#endif
