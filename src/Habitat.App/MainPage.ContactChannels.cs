using Habitat.Features.Contacts;
using Habitat.Features.Facebook;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Habitat;

public sealed partial class MainPage
{
    private sealed record ContactChannelSelection(string PersonId, string Kind, string Address, string? Sender = null);
    private readonly Dictionary<string, ContactChannelSelection> _contactChannelSelections = new();
    private readonly Dictionary<string, ContactChannelSelection> _preferredContactChannels = new();
    private readonly Dictionary<string, FacebookContact> _unavailableContacts = new();
    private readonly StackPanel ContactChannelPicker = new() { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
    private readonly StackPanel ContactChannelButtons = new() { Orientation = Orientation.Horizontal, Spacing = 6,
        HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ScrollViewer ContactChannelButtonScroll = new()
    {
        IsTabStop = false, HorizontalAlignment = HorizontalAlignment.Center,
        HorizontalScrollMode = ScrollMode.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    private string SelectedContactChannelLabel => IsFacebookDestination(_destination) ? "Facebook" :
        IsPhoneContactDestination(_destination) ? "iMessage" : IsEmailContactDestination(_destination) ? "Email" : "";
    private double ContactChannelPickerHeight => ContactChannelPicker.Visibility == Visibility.Visible ? ContactChannelPicker.Height : 0;
    private bool ContactChannelsEnabled => !_operations.IsStopping && !_composer.IsExclusiveBusy &&
        !(_destinationSections.GetValueOrDefault(_destination)?.IsBusy ?? false);

    private void UpdateContactChannelAvailability()
    {
        foreach (var control in ContactChannelButtons.Children.OfType<Control>().Concat(ContactChannelPicker.Children.OfType<Control>()))
            control.IsEnabled = ContactChannelsEnabled && control.Tag as string != "sms";
    }

    private static bool SameContactAddress(string kind, string left, string right) => kind == "phone"
        ? ContactBook.NormalizePhone(left) == ContactBook.NormalizePhone(right) : kind == "messenger"
        ? (ContactBook.FacebookProfileIdentity(left) ?? left) == (ContactBook.FacebookProfileIdentity(right) ?? right)
        : ContactBook.NormalizeEmail(left) == ContactBook.NormalizeEmail(right);

    private void RefreshSavedContactDestinations()
    {
        var book = _services.Contacts.Book;
        var removedCurrent = false;
        foreach (var (id, selection) in _contactChannelSelections.ToArray())
        {
            var person = book.People.FirstOrDefault(p => p.Id == selection.PersonId);
            if (person is null || !ContactChannels.For(book, person).Any(c => c.Kind == selection.Kind && SameContactAddress(c.Kind, c.Address, selection.Address)))
            {
                removedCurrent |= _destination == id;
                _contactChannelSelections.Remove(id); _preferredContactChannels.Remove(selection.PersonId);
                _emailRecipients.Remove(id); _phoneRecipients.Remove(id);
                _facebookRecipients.Remove(id); _facebookSendRecipients.Remove(id);
                continue;
            }
            var name = book.Name(person);
            if (_emailRecipients.TryGetValue(id, out var email)) _emailRecipients[id] = email with { Name = name, Display = email.Display with { Name = name } };
            if (_phoneRecipients.TryGetValue(id, out var phone)) _phoneRecipients[id] = phone with { Name = name, Display = phone.Display with { Name = name } };
            if (_facebookRecipients.TryGetValue(id, out var facebook))
            {
                _facebookRecipients[id] = facebook with { Name = name };
                // Manual contact names are the user-provided Messenger identity.
                if (book.For(person).All(r => book.Source(r).Kind == "local"))
                    _facebookSendRecipients[id] = _facebookSendRecipients[id] with { Name = name };
            }
        }
        foreach (var id in _unavailableContacts.Keys.ToArray())
        {
            var person = book.People.FirstOrDefault(p => "contact-person:" + p.Id == id);
            if (person is null || ContactChannels.For(book, person).Length > 0)
            { removedCurrent |= _destination == id; _unavailableContacts.Remove(id); }
            else _unavailableContacts[id] = _unavailableContacts[id] with { Name = book.Name(person) };
        }
        if (removedCurrent) SelectDestination(_preferences.Destination);
        else if (IsContactDestination(_destination)) UpdateDestinationPresentation();
    }

    private void AssociateContactChannel(string destination, string personId, string kind, string address, string? sender = null)
    {
        var selection = new ContactChannelSelection(personId, kind, address, sender);
        _contactChannelSelections[destination] = selection;
        _preferredContactChannels[personId] = selection;
    }

    private void SelectUnavailableContact(ContactBook book, ContactPerson person)
    {
        var id = "contact-person:" + person.Id;
        _unavailableContacts[id] = new(book.Name(person), "");
        _composerDrafts.Isolate(id);
        ContactProfile.Hide();
        SelectDestination(id); SetFacebookOpen(false); FocusComposer();
    }

    private void UpdateContactChannelPicker()
    {
        ContactChannelPicker.Children.Clear(); ContactChannelButtons.Children.Clear();
        ContactChannelPicker.Visibility = Visibility.Collapsed;
        if (!IsContactDestination(_destination)) return;
        var book = _services.Contacts.Book;
        var selection = _contactChannelSelections.GetValueOrDefault(_destination);
        if (selection is not null) _preferredContactChannels[selection.PersonId] = selection;
        var person = selection is null ? null : book.People.FirstOrDefault(p => p.Id == selection.PersonId);
        var channels = person is not null ? ContactChannels.For(book, person) :
            _emailRecipients.TryGetValue(_destination, out var email) ? [new ContactChannel("email", email.Address, "Email", "")] :
            _phoneRecipients.TryGetValue(_destination, out var phone) ? [new ContactChannel("phone", phone.Address, "Message", "")] :
            _facebookRecipients.TryGetValue(_destination, out var facebook) && facebook.Link is not null ?
                [new ContactChannel("messenger", facebook.Link, "Facebook", "")] : Array.Empty<ContactChannel>();
        if (channels.Length == 0) return;
        var currentKind = selection?.Kind ?? (IsFacebookDestination(_destination) ? "messenger" : IsPhoneContactDestination(_destination) ? "phone" : "email");
        var currentAddress = selection?.Address ?? channels[0].Address;
        foreach (var group in channels.GroupBy(c => c.Kind).OrderBy(g => g.Key == "phone" ? 0 : g.Key == "email" ? 1 : 2))
        {
            var kind = group.Key;
            var label = kind == "phone" ? "iMessage" : kind == "email" ? "Email" : "Facebook";
            var chosen = group.FirstOrDefault(c => SameContactAddress(kind, c.Address, currentAddress)) ?? group.First();
            if (kind == "phone")
                ContactChannelButtons.Children.Add(CreateContactChannelPill("sms", "SMS", "SMS sending is disabled in HABITAT.",
                    "SMS sending is disabled."));

            var button = CreateContactChannelPill(kind, label, "Use " + label + " for this contact",
                label + " · " + (kind == "messenger" ? "Messenger" : chosen.Address), kind == currentKind);
            button.Checked += async (_, _) =>
            {
                if (kind == currentKind) return;
                try
                {
                    if (person is not null) await ChangeContactChannelAsync(book, person, chosen, selection?.Sender);
                }
                finally
                {
                    // A successful switch rebuilds the picker. Keep the previous choice if the switch was rejected.
                    if (ContactChannelButtons.Children.Contains(button)) button.IsChecked = false;
                }
            };
            button.Unchecked += (_, _) => { if (kind == currentKind) button.IsChecked = true; };
            ContactChannelButtons.Children.Add(button);
        }
        ContactChannelButtonScroll.Content ??= ContactChannelButtons;
        ContactChannelPicker.Children.Add(ContactChannelButtonScroll);
        var addresses = channels.Where(c => c.Kind == currentKind).ToArray();
        if (addresses.Length > 1 && person is not null)
        {
            var address = new ComboBox { Width = 240, Height = 32, ItemsSource = addresses.Select(c => c.Address).ToArray(),
                SelectedIndex = Math.Max(0, Array.FindIndex(addresses, c => SameContactAddress(currentKind, c.Address, currentAddress))) };
            AutomationProperties.SetName(address, "Recipient address");
            address.SelectionChanged += async (_, _) =>
            {
                if (address.SelectedIndex >= 0 && !SameContactAddress(currentKind, addresses[address.SelectedIndex].Address, currentAddress))
                    await ChangeContactChannelAsync(book, person, addresses[address.SelectedIndex], selection?.Sender);
            };
            ContactChannelPicker.Children.Add(address);
        }
        var senders = ContactEmailSenders();
        if (currentKind == "email" && senders.Length > 1 && person is not null)
        {
            var sender = new ComboBox { Width = 240, Height = 32, ItemsSource = senders,
                SelectedIndex = Math.Max(0, Array.FindIndex(senders, s => s.Equals(selection?.Sender, StringComparison.OrdinalIgnoreCase))) };
            AutomationProperties.SetName(sender, "Gmail sender account");
            ToolTipService.SetToolTip(sender, "Gmail sender account");
            sender.SelectionChanged += async (_, _) =>
            {
                if (sender.SelectedIndex >= 0 && !senders[sender.SelectedIndex].Equals(selection?.Sender, StringComparison.OrdinalIgnoreCase))
                    await ChangeContactChannelAsync(book, person,
                    addresses.First(c => SameContactAddress(currentKind, c.Address, currentAddress)), senders[sender.SelectedIndex]);
            };
            ContactChannelPicker.Children.Add(sender);
        }
        ContactChannelPicker.Height = ContactChannelPicker.Children.Count * 32 + (ContactChannelPicker.Children.Count - 1) * 6;
        ContactChannelPicker.Visibility = Visibility.Visible;
        PaintContactChannelPicker();
    }

    private async Task ChangeContactChannelAsync(ContactBook book, ContactPerson person, ContactChannel channel, string? sender)
    {
        if (!ContactChannelsEnabled || HasAppearanceOverlay || _updateHandoff || _operations.IsStopping) return;
        if (channel.Kind == "email" && sender is null)
            sender = book.Sources.FirstOrDefault(s => s.Id == channel.SourceId && ContactChannels.IsEmail(s.Account))?.Account ??
                _gmailAuth?.Email ?? ContactEmailSenders().FirstOrDefault();
        await OpenContactChannelAsync(book, person, channel, sender);
    }

    private static ToggleButton CreateContactChannelPill(string kind, string label, string help, string tooltip, bool selected = false)
    {
        var button = new ToggleButton
        {
            Content = label, Tag = kind, IsChecked = selected, IsEnabled = kind != "sms",
            Height = 32, MinWidth = 48, Padding = new Thickness(12, 0, 12, 0),
            CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1),
            FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(button, kind == "sms" ? "SMS (disabled)" : label);
        AutomationProperties.SetHelpText(button, help);
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private void PaintContactChannelPicker()
    {
        var dark = RequestedTheme == ElementTheme.Dark;
        var text = TextBrush();
        var surface = new SolidColorBrush(dark ? Color(0xED, 0x25, 0x2B, 0x37) : Color(0xED, 0xF9, 0xF8, 0xFC));
        var outline = new SolidColorBrush(dark ? Color(0xFF, 0x51, 0x5B, 0x6D) : Color(0xFF, 0xC3, 0xC7, 0xD4));
        var hover = new SolidColorBrush(dark ? Color(0xFF, 0x34, 0x3C, 0x4B) : Color(0xFF, 0xEB, 0xED, 0xF5));
        var pressed = new SolidColorBrush(dark ? Color(0xFF, 0x40, 0x49, 0x5B) : Color(0xFF, 0xDC, 0xE1, 0xEF));
        var accent = new SolidColorBrush(dark ? Color(0xFF, 0xB1, 0xBA, 0xFF) : Color(0xFF, 0x49, 0x54, 0xB8));
        var accentHover = new SolidColorBrush(dark ? Color(0xFF, 0xC4, 0xCB, 0xFF) : Color(0xFF, 0x3F, 0x49, 0xA8));
        var accentPressed = new SolidColorBrush(dark ? Color(0xFF, 0x9D, 0xA8, 0xF5) : Color(0xFF, 0x36, 0x40, 0x94));
        var accentText = new SolidColorBrush(dark ? Color(0xFF, 0x1A, 0x22, 0x48) : Color(0xFF, 0xFF, 0xFF, 0xFF));
        var muted = new SolidColorBrush(dark ? Color(0xFF, 0xA0, 0xA8, 0xB7) : Color(0xFF, 0x6A, 0x71, 0x82));
        foreach (var button in ContactChannelButtons.Children.OfType<ToggleButton>())
        {
            var selected = button.IsChecked == true;
            var unavailable = button.Tag as string == "sms";
            button.Foreground = unavailable ? muted : selected ? accentText : text;
            button.Background = selected ? accent : surface;
            button.BorderBrush = selected ? accent : outline;
            SetState("PointerOver", hover, outline, text);
            SetState("Pressed", pressed, outline, text);
            SetState("Checked", accent, accent, accentText);
            SetState("CheckedPointerOver", accentHover, accentHover, accentText);
            SetState("CheckedPressed", accentPressed, accentPressed, accentText);
            SetState("Disabled", selected ? accent : surface, selected ? accent : outline, selected ? accentText : muted);
            AutomationProperties.SetItemStatus(button, unavailable ? "Disabled" : selected ? "Selected" : "Not selected");

            void SetState(string suffix, Brush background, Brush border, Brush foreground)
            {
                button.Resources["ToggleButtonBackground" + suffix] = background;
                button.Resources["ToggleButtonBorderBrush" + suffix] = border;
                button.Resources["ToggleButtonForeground" + suffix] = foreground;
            }
        }
    }
}
