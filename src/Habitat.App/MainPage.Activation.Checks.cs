#if HABITAT_UI_CHECKS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Habitat.Services;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckActivationUiAsync(string log)
    {
        var savedDestination = _destination;
        var savedText = Composer.Text;
        var savedDrafts = ComposerDestinations.Ordered.ToDictionary(destination => destination,
            destination => _composerDrafts.Text(destination));
        var prompts = 0;
        try
        {
            foreach (var destination in ComposerDestinations.Ordered)
            {
                SelectDestination(destination);
                Composer.Text = "";
                var editorAlignment = Composer.VerticalAlignment;
                var actionAlignment = SendButton.VerticalAlignment;
                var contentBefore = HasDestinationSection;
                var pending = _bridge?.HasMessageInFlight;
                await ReceiveActivationAsync("selected ñ\r\nliteral; do not execute");
                await Task.Delay(40);
                if (_destination != destination || !SameCapturedText(Composer.Text, "selected ñ\r\nliteral; do not execute") ||
                    _bridge?.HasMessageInFlight != pending || _composer.IsExclusiveBusy ||
                    Composer.VerticalAlignment != editorAlignment || SendButton.VerticalAlignment != actionAlignment ||
                    HasDestinationSection != contentBefore ||
                    !ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Composer))
                    throw new Exception($"Activation did not preserve destination, literal text and focus: {destination}; text={System.Text.Json.JsonSerializer.Serialize(Composer.Text)}, focus={FocusManager.GetFocusedElement(XamlRoot)?.GetType().Name}, pending={_bridge?.HasMessageInFlight}, exclusive={_composer.IsExclusiveBusy}");
                _confirmCapturedText = _ => { prompts++; return Task.FromResult(false); };
                await ReceiveActivationAsync("replacement");
                if (!SameCapturedText(Composer.Text, "selected ñ\r\nliteral; do not execute")) throw new Exception("Declining replaced a draft");
                _confirmCapturedText = _ => { prompts++; return Task.FromResult(true); };
                await ReceiveActivationAsync("replacement");
                if (Composer.Text != "replacement") throw new Exception("Confirmed selection was not inserted");
                var before = prompts;
                await ReceiveActivationAsync(null);
                await ReceiveActivationAsync("replacement");
                if (Composer.Text != "replacement" || prompts != before) throw new Exception("Empty/same activation disturbed draft");
                _confirmCapturedText = _ => { Composer.Text = "newer draft"; return Task.FromResult(true); };
                await ReceiveActivationAsync("stale replacement");
                if (Composer.Text != "newer draft") throw new Exception("A changed draft was overwritten");
            }
            SelectDestination(ComposerDestination.OpenAI);
            Composer.Text = "keep this draft";
            _confirmCapturedText = null;
            var confirming = ReceiveActivationAsync("selected replacement");
            await Task.Delay(80);
            if (_activeDialog is not { Title: "Replace draft?", PrimaryButtonText: "Replace draft", CloseButtonText: "Keep draft",
                DefaultButton: Microsoft.UI.Xaml.Controls.ContentDialogButton.Close })
                throw new Exception("The native confirmation must default to keeping the draft");
            _activeDialog.Hide();
            await confirming;
            await Task.Delay(40);
            if (Composer.Text != "keep this draft" || !ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Composer))
                throw new Exception("Cancelling the confirmation did not keep the draft and return focus");
            File.AppendAllText(log, "PASS: Ctrl+H insertion, focus, all destinations, no submission, keep/replace and concurrent drafts\n");
        }
        finally
        {
            _confirmCapturedText = null;
            foreach (var draft in savedDrafts) _composerDrafts.Edit(draft.Key, draft.Value);
            SelectDestination(savedDestination);
            Composer.Text = savedText;
        }
    }
}
#endif
