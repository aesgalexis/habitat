using Habitat.Features.Contacts;
using Habitat.Features.Facebook;
using Habitat.Features.Preferences;
using Habitat.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private readonly Button ContactsSettings = new() { Content = "Manage contacts", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button ContactsAdd = new() { Content = "Add contact", HorizontalAlignment = HorizontalAlignment.Stretch };
    private bool _newContact;
    private readonly Button ContactsLoadMore = new() { Content = "Load more contacts", Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Stretch };
    private int _contactVisibleLimit = 100;
    private ContactBook? _renderedContactBook;
    private string? _renderedContactQuery, _renderedContactError;
    private int _renderedContactLimit;
    private readonly Flyout ContactProfile = new();
    private string? _contactToManage;
    private ContactsPreferencesView? _contactsPreferences;

    private ContactsPreferencesView CreateContactsPreferences() => new(_services.Contacts,
        drive: _services.ContactDrive, pickDriveClient: PickDriveClientAsync);

    private async Task<GmailClientConfiguration?> PickDriveClientAsync()
    {
        var file = await _services.PickGoogleClientAsync(_windowHost?.Handle ?? 0);
        if (file is null) return null;
        if (new FileInfo(file).Length > 65536) throw new InvalidDataException("Choose a Google Desktop OAuth client JSON smaller than 64 KB.");
        return GmailClientConfiguration.FromJson(await File.ReadAllTextAsync(file, _operations.Token));
    }

    private void TickDriveContacts()
    {
        if (_operations.IsStopping || _services.DriveSync is not { Busy: false } drive) return;
        _ = RunOperationAsync(async () =>
        {
            var book = _services.Contacts.Book;
            var status = drive.Contacts.Status;
            var settings = _contactsPreferences;
            await drive.TickAsync(DateTimeOffset.UtcNow,
                _contactsPreferences is { IsEditingContact: true } or { IsBusy: true }, _operations.Token);
            if (_operations.IsStopping) return;
            if (_contactsPreferences is { IsBusy: false, IsEditingContact: false } contacts &&
                (!ReferenceEquals(book, _services.Contacts.Book) || status != drive.Contacts.Status || !ReferenceEquals(settings, contacts)))
                contacts.Refresh();
            RefreshDriveTasks();
        });
    }

    private void ContactsChanged()
    {
        if (_operations.IsStopping) return;
        if (_facebookPanelOpen) RenderUnifiedContacts();
        RefreshSavedContactDestinations();
    }

    private void RenderUnifiedContacts()
    {
        var book = _services.Contacts.Book;
        var query = FacebookContactSearch.Text.Trim();
        var error = _services.Contacts.Error;
        if (ReferenceEquals(book, _renderedContactBook) && query == _renderedContactQuery &&
            error == _renderedContactError && _contactVisibleLimit == _renderedContactLimit) return;
        _renderedContactBook = book; _renderedContactQuery = query; _renderedContactError = error;
        _renderedContactLimit = _contactVisibleLimit;
        FacebookAvatars.Children.Clear();
        _facebookError = error;
        var matches = book.ListedPeople.Where(p => query.Length == 0 || ContactBook.Matches(book.Name(p), query) ||
            book.For(p).Any(r => r.Current.Values.Any(v => v.Kind is "email" or "phone" && ContactBook.Matches(v.Value, query))))
            .OrderBy(book.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (_facebookError is null)
            foreach (var person in matches.Take(_contactVisibleLimit)) AddUnifiedContact(book, person);
        ContactsLoadMore.Visibility = matches.Length > _contactVisibleLimit ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetItemStatus(FacebookButton, $"{book.ListedPeople.Count()} contacts");
        AutomationProperties.SetHelpText(FacebookButton, _facebookError ?? "Open your contacts");
        ApplyFacebookContactFilter(); PaintFacebook();
    }

    private void AddUnifiedContact(ContactBook book, ContactPerson person)
    {
        var name = book.Name(person);
        var photo = book.For(person).SelectMany(r => r.Current.Values).FirstOrDefault(v => v.Kind == "photo" && FacebookContactsService.SafePhoto(v.Value))?.Value ?? "";
        var avatar = CreateFacebookAvatar(new(name, photo));
        avatar.Children[2].Visibility = Visibility.Collapsed;
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(avatar);
        var title = new TextBlock { Text = name, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center, MaxLines = 1 };
        Grid.SetColumn(title, 1); row.Children.Add(title);
        var card = new Button { Content = row, Tag = person.Id, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
            BorderThickness = new Thickness(2, 0, 0, 0), CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        UseStableWidgetButton(card);
        AutomationProperties.SetName(card, name);
        AutomationProperties.SetHelpText(card, "Open contact");
        AutomationProperties.SetItemStatus(card, string.Join(", ", book.For(person).Select(r => book.Source(r).Label).Distinct()));
        card.Click += async (_, _) => await OpenContactAsync(person.Id, card);
        card.ContextRequested += (_, e) => { e.Handled = true; ShowContactProfile(person.Id, card); };
        SizeFacebookContact(card); FacebookAvatars.Children.Add(card);
    }


    private string[] ContactEmailSenders() => _services.Contacts.Book.Sources
        .Where(s => s.Kind is "google" or "google-browser" && s.Connected && ContactChannels.IsEmail(s.Account)).Select(s => s.Account)
        .Concat(_gmailAuth?.Email is { } email ? new[] { email } : []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private async Task OpenContactAsync(string personId, FrameworkElement anchor)
    {
        if (HasAppearanceOverlay || _updateHandoff || _operations.IsStopping) return;
        var book = _services.Contacts.Book;
        var person = book.People.FirstOrDefault(p => p.Id == personId);
        if (person is null) return;
        var selected = _contactDestinations.Choose(book, person, ContactEmailSenders(), _gmailAuth?.Email);
        if (selected is null) { SelectUnavailableContact(book, person); return; }
        await OpenContactChannelAsync(book, person, selected.Value.Channel, selected.Value.Sender);
    }

    private async Task OpenContactChannelAsync(ContactBook book, ContactPerson person, ContactChannel channel, string? sender = null)
    {
        var id = channel.Kind == "phone" ? ContactDestinationSession.PhoneId(person.Id, channel.Address) :
            channel.Kind == "email" ? ContactDestinationSession.EmailId(person.Id, channel.Address, sender) :
            ContactDestinationSession.MessengerId(new(book.Name(person), "", channel.Address), person.Id);
        var previousDestination = _destination;
        await _contactDestinationViews.WaitForRetirementAsync(id);
        if (_operations.IsStopping || HasAppearanceOverlay || _updateDialogOpen || _updateHandoff || _destination != previousDestination) return;
        if (!ReferenceEquals(book, _services.Contacts.Book))
        {
            book = _services.Contacts.Book;
            var currentPerson = book.People.FirstOrDefault(item => item.Id == person.Id);
            if (currentPerson is null) return;
            var currentChannel = ContactChannels.For(book, currentPerson).FirstOrDefault(item =>
                item.Kind == channel.Kind && ContactDestinationSession.SameAddress(item.Kind, item.Address, channel.Address));
            if (currentChannel is null) return;
            person = currentPerson;
            channel = currentChannel;
        }
        if (channel.Kind == "messenger")
        {
            var record = book.For(person).First(r => r.SourceId == channel.SourceId &&
                r.Current.Values.Any(v => v.Kind == "messenger" && v.Value == channel.Address));
            var photo = record.Current.Values.FirstOrDefault(v => v.Kind == "photo" && FacebookContactsService.SafePhoto(v.Value))?.Value ?? "";
            var sourceName = record.Current.Values.FirstOrDefault(v => v.Kind == "name")?.Value ?? book.Name(person);
            ContactProfile.Hide();
            SelectFacebookContact(new(sourceName, photo, channel.Address), book.Name(person), person.Id);
        }
        else if (channel.Kind == "phone")
        {
            ContactProfile.Hide();
            SelectPhoneContact(book, person, channel.Address);
        }
        else
        {
            ContactProfile.Hide();
            SelectEmailContact(book, person, channel.Address, sender);
        }
    }

    private void ShowContactProfile(string personId, FrameworkElement anchor)
    {
        var book = _services.Contacts.Book;
        var person = book.People.FirstOrDefault(p => p.Id == personId);
        if (person is null) return;
        var panel = new StackPanel { Spacing = 8, Width = Math.Clamp(XamlRoot.Size.Width - 100, 200, 320) };
        panel.Children.Add(new TextBlock { Text = book.Name(person), FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        foreach (var value in book.For(person).SelectMany(r => r.Current.Values).Where(v => v.Kind is "email" or "phone" or "organization")
            .DistinctBy(v => (v.Kind, v.Value.ToUpperInvariant())))
            panel.Children.Add(new TextBlock { Text = value.Value, FontSize = 13, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        var phones = ContactChannels.For(book, person).Where(channel => channel.Kind == "phone").ToArray();
        foreach (var phone in phones)
        {
            var view = new Button { Content = phones.Length == 1 ? "View conversation" : "View conversation · " + phone.Address };
            view.Click += async (_, _) => await RunOperationAsync(() => ViewPhoneConversationAsync(person.Id, phone.Address));
            panel.Children.Add(view);
        }
        var manage = new Button { Content = "Manage contact" };
        manage.Click += async (_, _) =>
        {
            ContactProfile.Hide(); _contactToManage = person.Id;
            await RunOperationAsync(() => ShowPreferencesAsync("Contacts"));
        };
        panel.Children.Add(manage);
        ContactProfile.Content = new ScrollViewer { Content = panel, MaxHeight = Math.Clamp(XamlRoot.Size.Height - 120, 180, 650),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, RequestedTheme = RequestedTheme };
        ContactProfile.ShowAt(anchor);
    }
}
