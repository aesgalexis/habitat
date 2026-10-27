#if HABITAT_UI_CHECKS
using Habitat.Features.Contacts;
using Habitat.Features.Preferences;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckContactsSettingsUiAsync(string log)
    {
        var service = _services.Facebook;
        var photo = _services.FacebookPhoto;
        var launch = _launchBrowser;
        var draft = Composer.Text;
        var destination = _destination;
        Task? showing = null;
        await _services.Contacts.LoadAsync();
        var original = _services.Contacts.Book;
        var fake = new CheckedFacebook();
        try
        {
            _services.Facebook = fake; _services.FacebookPhoto = _ => null;
            _launchBrowser = _ => throw new InvalidOperationException("Unexpected browser launch in contact checks.");
            Composer.Text = "Synthetic draft · conservar ☕";
            var keptDraft = Composer.Text;
            SetFacebookOpen(true);
            await WaitForUpdateUiAsync(() => !_facebookBusy);
            if (fake.Reads != 0 || FacebookAvatars.Children.Count != 0 || FacebookRecovery.IsOpen ||
                FacebookNoMatches.Visibility != Visibility.Visible || !FacebookNoMatches.Text.Contains("Manage contacts") ||
                Composer.Text != keptDraft || _destination != destination)
                throw new InvalidOperationException("Fresh Contacts must stay empty, offer settings and preserve the draft without reading Facebook.");
            showing = ShowPreferencesAsync("Contacts");
            await WaitForUpdateUiAsync(() => _activeDialog?.Content is PreferencesView);
            var preferences = (PreferencesView)_activeDialog!.Content;
            var view = preferences.Contacts ?? throw new InvalidOperationException("Contacts settings are missing.");
            if (preferences.Category != "Contacts" || fake.Reads != 0 || _services.Contacts.Book.Sources.Length != 0)
                throw new InvalidOperationException("Opening settings must not connect or import any source.");
            var source = new ContactSource(ContactBook.NewId(), "vcard", "Synthetic phone", "Synthetic phone", false);
            ContactCandidate Candidate(string id, string name, string email) => new(id, [new("name", name), new("email", email)], id);
            var contacts = new[] { Candidate("one", "Amiga José ☕", "amiga@example.com"), Candidate("two", "Friend 雪", "friend@example.com") };
            view.BeginPreview(new(source, contacts)); view.Refresh();
            await Task.Delay(60);
            if (_services.Contacts.Book.People.Length != 0 || view.Preview is null)
                throw new InvalidOperationException("A preview must not save contacts before confirmation.");
            var selection = Descendants(view).OfType<ListView>().Single();
            selection.SelectedItems.Clear(); selection.SelectedItems.Add(selection.Items[0]);
            await InvokeAsync(FindButton(view, "Import selected contacts"));
            await WaitForUpdateUiAsync(() => !view.IsBusy);
            if (_services.Contacts.Book.People.Length != 1 || view.Preview is not null)
                throw new InvalidOperationException("Import must save only the selected records.");
            view.BeginPreview(new(source, contacts)); view.Refresh(); await Task.Delay(60);
            await InvokeAsync(FindButton(view, "Import selected contacts")); await WaitForUpdateUiAsync(() => !view.IsBusy);
            if (_services.Contacts.Book.People.Length != 2)
                throw new InvalidOperationException("Importing again must update existing records without duplicates.");
            File.AppendAllText(log, "PASS: selected contact import and repeat import\n");
            var google = new ContactSource(ContactBook.NewId(), "google", "Google · synthetic@example.com", "synthetic@example.com", false);
            await _services.Contacts.ChangeAsync(b => b.Import(google, [Candidate("people/amiga", "Amiga from Google", "amiga@example.com")]));
            view.SelectDuplicates(); await Task.Delay(60);
            File.AppendAllText(log, "CHECK: duplicate suggestions displayed\n");
            await InvokeAsync(FindButton(view, "Review suggestion"));
            File.AppendAllText(log, "CHECK: comparison displayed\n");
            if (!Descendants(view).OfType<TextBlock>().Any(t => t.Text == "Amiga José ☕") ||
                !Descendants(view).OfType<TextBlock>().Any(t => t.Text == "Amiga from Google"))
                throw new InvalidOperationException("Duplicate review must show both original people before merging.");
            await InvokeAsync(FindButton(view, "Confirm merge")); await WaitForUpdateUiAsync(() => !view.IsBusy);
            if (_services.Contacts.Book.People.Length != 2 || _services.Contacts.Book.Records.Length != 3)
                throw new InvalidOperationException("Confirm merge must group people without deleting records.");
            var merged = _services.Contacts.Book.People.Single(p => p.RecordIds.Length == 2);
            view.SelectPerson(merged.Id); await Task.Delay(60);
            await InvokeAsync(FindButton(view, "Undo last merge")); await WaitForUpdateUiAsync(() => !view.IsBusy);
            if (_services.Contacts.Book.People.Length != 3) throw new InvalidOperationException("Undo must restore the original groupings.");
            view.SelectDuplicates(); await Task.Delay(60);
            await InvokeAsync(FindButton(view, "Review suggestion"));
            await InvokeAsync(FindButton(view, "Keep separate")); await WaitForUpdateUiAsync(() => !view.IsBusy);
            if (_services.Contacts.Book.Suggestions().Length != 0) throw new InvalidOperationException("Keep separate must persist the rejection.");

            // Manual merge must work even when the records have no matching key.
            var first = _services.Contacts.Book.People.First();
            view.SelectPerson(first.Id); await Task.Delay(60);
            var mergeChoice = Descendants(view).OfType<ComboBox>().Single(c => c.Header as string == "Merge with");
            mergeChoice.SelectedIndex = 0;
            await InvokeAsync(FindButton(view, "Review merge"));
            await InvokeAsync(FindButton(view, "Confirm merge")); await WaitForUpdateUiAsync(() => !view.IsBusy);
            if (_services.Contacts.Book.People.Length != 2) throw new InvalidOperationException("Manual merge must require and honor explicit review.");
            view.SelectPerson(first.Id); await Task.Delay(60);
            await InvokeAsync(FindButton(view, "Undo last merge")); await WaitForUpdateUiAsync(() => !view.IsBusy);
            _activeDialog.Hide(); await showing; showing = null;
            if (fake.Reads != 0 || Composer.Text != keptDraft || _destination != destination)
                throw new InvalidOperationException("Settings must preserve the draft and destination without implicit provider reads.");
            FacebookContactSearch.Text = "  AMIGA@EXAMPLE.COM  ";
            await WaitForUpdateUiAsync(() => FacebookAvatars.Children.Count == 2);
            if (FacebookAvatars.Children.Count != 2) throw new InvalidOperationException("Contact search must include email addresses.");
            FacebookContactSearch.Text = "Jose";
            await WaitForUpdateUiAsync(() => FacebookAvatars.Children.Count == 1);
            if (FacebookAvatars.Children.Count != 1) throw new InvalidOperationException("Search must ignore accents without translating names.");
            Uri? openedDraft = null;
            _launchBrowser = uri => { openedDraft = uri; return Task.FromResult(true); };
            var contactCard = (Button)FacebookAvatars.Children[0];
            await InvokeAsync(contactCard);
            if (ContactProfile.IsOpen || Composer.Text != "" || !IsEmailContactDestination(_destination) ||
                openedDraft is not null || ComposerHint.Text != "Email Amiga José ☕…" || SendButton.Content as string != "Draft email")
                throw new InvalidOperationException("A single email channel must select its own HABITAT editor without opening Gmail or copying another destination's draft.");
            SelectDestination(destination);
            if (Composer.Text != keptDraft) throw new InvalidOperationException("The previous destination's draft must remain intact after selecting an email contact.");
            SetFacebookOpen(true); await WaitForUpdateUiAsync(() => !_facebookBusy);
            FacebookContactSearch.Text = "Jose";
            await WaitForUpdateUiAsync(() => FacebookAvatars.Children.Count == 1);
            contactCard = (Button)FacebookAvatars.Children[0];
            if (contactCard.ContextFlyout is not MenuFlyout menu || menu.Items.OfType<MenuFlyoutItem>().Single().Text != "Contact details")
                throw new InvalidOperationException("Contact details must remain available as an explicit secondary action.");
            ShowContactProfile((string)contactCard.Tag, contactCard);
            if (!ContactProfile.IsOpen) throw new InvalidOperationException("The explicit details action must open the contact profile.");
            var profile = (ScrollViewer)ContactProfile.Content;
            await WaitForUpdateUiAsync(() => Descendants(profile).OfType<Button>().Any());
            if (!Descendants(profile).OfType<Button>().Any(b => (b.Content as string)?.StartsWith("Write email", StringComparison.Ordinal) == true) ||
                Descendants(profile).OfType<Button>().Any(b => (b.Content as string)?.Contains("iMessage", StringComparison.Ordinal) == true))
                throw new InvalidOperationException("Profiles must expose supported channels without inventing iMessage availability.");
            ContactProfile.Hide();
            SetFacebookOpen(true); await WaitForUpdateUiAsync(() => !_facebookBusy);
            _launchBrowser = _ => throw new InvalidOperationException("Unexpected browser launch in contact checks.");
            var extra = Enumerable.Range(0, 150).Select(i => Candidate("extra-" + i, "Paged person " + i.ToString("D3"), "page" + i + "@example.com")).ToArray();
            await _services.Contacts.ChangeAsync(b => b.Import(source, extra));
            FacebookContactSearch.Text = "";
            await WaitForUpdateUiAsync(() => FacebookAvatars.Children.Count == 100);
            if (ContactsLoadMore.Visibility != Visibility.Visible) throw new InvalidOperationException("Long agendas must offer bounded pages.");
            await InvokeAsync(ContactsLoadMore);
            await WaitForUpdateUiAsync(() => FacebookAvatars.Children.Count == 153);
            if (FacebookAvatars.Children.Count != 153) throw new InvalidOperationException($"Load more must reveal remaining contacts. Cards={FacebookAvatars.Children.Count}, people={_services.Contacts.Book.ListedPeople.Count()}, limit={_contactVisibleLimit}, open={_facebookPanelOpen}.");
            FacebookContactSearch.Text = "page149@example.com";
            await WaitForUpdateUiAsync(() => FacebookAvatars.Children.Count == 1);
            if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(FacebookAvatars.Children[0]) != "Paged person 149")
                throw new InvalidOperationException("Search must find contacts beyond the first page.");

            showing = ShowPreferencesAsync("Contacts");
            await WaitForUpdateUiAsync(() => _activeDialog?.Content is PreferencesView);
            view = ((PreferencesView)_activeDialog!.Content).Contacts!;
            await WaitForUpdateUiAsync(() => Descendants(view).OfType<Button>().Any(b => b.Content as string == "Other contact options"));
            await InvokeAsync(FindButton(view, "Other contact options"));
            await WaitForUpdateUiAsync(() => Descendants(view).OfType<TextBox>().Any(t => t.Header as string == "Facebook account name"));
            var account = Descendants(view).OfType<TextBox>().Single(t => t.Header as string == "Facebook account name");
            account.Text = "Synthetic cancelled source";
            await Task.Delay(40);
            fake.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await InvokeAsync(FindButton(view, "Preview Facebook contacts"));
            if (!view.IsBusy || fake.Reads != 1) throw new InvalidOperationException($"An explicit Facebook preview must use a cancellable source read. Busy={view.IsBusy}, reads={fake.Reads}.");
            _activeDialog.Hide(); await showing; showing = null;
            if (!fake.Canceled || _services.Contacts.Book.Sources.Any(s => s.Account == "Synthetic cancelled source"))
                throw new InvalidOperationException("Closing settings must cancel its pending import without adding a source or contacts.");
            File.AppendAllText(log, "PASS: Contacts settings starts empty; selected import, idempotent reload, provenance, side-by-side duplicate review, manual merge, undo, remembered rejection, email channels, original names, full-book search, paging and source cancellation. No real provider reads or messages.\n");
        }
        finally
        {
            _activeDialog?.Hide(); if (showing is not null) await showing;
            ContactProfile.Hide(); SetFacebookOpen(false);
            await _services.Contacts.ChangeAsync(_ => original);
            _services.Facebook = service; _services.FacebookPhoto = photo; _launchBrowser = launch;
            SelectDestination(destination); Composer.Text = draft;
        }
    }
}
#endif
