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
            var label = _destinationCatalog.WindowsApp(destination) is { } app ? $"Open {app.Name}" :
                ComposerDestinations.ExternalActionLabel(destination, !string.IsNullOrWhiteSpace(Composer.Text));
            ToolTipService.SetToolTip(button, label);
            AutomationProperties.SetName(button, label);
            AutomationProperties.SetHelpText(button, destination == ComposerDestination.Gmail
                ? "Open saved Gmail drafts in your browser." : "Use the current editor text and switch to the destination window.");
            button.IsEnabled = !_operations.IsStopping && _composer.CanSend(destination, Composer.Text, external: true);
        }
    }

    private Task OpenSelectedDestinationAsync()
    {
        if (_operations.IsStopping || HasAppearanceOverlay ||
            !_externalDestinationButtons.TryGetValue(_destination, out var button) || !button.IsEnabled)
            return Task.CompletedTask;
        return LaunchDestinationAsync(_destination, Composer.Text, openExternally: true);
    }

    private async Task OpenChatWindowAsync()
    {
        try
        {
            if (!await _openChat()) SetStatus("Could not open the linked ChatGPT conversation. Reconnect the browser extension.", StatusTone.Warning);
        }
        catch (Exception exception)
        {
            _services.ReportError("Open ChatGPT", exception);
            SetStatus("Could not open ChatGPT. Try again.", StatusTone.Warning);
        }
    }

    private Func<Task<bool>> _openChat;
}
