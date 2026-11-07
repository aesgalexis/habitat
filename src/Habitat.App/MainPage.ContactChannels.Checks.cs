#if HABITAT_UI_CHECKS
using Habitat.Features.Contacts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckContactChannelsUiAsync(string log)
    {
        await _services.Contacts.LoadAsync();
        var original = _services.Contacts.Book;
        var destination = _destination;
        var draft = Composer.Text;
        var launch = _launchBrowser;
        var previousSections = _destinationSections.Keys.ToHashSet();
        try
        {
            var launches = 0;
            _launchBrowser = _ => { launches++; return Task.FromResult(true); };
            var google = new ContactSource("channel-google", "google", "Private source label", "sender@example.com", true);
            var facebook = new ContactSource("channel-facebook", "facebook", "Another private source", "synthetic", true);
            var other = new ContactSource("channel-other", "google", "Other account", "other@example.com", true);
            var book = ContactBook.Empty.Import(google, [new("person", [new("name", "Multi-channel friend ☕"),
                new("email", "one@example.com"), new("email", "two@example.com"),
                new("phone", "+34600111222"), new("phone", "+34600333444"),
                new("note", "Irrelevant private note"), new("other", "Internal ID")], "Original source JSON")])
                .Import(facebook, [new("friend", [new("name", "Facebook friend"),
                    new("messenger", "https://www.facebook.com/channel.friend")], "private Facebook record")]).WithSource(other);
            book = book.Merge(book.People[0].Id, book.People[1].Id);
            await _services.Contacts.ChangeAsync(_ => book);
            var person = _services.Contacts.Book.People.Single();
            SetFacebookOpen(true); await WaitForUpdateUiAsync(() => !_facebookBusy);
            await InvokeAsync((Button)FacebookAvatars.Children.Single());
            if (!IsPhoneContactDestination(_destination) || ContactProfile.IsOpen || _facebookPanelOpen ||
                ContactMessageName.Text != "Multi-channel friend ☕" || ContactChannelButtons.Children.Count != 4 || launches != 0)
                throw new InvalidOperationException("Clicking a multi-channel contact must select its destination directly, with a default channel and no profile or browser.");
            var pills = ContactChannelButtons.Children.OfType<ToggleButton>().ToArray();
            var sms = pills.Single(b => b.Tag as string == "sms");
            var phone = pills.Single(b => b.Tag as string == "phone");
            if (!pills.Select(b => b.Content as string).SequenceEqual(["SMS", "iMessage", "Email", "Facebook"]) ||
                pills.Any(b => b.CornerRadius.TopLeft != b.Height / 2) ||
                !phone.IsEnabled || sms.IsEnabled || sms.IsChecked == true ||
                AutomationProperties.GetItemStatus(sms) != "Disabled" || AutomationProperties.GetName(sms) != "SMS (disabled)")
                throw new InvalidOperationException("Contact channels must use matching text pills, with SMS visibly disabled.");
            CheckSelection("phone");
            await Choose("phone");
            CheckSelection("phone");
            var phoneDestination = _destination;
            Composer.Text = "Phone draft kept separately";
            CheckSelection("phone"); CheckPickerLayout();
            CheckHeaderPresence(true);
            await Choose("email");
            if (!IsEmailContactDestination(_destination) || ContactProfile.IsOpen || launches != 0 ||
                _emailRecipients[_destination].Address != "one@example.com" || _emailRecipients[_destination].Sender != "sender@example.com")
                throw new InvalidOperationException("The Email pill must select an email destination and default recipient/sender without browser work.");
            CheckSelection("email"); CheckPickerLayout();
            CheckHeaderPresence(true);
            var combos = ContactChannelPicker.Children.OfType<ComboBox>().ToArray();
            if (combos.Length != 2) throw new InvalidOperationException("Multiple recipient addresses and sender accounts must remain selectable below the channel pills.");
            combos.Single(c => AutomationProperties.GetName(c) == "Recipient address").SelectedIndex = 1;
            await Task.Delay(100);
            if (_emailRecipients[_destination].Address != "two@example.com") throw new InvalidOperationException("Choosing another address must update the fixed recipient.");
            ContactChannelPicker.Children.OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Gmail sender account").SelectedIndex = 1;
            await Task.Delay(100);
            if (_emailRecipients[_destination].Sender != "other@example.com") throw new InvalidOperationException("Changing sender must select its independent email draft.");
            Composer.Text = "Email draft kept separately";
            await Choose("messenger"); CheckSelection("messenger"); CheckPickerLayout();
            CheckHeaderPresence(true);
            if (!IsFacebookDestination(_destination) || ContactMessageName.Text != "Multi-channel friend ☕" || launches != 0)
                throw new InvalidOperationException("The Facebook pill must retain the person as destination without sending or launching.");
            SetFacebookOpen(true); await WaitForUpdateUiAsync(() => !_facebookBusy);
            var card = (Button)FacebookAvatars.Children.Single();
            await InvokeAsync(card);
            if (!IsFacebookDestination(_destination)) throw new InvalidOperationException("Reopening a contact must remember the selected channel.");
            SetFacebookOpen(true); await WaitForUpdateUiAsync(() => !_facebookBusy);
            await WaitForUpdateUiAsync(() => FacebookAvatars.Children.OfType<Button>().Any(c => c.XamlRoot is not null));
            card = (Button)FacebookAvatars.Children.Single();
            ShowContactProfile(person.Id, card);
            await Task.Delay(80);
            var profile = (ScrollViewer)ContactProfile.Content;
            var details = Descendants(profile).OfType<TextBlock>().Select(t => t.Text).ToArray();
            var actions = ((StackPanel)profile.Content).Children.OfType<Button>().Select(button => button.Content as string).ToArray();
            if (!ContactProfile.IsOpen || !details.Contains("one@example.com") || !details.Contains("+34600111222") ||
                details.Any(t => t is "Original source JSON" or "Private source label" or "Internal ID" or "Irrelevant private note") ||
                Descendants(profile).OfType<TextBox>().Any() ||
                !actions.SequenceEqual(["View conversation · +34600111222", "View conversation · +34600333444", "Manage contact"]))
                throw new InvalidOperationException("The right-click profile must show relevant details, one conversation action per number and Manage contact.");
            ContactProfile.Hide();
            SelectDestination(phoneDestination);
            if (Composer.Text != "Phone draft kept separately") throw new InvalidOperationException("Channel switches must preserve each channel's existing draft.");
            CheckHeaderPresence(true);
            await _services.Contacts.ChangeAsync(b => b.UndoMerge(person.Id));
            CheckHeaderPresence(false);
            var facebookPerson = _services.Contacts.Book.People.Single(p => _services.Contacts.Book.For(p).Any(r => r.SourceId == facebook.Id));
            await _services.Contacts.ChangeAsync(b => b.Merge(person.Id, facebookPerson.Id));
            CheckHeaderPresence(true);
            SelectDestination(destination);
            if (ContactChannelPicker.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Regular destinations must hide the contact channel picker.");
            var local = new ContactSource("channels-local", "local", "HABITAT", "local", true);
            await _services.Contacts.ChangeAsync(b => b.Import(local, [
                new("name-only", [new("name", "No address friend")], "name-only"),
                new("phone-only", [new("name", "Phone only"), new("phone", "+34600555666")], "phone-only"),
                new("email-only", [new("name", "Email only"), new("email", "only@example.com")], "email-only")]));
            foreach (var name in new[] { "Phone only", "Email only" })
            {
                SetFacebookOpen(true); await WaitForUpdateUiAsync(() => !_facebookBusy);
                await InvokeAsync((Button)FacebookAvatars.Children.Single(c => AutomationProperties.GetName(c) == name));
                CheckHeaderPresence(false);
                PaintContactMessageHeader();
                CheckHeaderPresence(false);
            }
            var noAddress = _services.Contacts.Book.People.Single(p => _services.Contacts.Book.Name(p) == "No address friend");
            SetFacebookOpen(true); await WaitForUpdateUiAsync(() => !_facebookBusy);
            await InvokeAsync((Button)FacebookAvatars.Children.Single(c => AutomationProperties.GetName(c) == "No address friend"));
            if (ContactMessageName.Text != "No address friend" || !IsContactDestination(_destination) || SendButton.IsEnabled || ContactProfile.IsOpen)
                throw new InvalidOperationException("A contact without an address must still become the destination with Send disabled and no popup.");
            CheckHeaderPresence(false);
            File.AppendAllText(log, "PASS: header presence only for Facebook and merged Facebook people across phone/email/Messenger; phone-only, email-only and no-channel contacts hide it; merge/undo updates the existing header.\n");
            File.AppendAllText(log, "PASS: multi-channel contact selects destination directly; text pills below the action with one selected channel; address/sender selection, last-channel preference, independent drafts, minimal secondary profile and unavailable-channel handling. No real messages, provider reads or browser navigation.\n");
        }
        finally
        {
            ContactProfile.Hide(); SetFacebookOpen(false); _launchBrowser = launch;
            await _services.Contacts.ChangeAsync(_ => original);
            SelectDestination(destination); Composer.Text = draft;
            foreach (var id in _destinationSections.Keys.Where(id => !previousSections.Contains(id)).ToArray())
            {
                _composerDrafts.Edit(id, "");
                await _contactDestinationViews.RetireAsync(id);
            }
        }

        async Task Choose(string kind)
        {
            var button = ContactChannelButtons.Children.OfType<ToggleButton>().Single(b => b.Tag as string == kind);
            ((IToggleProvider)new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle)).Toggle();
            await Task.Delay(100);
        }
        void CheckSelection(string kind)
        {
            if (ContactChannelButtons.Children.OfType<ToggleButton>().Count(b => b.IsChecked == true) != 1 ||
                ContactChannelButtons.Children.OfType<ToggleButton>().Single(b => b.IsChecked == true).Tag as string != kind)
                throw new InvalidOperationException("Exactly one available contact channel must be selected.");
        }
        void CheckHeaderPresence(bool expected)
        {
            var avatar = ContactMessageHeader.Children.OfType<Grid>().Single();
            if (avatar.Children[2].Visibility != (expected ? Visibility.Visible : Visibility.Collapsed))
                throw new InvalidOperationException("The contact header must show presence only for Facebook contacts or people merged with Facebook, independently of the selected channel.");
        }
        void CheckPickerLayout()
        {
            UpdateWritingLayout(); ((Grid)Content).UpdateLayout();
            var picker = ContactChannelPicker.TransformToVisual(_root).TransformPoint(new());
            var send = SendButton.TransformToVisual(_root).TransformPoint(new(0, SendButton.ActualHeight));
            if (picker.Y < send.Y + 1 || picker.Y + ContactChannelPicker.ActualHeight > _root.ActualHeight + 1 ||
                Composer.TextAlignment != TextAlignment.Center || _destinationContent.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Channel controls must sit below the action while preserving the centered initial editor.");
        }
    }
}
#endif
