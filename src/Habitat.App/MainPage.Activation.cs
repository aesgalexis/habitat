using Habitat.Activation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private bool _receivingActivation;
    // Injection is restricted to the isolated UI-check build.
#if HABITAT_UI_CHECKS
    private Func<string, Task<bool>>? _confirmCapturedText;
#endif

    internal async void ReceiveActivation(string? text)
    {
        await RunOperationAsync(() => ReceiveActivationAsync(text));
    }

    private async Task ReceiveActivationAsync(string? text)
    {
        // Dismiss modal UI through its normal cancel/close path before focusing.
        if (_operations.IsStopping || _receivingActivation || _updateDialogOpen || _updateHandoff) return;
        _receivingActivation = true;
        try
        {
            if (_activeDialog is not null)
            {
                _activeDialog.Hide();
                for (var attempt = 0; attempt < 100 && _activeDialog is not null; attempt++) await Task.Delay(20);
                if (_activeDialog is not null || _operations.IsStopping) return;
            }
            OptionsMenu.Hide();
            GmailFlyout.Hide();
            FacebookRecovery.Hide();
            if (_tasksPanelOpen) SetTasksOpen(false);
            if (string.IsNullOrEmpty(text) || text.Length > ActivationChannel.MaxTextLength) return;
            var destination = _destination;
            var previous = Composer.Text;
            if (SameCapturedText(previous, text)) return;
            if (previous.Length > 0)
            {
                bool replace;
#if HABITAT_UI_CHECKS
                if (_confirmCapturedText is not null) replace = await _confirmCapturedText(text);
                else
#endif
                {
                    var dialog = UiLanguage.Watch(new ContentDialog
                    {
                        XamlRoot = XamlRoot, Title = UiText.Get("ReplaceDraftMessage"),
                        Content = UiText.Get("TheEditorAlreadyContainsADraftReplaceIt"),
                        PrimaryButtonText = UiText.Get("ReplaceDraft"), CloseButtonText = UiText.Get("KeepDraft"),
                        DefaultButton = ContentDialogButton.Close
                    }, ("Title", () => (object?)(UiText.Get("ReplaceDraftMessage"))), ("Content", () => (object?)(UiText.Get("TheEditorAlreadyContainsADraftReplaceIt"))), ("PrimaryButtonText", () => (object?)(UiText.Get("ReplaceDraft"))), ("CloseButtonText", () => (object?)(UiText.Get("KeepDraft"))));
                    StyleDialog(dialog);
                    replace = await ShowDialogAsync(dialog) == ContentDialogResult.Primary;
                }
                if (!replace || _destination != destination || Composer.Text != previous || _operations.IsStopping) return;
            }
            // This is deliberately independent of ComposerCoordinator.SubmitAsync.
            Composer.Text = text;
        }
        finally { _receivingActivation = false; FocusComposer(); }
    }

    private static bool SameCapturedText(string left, string right) =>
        left.Replace("\r\n", "\n").Replace('\r', '\n') == right.Replace("\r\n", "\n").Replace('\r', '\n');

    private async Task ApplyGlobalHotkeyAsync()
    {
        var error = await _services.GlobalHotkey.ApplyAsync(_preferences.GlobalHotkeyEnabled);
        if (error is not null && !_operations.IsStopping) SetStatus(error, StatusTone.Warning);
    }
}
