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
            var label = _destinationCatalog.WindowsApp(destination) is { } app ? UiText.Format("OpenMessage", app.Name) :
                ComposerDestinations.ExternalActionLabel(destination, !string.IsNullOrWhiteSpace(Composer.Text));
            ToolTipService.SetToolTip(button, label);
            AutomationProperties.SetName(button, label);
            UiLanguage.SetHelpText(button, () => destination == ComposerDestination.Gmail
                ? UiText.Get("OpenSavedGmailDraftsInYourBrowser") : UiText.Get("UseTheCurrentEditorTextAndSwitchTo"));
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
            if (!await _openChat()) SetStatus(UiText.Get("CouldNotOpenTheLinkedChatGPTConversationReconnect"), StatusTone.Warning);
        }
        catch (Exception exception)
        {
            _services.ReportError("Open ChatGPT", exception);
            SetStatus(UiText.Get("CouldNotOpenChatGPTTryAgain"), StatusTone.Warning);
        }
    }

    private Func<Task<bool>> _openChat;
}
