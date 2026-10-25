#if HABITAT_UI_CHECKS
using Habitat.Features.Preferences;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Habitat;

public sealed partial class MainPage
{
    internal async Task CheckWindowMenuActionsAsync(string log)
    {
        var original = _preferences;
        try
        {
            foreach (var language in new[] { "en", "es" })
            {
                _preferences = _preferences with { InterfaceLanguage = language };
                ApplyInterfaceLanguage();
                await InvokeAsync(OverflowButton);
                if (!OptionsMenu.IsOpen || AutomationProperties.GetName(OverflowButton) != UiText.Get("HABITATMenu"))
                    throw new InvalidOperationException($"The window menu did not open in {language}.");
                // Activation can queue a composer-focus request while the menu is opening.
                FocusComposer();
                await Task.Delay(100);
                if (!OptionsMenu.IsOpen || ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Composer))
                    throw new InvalidOperationException("Composer stole focus from the open window menu.");
                var preferences = (MenuFlyoutItem)OptionsMenu.Items[0];
                if (preferences.Text != UiText.Get("PreferencesMessage"))
                    throw new InvalidOperationException("Preferences menu label did not follow the interface language.");
                ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(preferences).GetPattern(PatternInterface.Invoke)).Invoke();
                await Task.Delay(180);
                if (_activeDialog?.Content is not PreferencesView || _activeDialog.Title as string != UiText.Get("Preferences"))
                    throw new InvalidOperationException("Preferences did not open from the window menu.");
                _activeDialog.Hide();
                for (var attempt = 0; attempt < 80 && (_activeDialog is not null ||
                    !ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Composer)); attempt++)
                    await Task.Delay(25);
                if (_activeDialog is not null || !ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Composer))
                    throw new InvalidOperationException($"Closing preferences did not restore the composer: dialog={_activeDialog is not null}, menu={OptionsMenu.IsOpen}, focused={FocusManager.GetFocusedElement(XamlRoot)?.GetType().Name}.");
                await InvokeAsync(OverflowButton);
                if (!OptionsMenu.IsOpen) throw new InvalidOperationException("Window menu did not reopen.");
                OptionsMenu.Hide();
                await Task.Delay(100);
                if (!ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Composer))
                    throw new InvalidOperationException("Dismissing the menu did not restore the composer.");
                File.AppendAllText(log, $"PASS: menu opens, keeps focus, opens Preferences and restores writing in {language}.\n");
            }
        }
        finally
        {
            OptionsMenu.Hide();
            _activeDialog?.Hide();
            _preferences = original;
            ApplyInterfaceLanguage();
        }
    }
}
#endif
