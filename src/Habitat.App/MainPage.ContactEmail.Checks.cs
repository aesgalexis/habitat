#if HABITAT_UI_CHECKS
using Habitat.Features.Contacts;
using Habitat.Features.Destinations;
using Habitat.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckContactEmailUiAsync(string log)
    {
        await _services.Contacts.LoadAsync();
        var original = _services.Contacts.Book;
        var destination = _destination;
        var draft = Composer.Text;
        var launcher = _launchBrowser;
        var save = _saveContactEmailDraft;
        var previousIds = _emailRecipients.Keys.ToHashSet();
        var browserCalls = 0; var saves = 0;
        string? to = null, from = null, body = null;
        var pending = new TaskCompletionSource<GmailDraft>();
        try
        {
            _launchBrowser = _ => { browserCalls++; return Task.FromResult(true); };
            _saveContactEmailDraft = (text, recipient, sender, _) =>
            { saves++; body = text; to = recipient; from = sender; return pending.Task; };
            var source = new ContactSource(ContactBook.NewId(), "google-browser", "Synthetic Google", "sender@example.com", true);
            ContactCandidate Candidate(string email, string name) => new(email, [new("name", name), new("email", email)], email);
            await _services.Contacts.ChangeAsync(b => b.Import(source, [Candidate("one@example.com", "Email friend ☕"),
                Candidate("two@example.com", "Second email friend"), Candidate("no-reply@example.com", "Automated sender")]));
            SetFacebookOpen(true); await WaitForUpdateUiAsync(() => !_facebookBusy);
            if (FacebookAvatars.Children.Count != 2 || _services.Contacts.Book.Records.Length != 3)
                throw new InvalidOperationException("Existing no-reply records must be hidden from the agenda while preserving their source data.");
            await InvokeAsync((Button)FacebookAvatars.Children.Single(c => AutomationProperties.GetName(c) == "Email friend ☕"));
            var first = _destination;
            CheckInitial();
            Composer.Text = "Body for first friend · café ☕\nSecond line";
            var firstText = Composer.Text;
            SetFacebookOpen(true); await WaitForUpdateUiAsync(() => !_facebookBusy);
            await InvokeAsync((Button)FacebookAvatars.Children.Single(c => AutomationProperties.GetName(c) == "Second email friend"));
            if (Composer.Text != "" || _destination == first || !IsEmailContactDestination(_destination))
                throw new InvalidOperationException("Each email recipient must have an independent editor without opening a browser.");
            Composer.Text = "Other recipient's draft";
            SelectDestination(first);
            if (Composer.Text != firstText) throw new InvalidOperationException("Returning to an email contact must restore its own draft.");
            var submitting = LaunchDestinationAsync(first, Composer.Text);
            await Task.Delay(40);
            await LaunchDestinationAsync(first, Composer.Text);
            if (saves != 1 || to != "one@example.com" || from != "sender@example.com" || body != firstText || browserCalls != 0)
                throw new InvalidOperationException("Draft email must target the selected recipient and sender once, with the exact text, without opening Gmail.");
            pending.SetResult(new("synthetic-draft", "Body for first friend", "contact-test@habitat.local"));
            await submitting;
            var section = (GmailDraftSection)_destinationSections[first];
            if (!section.HasContent || !section.StatusText.Contains("Draft saved") || Composer.Text != firstText || browserCalls != 0)
                throw new InvalidOperationException("Saving must show the result in HABITAT and preserve the editor without opening Gmail.");
            section.RequestClear(); CheckInitial();
            _saveContactEmailDraft = (_, _, _, _) => throw new GmailDraftAuthorizationException("Authorize Gmail drafts, then press Draft email again.");
            Composer.Text = "Keep this draft when authorization is missing";
            await LaunchDestinationAsync(first, Composer.Text);
            if (!section.StatusText.Contains("Authorize") || Composer.Text != "Keep this draft when authorization is missing" || browserCalls != 0)
                throw new InvalidOperationException("Missing draft authorization must preserve the editor and offer explicit authorization without opening Gmail automatically.");
            File.AppendAllText(log, "PASS: contact email stays in HABITAT; centered initial editor, fixed recipient/sender, isolated drafts, one save per submission, exact text, authorization recovery and no browser on selection/save; existing no-reply records hidden without data loss. All email operations intercepted.\n");

            void CheckInitial()
            {
                if (!IsEmailContactDestination(_destination) || _facebookPanelOpen || ContactProfile.IsOpen || Composer.Text != "" ||
                    ComposerHint.Text != "Email Email friend ☕…" || SendButton.Content as string != "Draft email" ||
                    _destinationSections[_destination].HasContent || _destinationContent.Visibility != Visibility.Collapsed ||
                    Composer.TextAlignment != TextAlignment.Center || ContactMessageName.Text != "Email friend ☕" ||
                    ContactMessageChannel.Text != "Gmail" || ContactMessageHeader.Visibility != Visibility.Visible || browserCalls != 0)
                    throw new InvalidOperationException("An email contact must start/reset to HABITAT's centered writing view, recipient header and Draft email action without launching a browser.");
            }
        }
        finally
        {
            _launchBrowser = launcher; _saveContactEmailDraft = save;
            SetFacebookOpen(false); SelectDestination(destination); Composer.Text = draft;
            await _services.Contacts.ChangeAsync(_ => original);
            foreach (var id in _emailRecipients.Keys.Where(id => !previousIds.Contains(id)).ToArray())
            {
                if (_destinationSections.Remove(id, out var section)) { await section.StopAsync(); section.Dispose(); }
                _emailRecipients.Remove(id);
            }
        }
    }
}
#endif
