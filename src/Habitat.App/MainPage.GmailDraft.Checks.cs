#if HABITAT_UI_CHECKS
using Habitat.Features.Destinations;
using Habitat.Models;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckGmailDraftUiAsync(string logPath)
    {
        var launcher = _launchBrowser;
        try
        {
            var browserCalls = 0;
            Uri? opened = null;
            _launchBrowser = uri => { browserCalls++; opened = uri; return Task.FromResult(true); };
            SelectDestination(ComposerDestination.Gmail);
            Composer.Text = "";
            var section = (GmailDraftSection)_destinationSections[ComposerDestination.Gmail];
            CheckInitialWritingView();
            const string original = "Draft body with Unicode café ☕";
            Composer.Text = original;
            await LaunchDestinationAsync(ComposerDestination.Gmail, Composer.Text);
            if (browserCalls != 0 || Composer.Text != original || section.IsBusy ||
                !section.HasContent || _destinationContent.Visibility != Visibility.Visible ||
                !section.StatusText.Contains("Authorize") || _bridge!.HasMessageInFlight)
                throw new InvalidOperationException("Draft email must never open a browser or clear the editor when authorization is needed.");
            await OpenSelectedDestinationAsync();
            if (browserCalls != 1 || opened?.Fragment != "#drafts" || Composer.Text != original)
                throw new InvalidOperationException("External Gmail opens saved drafts without composing or clearing the editor.");
            section.RequestClear();
            if (Composer.Text != "") throw new InvalidOperationException("Clear must clear the Gmail editor.");
            CheckInitialWritingView();
            if (!ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot), Composer))
                throw new InvalidOperationException("Clear must return the cursor to the editor.");

            void CheckInitialWritingView()
            {
                if (section.HasContent || section.StatusText.Length != 0 || _destinationContent.Content is not null ||
                    _destinationContent.Visibility != Visibility.Collapsed || Composer.VerticalAlignment != VerticalAlignment.Top ||
                    Composer.TextAlignment != TextAlignment.Center || ComposerHint.Visibility != Visibility.Visible ||
                    ComposerHint.Text != "Draft a new email…" || SendButton.Content as string != "Draft email" ||
                    SendButton.HorizontalAlignment != HorizontalAlignment.Center)
                    throw new InvalidOperationException("Gmail must start and reset to the shared centered action hint, editor and action button without a section or explanatory text.");
            }

            var saves = 0;
            var pending = new TaskCompletionSource<GmailDraft>();
            string? savedText = null;
            using var isolated = new GmailDraftSection((text, _) => { saves++; savedText = text; return pending.Task; },
                () => Task.CompletedTask, () => "test@example.com", _launchBrowser,
                exception => throw new InvalidOperationException("Unexpected draft error", exception));
            var clearing = 0;
            isolated.ClearRequested += () => clearing++;
            var saving = isolated.SubmitAsync(original);
            await isolated.SubmitAsync(original);
            isolated.RequestClear();
            if (saves != 1 || !isolated.IsBusy || savedText != original || clearing != 0)
                throw new InvalidOperationException("Saving must gate duplicate submissions and Clear until the result is known.");
            var saved = new GmailDraft("draft123", "Draft body", "test@habitat.local");
            pending.SetResult(saved);
            await saving;
            if (isolated.IsBusy || !isolated.StatusText.Contains("Draft saved") || browserCalls != 1)
                throw new InvalidOperationException("A saved draft must confirm success without opening the browser.");
            await isolated.OpenBrowserAsync();
            if (opened != saved.BrowserUri("test@example.com") || saves != 1)
                throw new InvalidOperationException("Open in browser must open the existing draft without saving another one.");
            isolated.RequestClear();
            await isolated.OpenBrowserAsync();
            if (clearing != 1 || opened?.Fragment != "#drafts" || saves != 1 || isolated.HasContent || isolated.StatusText.Length != 0)
                throw new InvalidOperationException("Clear resets the local view while leaving saved Gmail drafts intact.");
            var buttons = ((StackPanel)isolated.View).Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToArray();
            if (buttons[0].Content as string != "Open in browser" || buttons[1].Content as string != "Clear" ||
                AutomationProperties.GetName(buttons[0]) != "Open in browser" || AutomationProperties.GetName(buttons[1]) != "Clear")
                throw new InvalidOperationException("Gmail action labels and accessibility names must be in English.");
            await isolated.StopAsync();
            File.AppendAllText(logPath, "PASS: Gmail starts and resets to the shared centered action hint/editor/action; saves without browser launches; explicit browser opening, Clear, duplicate gating and English accessibility labels\n");
        }
        finally { _launchBrowser = launcher; }
    }
}
#endif
