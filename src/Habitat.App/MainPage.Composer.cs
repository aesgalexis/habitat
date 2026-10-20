using Habitat.Services;
using Habitat.Features.Composer;

namespace Habitat;

public sealed partial class MainPage
{
    private readonly ComposerDrafts _composerDrafts = new();
    private readonly ComposerCoordinator _composer;
    private bool _restoringComposerDraft;

    private void ConfigureComposerRoutes()
    {
        foreach (var destination in _destinationCatalog.Available)
        {
            foreach (var external in new[] { false, true })
            {
                if (_destinationCatalog.WindowsApp(destination) is not null)
                    _composer.Register(destination, external, OpenWindowsAppAsync, _ => false, exclusive: true, allowEmpty: true);
                else if (destination == ComposerDestination.OpenAI)
                    _composer.Register(destination, external, SendOpenAIAsync,
                        text => !(external && string.IsNullOrWhiteSpace(text)) && _bridge is not { HasMessageInFlight: false }, external);
                else if (destination == ComposerDestination.Codex)
                    _composer.Register(destination, external, SendCodexComposerAsync,
                        text => _codexSendCancellation is not null || !(external && string.IsNullOrWhiteSpace(text)) && CodexChat.IsBusy,
                        exclusiveForEmpty: external);
                else if (_destinationSections.TryGetValue(destination, out var section) && (!external || destination == ComposerDestination.VSCode))
                    _composer.Register(destination, external, SubmitSectionAsync, _ => _destinationSections[destination].IsBusy);
                else
                    _composer.Register(destination, external, LaunchExternalAsync,
                        _ => _destinationSections.TryGetValue(destination, out var view) && view.IsBusy, true);
            }
        }
    }

    private Task OpenWindowsAppAsync(ComposerCoordinator.Submission request)
    {
        if (_destinationCatalog.WindowsApp(request.Destination) is not { } app) return Task.CompletedTask;
        try
        {
            if (!_services.LaunchWindowsApp(app, request.Text))
                SetStatus($"Could not open {app.Name}. Check that the application is installed or add it again in Preferences.", StatusTone.Warning);
            else if (app.PassText && request.Text.Length > 0) { request.Accept(); request.Clear(); }
        }
        catch (Exception exception)
        {
            _services.ReportError("Open Windows application", exception);
            SetStatus($"Could not open {app.Name}. Check the application in Preferences and try again.", StatusTone.Warning);
        }
        finally { if (!request.External) FocusComposer(); }
        return Task.CompletedTask;
    }

    private Task SendOpenAIAsync(ComposerCoordinator.Submission request)
    {
        var text = request.Text.Trim();
        if (text.Length == 0) return OpenChatWindowAsync();
        if (_bridge is null) return Task.CompletedTask;
        if (!_bridge.TryQueueMessage(ComposerCoordinator.OpenAIText(text, _preferences.TextOnly), out var reason, request.External))
        {
            SetOpenAIStatus(reason, StatusTone.Warning);
            FocusComposer();
            return Task.CompletedTask;
        }
        AddMessage("YOU", text, MessageTone.User);
        request.Accept();
        request.Clear();
        SetOpenAIStatus("Sending to browser…", StatusTone.Active);
        UpdateSendState();
        if (!request.External) FocusComposer();
        return Task.CompletedTask;
    }

    private Task SendCodexComposerAsync(ComposerCoordinator.Submission request) => !string.IsNullOrWhiteSpace(request.Text)
        ? SendToCodexAsync(request.Text, request.Draft, request.External)
        : CodexChat.Target is not null ? CodexChat.OpenAsync() : LaunchExternalAsync(request);

    private async Task SubmitSectionAsync(ComposerCoordinator.Submission request)
    {
        var section = _destinationSections[request.Destination];
        if (section is Features.Destinations.VSCodeSection vscode)
        {
            if (string.IsNullOrWhiteSpace(request.Text)) { await vscode.OpenProjectAsync(); return; }
            if (await vscode.RunCommandAsync(request.Text, request.Accept, request.External)) request.Clear();
            return;
        }
        request.Accept();
        await section.SubmitAsync(request.Text);
    }

    private async Task LaunchExternalAsync(ComposerCoordinator.Submission request)
    {
        var destination = request.Destination;
        var draft = request.Text;
        if (destination == ComposerDestination.Gmail)
        {
            await ((Features.Destinations.GmailDraftSection)_destinationSections[destination]).OpenBrowserAsync();
            return;
        }
        try
        {
            if (destination == ComposerDestination.Terminal && !string.IsNullOrWhiteSpace(draft) &&
                !await (_confirmTerminal ?? ConfirmTerminalAsync)(draft)) return;
            if (!request.CanProceed) return;
            request.Accept();
            var opened = destination == ComposerDestination.Terminal
                ? (_launchTerminal ?? _powerShell.Run)(draft)
                : destination == ComposerDestination.Run
                ? (string.IsNullOrWhiteSpace(draft) ? _openRun() : _launchRun(draft))
                : destination == ComposerDestination.Notepad
                ? await _launchNotepad(draft)
                : (request.External ? ComposerDestinations.ExternalUri(destination, draft, _gmailAuth?.Email,
                    ((Features.Translation.TranslateSection)_destinationSections[ComposerDestination.Translate]).TargetLanguage)
                    : ComposerDestinations.SearchUri(destination, draft, _gmailAuth?.Email)) is { } uri && await _launchBrowser(uri);
            if (opened)
            {
                if (destination != ComposerDestination.Translate) request.Clear();
            }
            else SetStatus(DestinationLaunchError(destination), StatusTone.Warning);
        }
        catch (Exception exception)
        {
            _services.ReportError("Launch destination", exception);
            if (!_operations.IsStopping) SetStatus(DestinationLaunchError(destination), StatusTone.Warning);
        }
        finally { if (!request.External) FocusComposer(); }
    }

    private static string DestinationLaunchError(string destination) => destination == ComposerDestination.Run
        ? "Could not run this command. Check the name or path and try again."
        : $"Could not open {ComposerDestinations.Label(destination)}. Try again.";

    private void RestoreComposerDraft()
    {
        var text = _composerDrafts.Text(_destination);
        if (Composer.Text == text) return;
        _restoringComposerDraft = true;
        try { Composer.Text = text; }
        finally { _restoringComposerDraft = false; }
    }
}
