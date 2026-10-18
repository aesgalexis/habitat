using Habitat.Services;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private Func<bool> _openRun;

    private void UpdateExternalDestinationState()
    {
        foreach (var (destination, button) in _externalDestinationButtons)
        {
            var label = ComposerDestinations.ExternalActionLabel(destination, !string.IsNullOrWhiteSpace(Composer.Text));
            ToolTipService.SetToolTip(button, label);
            AutomationProperties.SetName(button, label);
            AutomationProperties.SetHelpText(button, "Use the current editor text and switch to the destination window.");
            button.IsEnabled = !_operations.IsStopping && !_destinationLaunching &&
                (destination == ComposerDestination.Codex ? _codexSendCancellation is null && (string.IsNullOrWhiteSpace(Composer.Text) || !CodexChat.IsBusy) :
                 destination == ComposerDestination.OpenAI ? string.IsNullOrWhiteSpace(Composer.Text) || _bridge is { HasMessageInFlight: false } :
                 !_destinationSections.TryGetValue(destination, out var section) || !section.IsBusy);
        }
    }

    private Task OpenSelectedDestinationAsync()
    {
        if (_operations.IsStopping || HasAppearanceOverlay || !_externalDestinationButtons[_destination].IsEnabled)
            return Task.CompletedTask;
        if (_destination == ComposerDestination.OpenAI)
        {
            if (!string.IsNullOrWhiteSpace(Composer.Text)) SendCurrentMessage(openExternally: true);
            else return RunOperationAsync(OpenChatWindowAsync);
            return Task.CompletedTask;
        }
        return LaunchDestinationAsync(_destination, Composer.Text, openExternally: true);
    }

    private async Task OpenChatWindowAsync()
    {
        if (_destinationLaunching) return;
        _destinationLaunching = true;
        UpdateSendState();
        try
        {
            if (!await _openChat()) SetStatus("Could not open the linked ChatGPT conversation. Reconnect the browser extension.", StatusTone.Warning);
        }
        catch (Exception exception)
        {
            _services.ReportError("Open ChatGPT", exception);
            SetStatus("Could not open ChatGPT. Try again.", StatusTone.Warning);
        }
        finally { _destinationLaunching = false; UpdateSendState(); }
    }

    private Func<Task<bool>> _openChat;
}
