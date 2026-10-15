#if HABITAT_UI_CHECKS
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckDestinationsUiAsync(string logPath)
    {
        var saved = _preferences;
        var launcher = _launchBrowser;
        var notepadLauncher = _launchNotepad;
        var codexLauncher = _launchCodex;
        var terminalLauncher = _launchTerminal;
        var terminalConfirmation = _confirmTerminal;
        const string draft = "café & YouTube #1";
        try
        {
            Composer.Text = draft;
            foreach (var destination in Enum.GetValues<ComposerDestination>())
            {
                SelectDestination(destination);
                if (Composer.Text != draft ||
                    ComposerHint.Text != ComposerDestinations.Placeholder(destination) || Composer.PlaceholderText != "" ||
                    AutomationProperties.GetItemStatus(_destinationButtons[destination]) != "Selected" ||
                    _destinationButtons.Count(item => AutomationProperties.GetItemStatus(item.Value) == "Selected") != 1 ||
                    AutomationProperties.GetName(_destinationButtons[destination]) != ComposerDestinations.Label(destination) ||
                    Preferences.Load(_services.Paths.PreferencesPath).Destination != destination)
                    throw new InvalidOperationException("Destination selection, draft, labels or persistence failed.");
            }
            await Task.Delay(100);
            SizeTopWidgets((Microsoft.UI.Xaml.Controls.Grid)Content);
            var surface = (Microsoft.UI.Xaml.Controls.Grid)Content;
            var selector = DestinationRail.TransformToVisual(surface).TransformPoint(new Windows.Foundation.Point());
            if (Math.Abs(selector.Y + DestinationRail.ActualHeight / 2 - surface.ActualHeight / 2) > 1 ||
                Math.Abs(selector.X + DestinationRail.ActualWidth - (surface.ActualWidth - surface.Padding.Right)) > 1 ||
                DestinationRail.Children.Count != Enum.GetValues<ComposerDestination>().Length || DestinationRail.Spacing < 16 ||
                !ReferenceEquals(DestinationRail.Parent, Content))
                throw new InvalidOperationException("Destinations must be separated and vertically centered at the right edge.");
            File.AppendAllText(logPath, "PASS: destination labels, exclusive selection, draft preservation, persistence and separated right-side placement\n");

            Composer.Text = "";
            foreach (var destination in Enum.GetValues<ComposerDestination>())
            {
                SelectDestination(destination);
                await Task.Delay(60);
                var hint = ComposerHint.TransformToVisual(_root).TransformPoint(new Windows.Foundation.Point());
                var editor = Composer.TransformToVisual(_root).TransformPoint(new Windows.Foundation.Point());
                if (ComposerHint.Visibility != Visibility.Visible || hint.Y + ComposerHint.ActualHeight > editor.Y - 15 ||
                    ComposerHint.Text != ComposerDestinations.Placeholder(destination))
                    throw new InvalidOperationException("The mode hint must sit above the editor with a separate gap.");
            }
            Composer.Text = draft;
            await Task.Delay(80);
            if (ComposerHint.Visibility != Visibility.Collapsed) throw new InvalidOperationException("The mode hint must hide while typing.");
            File.AppendAllText(logPath, "PASS: all mode hints sit above the cursor area, separated from the editor, and hide while typing\n");

            SelectDestination(ComposerDestination.Google);
            Uri? openedUri = null;
            _launchBrowser = uri => { openedUri = uri; return Task.FromResult(true); };
            SendCurrentMessage();
            if (Composer.Text != "" || openedUri != ComposerDestinations.SearchUri(ComposerDestination.Google, draft) ||
                Conversation.Children.Count != 0 || _bridge!.HasMessageInFlight)
                throw new InvalidOperationException("Search must clear the editor without using chat.");
            Composer.Text = draft;
            _launchBrowser = _ => Task.FromResult(false);
            SendCurrentMessage();
            if (Composer.Text != draft || !SendButton.IsEnabled)
                throw new InvalidOperationException("Failed browser launch must preserve the draft and allow retry.");
            _launchBrowser = _ => Task.FromException<bool>(new InvalidOperationException("Synthetic launch failure"));
            SendCurrentMessage();
            if (Composer.Text != draft || !SendButton.IsEnabled)
                throw new InvalidOperationException("Browser exceptions must preserve the draft.");
            var pending = new TaskCompletionSource<bool>();
            _launchBrowser = _ => pending.Task;
            var launch = LaunchDestinationAsync(ComposerDestination.Google, draft);
            Composer.Text = "New draft";
            SelectDestination(ComposerDestination.YouTube);
            pending.SetResult(true);
            await launch;
            if (Composer.Text != "New draft") throw new InvalidOperationException("New input was erased by a pending search.");
            File.AppendAllText(logPath, "PASS: search routing, success clearing, failure recovery and new drafts during browser launch\n");

            const string fullText = "  Note ☕\r\nwith line breaks\n  ";
            Composer.Text = fullText;
            // WinUI normalizes line endings when assigning TextBox.Text.
            var editorText = Composer.Text;
            SelectDestination(ComposerDestination.Notepad);
            string? openedNote = null;
            _launchNotepad = text => { openedNote = text; return Task.FromResult(true); };
            SendCurrentMessage();
            if (openedNote != editorText || Composer.Text != "" || Conversation.Children.Count != 0 || _bridge!.HasMessageInFlight)
                throw new InvalidOperationException("Notepad routing must preserve exact content and avoid chat.");
            Composer.Text = fullText;
            _launchNotepad = _ => Task.FromResult(false);
            SendCurrentMessage();
            if (Composer.Text != editorText) throw new InvalidOperationException("Notepad failures must preserve the draft.");
            SelectDestination(ComposerDestination.Translate);
            _launchBrowser = uri => { openedUri = uri; return Task.FromResult(true); };
            SendCurrentMessage();
            if (Composer.Text != "" || openedUri != ComposerDestinations.SearchUri(ComposerDestination.Translate, editorText))
                throw new InvalidOperationException("Translate must transfer exact content to the browser.");
            File.AppendAllText(logPath, "PASS: Notepad and Translate transfer exact text, clear on success and preserve drafts on failure\n");

            Composer.Text = fullText;
            SelectDestination(ComposerDestination.Codex);
            string? codexText = null;
            _launchCodex = text => { codexText = text; return Task.FromResult(true); };
            SendCurrentMessage();
            if (codexText != editorText || Composer.Text != "" || _bridge!.HasMessageInFlight)
                throw new InvalidOperationException("Codex handoff must transfer the full instruction without sending through OpenAI.");
            Composer.Text = fullText;
            _launchCodex = _ => Task.FromResult(false);
            SendCurrentMessage();
            if (Composer.Text != editorText) throw new InvalidOperationException("Failed Codex opening must preserve the instruction.");
            File.AppendAllText(logPath, "PASS: Codex handoff clears on success and preserves the draft on failure\n");

            foreach (var destination in new[] { ComposerDestination.Gmail, ComposerDestination.GoogleMaps })
            {
                Composer.Text = fullText;
                SelectDestination(destination);
                SendCurrentMessage();
                if (Composer.Text != "" || openedUri != ComposerDestinations.SearchUri(destination, editorText, _gmailAuth?.Email) ||
                    Conversation.Children.Count != 0 || _bridge!.HasMessageInFlight)
                    throw new InvalidOperationException($"{destination} must open the correct URI without using chat.");
            }
            File.AppendAllText(logPath, "PASS: Gmail draft and Google Maps routing preserve the full encoded input\n");

            SelectDestination(ComposerDestination.Terminal);
            Composer.Text = "Write-Output 'Synthetic confirmation test'";
            var command = Composer.Text;
            var runs = 0;
            _launchTerminal = text => { if (text != command) throw new InvalidOperationException("Command changed."); runs++; return true; };
            _confirmTerminal = _ => Task.FromResult(false);
            SendCurrentMessage();
            if (runs != 0 || Composer.Text != command) throw new InvalidOperationException("Cancel must not execute or clear a command.");
            var confirmation = new TaskCompletionSource<bool>();
            _confirmTerminal = text => { if (text != command) throw new InvalidOperationException("Confirmation text changed."); return confirmation.Task; };
            var commandLaunch = LaunchDestinationAsync(ComposerDestination.Terminal, command);
            SendCurrentMessage();
            if (runs != 0 || SendButton.IsEnabled) throw new InvalidOperationException("Commands must wait for confirmation and reject duplicate Enter.");
            confirmation.SetResult(true);
            await commandLaunch;
            if (runs != 1 || Composer.Text != "") throw new InvalidOperationException("Confirmed command must run once and clear on success.");
            Composer.Text = command;
            _confirmTerminal = _ => Task.FromResult(true);
            _launchTerminal = _ => false;
            SendCurrentMessage();
            if (Composer.Text != command) throw new InvalidOperationException("Failed command launch must preserve the draft.");
            File.AppendAllText(logPath, "PASS: Terminal cancel, confirmation gating, duplicate prevention and launch failure recovery\n");
        }
        finally
        {
            _launchBrowser = launcher;
            _launchNotepad = notepadLauncher;
            _launchCodex = codexLauncher;
            _launchTerminal = terminalLauncher;
            _confirmTerminal = terminalConfirmation;
            _preferences = saved;
            SelectDestination(saved.Destination);
            Composer.Text = "";
        }
    }
}
#endif
