#if HABITAT_UI_CHECKS
using Habitat.Features.Destinations;
using Habitat.Features.Translation;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckExternalDestinationsUiAsync(string logPath)
    {
        var browser = _launchBrowser;
        var notepad = _launchNotepad;
        var codex = _launchCodex;
        var terminal = _launchTerminal;
        var run = _launchRun;
        var openRun = _openRun;
        var openChat = _openChat;
        var confirm = _confirmTerminal;
        var originalVSCode = _destinationSections[ComposerDestination.VSCode];
        var translate = (TranslateSection)_destinationSections[ComposerDestination.Translate];
        const string text = "  unsent café & #? ☕\r\nsecond line  ";
        try
        {
            foreach (var destination in ComposerDestinations.Ordered)
            {
                SelectDestination(destination);
                Composer.Text = text;
                await Task.Delay(25);
                var external = _externalDestinationButtons[destination];
                var iconPosition = external.TransformToVisual(DestinationRail).TransformPoint(new());
                var labelPosition = _destinationButtons[destination].TransformToVisual(DestinationRail).TransformPoint(new());
                if (_externalDestinationButtons.Count(pair => pair.Value.Visibility == Visibility.Visible) != 1 ||
                    external.Visibility != Visibility.Visible || !external.IsTabStop ||
                    labelPosition.X - iconPosition.X < external.ActualWidth + 7 || Math.Abs(labelPosition.Y - iconPosition.Y) > 1 ||
                    AutomationProperties.GetName(external) != ComposerDestinations.ExternalActionLabel(destination, true))
                    throw new Exception("The single external button must align left of the selected row, with an English accessible action label.");
            }
            var opened = new List<Uri>();
            _launchBrowser = uri => { opened.Add(uri); return Task.FromResult(true); };
            foreach (var destination in new[] { ComposerDestination.Google, ComposerDestination.YouTube,
                ComposerDestination.Translate, ComposerDestination.Gmail, ComposerDestination.GoogleMaps })
            {
                SelectDestination(destination);
                Composer.Text = text;
                var exact = Composer.Text;
                if (destination == ComposerDestination.Translate) translate.SelectTarget("el");
                var before = opened.Count;
                await OpenSelectedDestinationAsync();
                if (opened.Count != before + 1 || opened[^1] != ComposerDestinations.ExternalUri(destination, exact, _gmailAuth?.Email, translate.TargetLanguage) ||
                    _bridge!.HasMessageInFlight || (destination is ComposerDestination.Translate or ComposerDestination.Gmail ? Composer.Text != exact : Composer.Text != ""))
                    throw new Exception($"{destination} must open its action once using the current, complete unsent text.");
                Composer.Text = "";
                await OpenSelectedDestinationAsync();
                if (opened[^1] != ComposerDestinations.ExternalUri(destination, "", _gmailAuth?.Email, translate.TargetLanguage))
                    throw new Exception("Empty external actions must open a starting page or saved Gmail drafts.");
            }

            SelectDestination(ComposerDestination.Gmail);
            Composer.Text = text;
            var receipt = new TaskCompletionSource<bool>();
            var attempts = 0;
            _launchBrowser = _ => { attempts++; return receipt.Task; };
            var pending = OpenSelectedDestinationAsync();
            await OpenSelectedDestinationAsync();
            SendCurrentMessage();
            if (attempts != 1 || SendButton.IsEnabled || _externalDestinationButtons[_destination].IsEnabled)
                throw new Exception("Both action buttons must reject duplicate delivery during an external launch.");
            SelectDestination(ComposerDestination.Google);
            Composer.Text = "newer draft";
            var hadSearchContent = _destinationSections[ComposerDestination.Google].HasContent;
            SendCurrentMessage();
            if (_destinationSections[ComposerDestination.Google].HasContent != hadSearchContent)
                throw new Exception("Enter must not start an integrated search while an external delivery is pending.");
            receipt.SetResult(true);
            await pending;
            if (Composer.Text != "newer draft") throw new Exception("Opening externally erased a newer draft.");
            _launchBrowser = _ => Task.FromResult(false);
            await OpenSelectedDestinationAsync();
            if (Composer.Text != "newer draft" || !SendButton.IsEnabled)
                throw new Exception("External launch failure must preserve the draft and allow recovery.");

            SelectDestination(ComposerDestination.Notepad);
            Composer.Text = text;
            var exactNote = Composer.Text;
            string? note = null;
            _launchNotepad = value => { note = value; return Task.FromResult(true); };
            await OpenSelectedDestinationAsync();
            if (note != exactNote || Composer.Text != "") throw new Exception("External Notepad must receive exact text.");
            await OpenSelectedDestinationAsync();
            if (note != "") throw new Exception("Empty Notepad must open a blank document.");

            SelectDestination(ComposerDestination.Codex);
            Composer.Text = text;
            var instruction = Composer.Text;
            var codexRuns = 0;
            _launchCodex = (value, _) => { if (value != instruction) throw new Exception("Codex text changed."); codexRuns++; return Task.FromResult(CodexDeliveryResult.Sent); };
            await OpenSelectedDestinationAsync();
            if (codexRuns != 1 || Composer.Text != "") throw new Exception("External Codex must execute once through the existing sender.");
            _launchBrowser = uri => { opened.Add(uri); return Task.FromResult(true); };
            await OpenSelectedDestinationAsync();
            if (opened[^1] != new Uri("codex://threads/new") || codexRuns != 1) throw new Exception("Empty Codex must open without sending.");

            SelectDestination(ComposerDestination.Terminal);
            Composer.Text = "Write-Output 'external test'";
            var runs = 0;
            var confirmations = 0;
            _launchTerminal = _ => { runs++; return true; };
            _confirmTerminal = _ => { confirmations++; return Task.FromResult(false); };
            await OpenSelectedDestinationAsync();
            if (runs != 0 || Composer.Text == "" || confirmations != 1) throw new Exception("External Terminal must respect cancellation.");
            _confirmTerminal = _ => { confirmations++; return Task.FromResult(true); };
            await OpenSelectedDestinationAsync();
            await OpenSelectedDestinationAsync();
            if (runs != 2 || confirmations != 2) throw new Exception("Terminal must confirm commands and open an empty shell without confirmation.");

            SelectDestination(ComposerDestination.Run);
            Composer.Text = "calc";
            var programs = new List<string>();
            _launchRun = value => { programs.Add(value); return true; };
            var dialogs = 0;
            _openRun = () => { dialogs++; return true; };
            await OpenSelectedDestinationAsync();
            await OpenSelectedDestinationAsync();
            if (!programs.SequenceEqual(new[] { "calc" }) || dialogs != 1) throw new Exception("Run opens the command once or its empty dialog.");

            var folder = new VSCodeFolder("External project", Path.Combine(_services.Paths.LocalDirectory, "external-vscode"));
            var endpoint = new VSCodeEndpoint(Guid.NewGuid().ToString(), new string('a', 64), 12345);
            var context = new VSCodeContext(endpoint.SessionId, "external", "Workspace", [folder], folder.Path, 1, true, false);
            var project = new VSCodeProject(endpoint, context, folder);
            var deliveries = 0;
            var windows = 0;
            using var vscode = new VSCodeSection(_ => Task.FromResult<IReadOnlyList<VSCodeProject>>([project]),
                (target, value, _) => { if (target != project || value != "echo external") throw new Exception("VS Code target changed."); deliveries++; return Task.FromResult(new VSCodeRunResult(true, "")); },
                _ => { },
                target => { if (target != project) throw new Exception("Wrong VS Code window."); windows++; return Task.FromResult(false); });
            vscode.StateChanged += () => { PresentDestinationSection(); UpdateWritingLayout(); UpdateSendState(); };
            _destinationSections[ComposerDestination.VSCode] = vscode;
            SelectDestination(ComposerDestination.VSCode);
            await vscode.RefreshAsync();
            Composer.Text = "echo external";
            await OpenSelectedDestinationAsync();
            if (deliveries != 1 || windows != 1 || Composer.Text != "" || !vscode.StatusText.StartsWith("Command sent.") || _terminalConfirmationOpen)
                throw new Exception("VS Code must execute directly and open the captured project after one delivery, even if focus fails.");
            await OpenSelectedDestinationAsync();
            if (deliveries != 1 || windows != 2) throw new Exception("An empty VS Code open must not execute another command.");

            SelectDestination(ComposerDestination.OpenAI);
            Composer.Text = "";
            var chats = 0;
            _openChat = () => { chats++; return Task.FromResult(true); };
            await OpenSelectedDestinationAsync();
            Composer.Text = "unsent ChatGPT draft";
            await OpenSelectedDestinationAsync();
            if (chats != 1 || Composer.Text != "unsent ChatGPT draft" || _bridge!.HasMessageInFlight)
                throw new Exception("Empty ChatGPT opens the linked chat; disconnected sends preserve the unsent draft.");
            File.AppendAllText(logPath, "PASS: external action icon placement, English labels, current drafts, empty destinations, exact routing, translation language, duplicate gating, failures, newer edits, Terminal confirmation, direct VS Code execution and window handoff\n");
        }
        finally
        {
            _launchBrowser = browser; _launchNotepad = notepad; _launchCodex = codex;
            _launchTerminal = terminal; _launchRun = run; _openRun = openRun; _openChat = openChat; _confirmTerminal = confirm;
            _destinationSections[ComposerDestination.VSCode] = originalVSCode;
            translate.SelectTarget("en");
            Composer.Text = "";
        }
    }
}
#endif
