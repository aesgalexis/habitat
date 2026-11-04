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
        await _services.Contacts.LoadAsync();
        var original = _services.Contacts.Book;
        var service = _services.Facebook;
        var launch = _launchBrowser;
        var destination = _destination;
        var draft = Composer.Text;
        var previousSections = _destinationSections.Keys.ToHashSet();
        Task? showing = null;
        var fake = new CheckedFacebook();
        try
        {
            _services.Facebook = fake;
            _launchBrowser = _ => throw new InvalidOperationException("Manual contacts must not launch a browser.");
            await _services.Contacts.ChangeAsync(_ => ContactBook.Empty);
            Composer.Text = "Unrelated draft stays here ☕";
            var kept = Composer.Text;
            SetFacebookOpen(true); await WaitForUpdateUiAsync(() => !_facebookBusy);
            if (FacebookAvatars.Children.Count != 0 || !FacebookNoMatches.Text.Contains("Add contact") || fake.Reads != 0)
                throw new InvalidOperationException("Contacts must start empty and offer manual creation without reading providers.");
            await InvokeAsync(ContactsAdd);
            await WaitForUpdateUiAsync(() => _activeDialog?.Content is PreferencesView);
            var view = ((PreferencesView)_activeDialog!.Content).Contacts!;
            await WaitForUpdateUiAsync(() => Descendants(view).OfType<TextBox>().Any(t => t.Header as string == "Name"));
            TextBox Field(string header) => Descendants(view).OfType<TextBox>().Single(t => t.Header as string == header);
            Field("Name").Text = "Amiga José ☕";
            Field("Email").Text = "amiga@example.com";
            await InvokeAsync(FindButton(view, "Add detail"));
            await WaitForUpdateUiAsync(() => Descendants(view).OfType<TextBox>().Any(t => t.Header as string == "Notes"));
            if (Field("Name").Text != "Amiga José ☕" || Field("Email").Text != "amiga@example.com")
                throw new InvalidOperationException("Adding another detail must preserve the fields already entered.");
            Field("Notes").Text = "Dato original 雪";
            Field("Label (optional)").Text = "Personal";
            await InvokeAsync(FindButton(view, "Add contact"));
            await WaitForUpdateUiAsync(() => !view.IsBusy);
            if (_services.Contacts.Book.People.Length != 1)
                throw new InvalidOperationException("Manual creation failed: " + string.Join(" | ", Descendants(view).OfType<TextBlock>().Select(t => t.Text)) + " Session: " + _services.Contacts.Error);
            var personId = _services.Contacts.Book.People.Single().Id;
            var recordId = _services.Contacts.Book.Records.Single().Id;
            if (!_services.Contacts.Book.Records.Single().Current.Values.Contains(new("note", "Dato original 雪", "Personal")))
                throw new InvalidOperationException("Custom details and their labels must be saved with the contact.");
            await InvokeAsync(FindButton(view, "Add contact"));
            await WaitForUpdateUiAsync(() => Descendants(view).OfType<TextBox>().Any(t => t.Header as string == "Email"));
            Field("Name").Text = "Duplicate attempt";
            Field("Email").Text = "AMIGA@example.com";
            await InvokeAsync(FindButton(view, "Add contact"));
            await WaitForUpdateUiAsync(() => !view.IsBusy);
            if (_services.Contacts.Book.People.Length != 1 || _services.Contacts.Error is not null || Field("Name").Text != "Duplicate attempt" ||
                !Descendants(view).OfType<TextBlock>().Any(t => t.Text.Contains("already belong")))
                throw new InvalidOperationException("Duplicate addresses must be rejected without losing entered data or locking the address book.");
            await InvokeAsync(FindButton(view, "Cancel"));
            view.SelectPerson(personId); await Task.Delay(60);
            Field("Name").Text = "Amiga José editada ☕";
            Field("Phone").Text = "+34 600 111 222";
            await InvokeAsync(FindButton(view, "Save contact")); await WaitForUpdateUiAsync(() => !view.IsBusy);
            if (_services.Contacts.Book.People.Single().Id != personId || _services.Contacts.Book.Records.Single().Id != recordId ||
                ContactChannels.For(_services.Contacts.Book, _services.Contacts.Book.People.Single()).Length != 2)
                throw new InvalidOperationException("Editing must add channels to the same person rather than create another card.");
            _activeDialog.Hide(); await WaitForUpdateUiAsync(() => !_appearanceDialogOpen);
            if (Composer.Text != kept || _destination != destination || fake.Reads != 0)
                throw new InvalidOperationException("Adding and editing contacts must preserve the current editor without provider reads.");
            FacebookContactSearch.Text = "JOSE"; await WaitForUpdateUiAsync(() => FacebookAvatars.Children.Count == 1);
            await InvokeAsync((Button)FacebookAvatars.Children.Single());
            if (!IsPhoneContactDestination(_destination) || Composer.Text.Length != 0 || ContactMessageName.Text != "Amiga José editada ☕" ||
                ComposerHint.Text != "Message Amiga José editada ☕…" || SendButton.Content as string != "Send" ||
                _destinationSections[_destination].HasContent)
                throw new InvalidOperationException("A manual contact must open its own initial destination with a centered composer and no result content.");
            var contactDestination = _destination;
            Composer.Text = "Manual contact draft";
            showing = ShowPreferencesAsync("Contacts");
            await WaitForUpdateUiAsync(() => _activeDialog?.Content is PreferencesView);
            view = ((PreferencesView)_activeDialog!.Content).Contacts!;
            view.SelectPerson(personId); await Task.Delay(60);
            Field("Name").Text = "Renamed destination";
            await InvokeAsync(FindButton(view, "Save contact")); await WaitForUpdateUiAsync(() => !view.IsBusy);
            if (_destination != contactDestination || Composer.Text != "Manual contact draft" || ContactMessageName.Text != "Renamed destination")
                throw new InvalidOperationException("Renaming must update the selected destination while retaining its draft and identity.");
            view.SelectPerson(personId); await Task.Delay(60);
            await InvokeAsync(FindButton(view, "Remove contact"));
            if (_services.Contacts.Book.People.Length != 1) throw new InvalidOperationException("Removal must await its explicit confirmation.");
            await InvokeAsync(FindButton(view, "Keep contact"));
            await InvokeAsync(FindButton(view, "Remove contact"));
            await InvokeAsync(FindButton(view, "Confirm remove")); await WaitForUpdateUiAsync(() => !view.IsBusy);
            if (_services.Contacts.Book.People.Length != 0 || IsContactDestination(_destination))
                throw new InvalidOperationException("Removing the selected person must remove the card and its actionable destination.");
            _activeDialog.Hide(); await showing; showing = null;
            await _services.Contacts.LoadAsync();
            SetFacebookOpen(true); await WaitForUpdateUiAsync(() => !_facebookBusy);
            if (FacebookAvatars.Children.Count != 0 || fake.Reads != 0) throw new InvalidOperationException("The empty manual agenda must persist on reload.");
            File.AppendAllText(log, "PASS: Add contact shortcut; manual create/edit; duplicate rejection with fields preserved; one person and multiple channels; accent-insensitive search; initial destination layout; rename preserves identity/draft; removal confirmation and restart. No real providers or messages.\n");
        }
        finally
        {
            _activeDialog?.Hide(); if (showing is not null) await showing;
            SetFacebookOpen(false);
            await _services.Contacts.ChangeAsync(_ => original);
            _services.Facebook = service; _launchBrowser = launch;
            SelectDestination(destination); Composer.Text = draft;
            foreach (var id in _destinationSections.Keys.Where(id => !previousSections.Contains(id)).ToArray())
            {
                var section = _destinationSections[id]; await section.StopAsync(); section.Dispose(); _destinationSections.Remove(id);
                _emailRecipients.Remove(id); _phoneRecipients.Remove(id); _facebookRecipients.Remove(id); _facebookSendRecipients.Remove(id);
                _contactChannelSelections.Remove(id);
            }
        }
    }
}
#endif
