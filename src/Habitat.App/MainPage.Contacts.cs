using Habitat.Features.Contacts;
using Habitat.Features.Facebook;
using Habitat.Features.Preferences;
using Habitat.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;

namespace Habitat;

public sealed partial class MainPage
{
    private readonly Button ContactsSettings = new() { Content = "Manage contacts", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button ContactsDuplicates = new() { Content = "Possible duplicates", Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button ContactsLoadMore = new() { Content = "Load more contacts", Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button ContactsEnableGoogle = new() { Content = "Enable Gmail contacts", Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Stretch };
    private int _contactVisibleLimit = 100;
    private ContactBook? _renderedContactBook;
    private string? _renderedContactQuery, _renderedContactError;
    private int _renderedContactLimit;
    private readonly Flyout ContactProfile = new();
    private string? _contactToManage;
    private bool _reviewContactDuplicates;
    private bool _browserContactUpdating;
    private ContactsPreferencesView? _contactsPreferences;

    private ContactsPreferencesView CreateContactsPreferences() => new(_services.Contacts, ConnectGoogleContactsAsync,
        ReloadGoogleContactsAsync, PickVCardContactsAsync, ReadFacebookCandidatesAsync, DisconnectContactSourceAsync,
        _services.Bridge.BrowserContacts, SetBrowserContactSource, EnableBrowserContactAccessAsync);

    private bool SetBrowserContactSource(string provider, bool enabled)
    {
        var settings = provider == "facebook" ? _preferences.BrowserContacts with { Facebook = enabled }
            : _preferences.BrowserContacts with { Google = enabled };
        var updated = _preferences with { BrowserContacts = settings };
        if (!updated.Save(_services.Paths.PreferencesPath, ex => _services.ReportError("Save automatic contacts", ex))) return false;
        _preferences = updated;
        _services.Bridge.BrowserContacts.Configure(settings);
        return true;
    }

    private async Task EnableBrowserContactAccessAsync(string provider)
    {
        if (_services.Bridge.BrowserContacts.Supported) _services.Bridge.BrowserContacts.RequestSetup(provider);
        else if (!await _launchBrowser(new Uri("chrome://extensions/")))
            throw new InvalidOperationException("Open Chrome extensions and reload HABITAT Browser Bridge 0.10.8 or later.");
    }

    private void TickBrowserContacts()
    {
        _contactsPreferences?.RefreshBrowserState();
        RefreshContactSourceAction();
        if (_browserContactUpdating || _operations.IsStopping) return;
        if (!_services.Bridge.BrowserContacts.TryTake(out var first) || first is null) return;
        _browserContactUpdating = true;
        _ = RunOperationAsync(async () =>
        {
            try
            {
                if (!_services.Contacts.Loaded && _services.Contacts.Error is null) await _services.Contacts.LoadAsync(_operations.Token);
                if (!_services.Contacts.Loaded || _services.Contacts.Error is not null) return;
                var snapshots = new List<BrowserContactSnapshot> { first };
                while (_services.Bridge.BrowserContacts.TryTake(out var next) && next is not null) snapshots.Add(next);
                await _services.Contacts.ChangeAsync(book =>
                {
                    foreach (var snapshot in snapshots)
                        if (_services.Bridge.BrowserContacts.Settings.Allows(snapshot.Provider))
                            book = BrowserContactsService.Apply(book, snapshot);
                    return book;
                }, _operations.Token);
            }
            finally { _browserContactUpdating = false; }
        });
    }

    private async Task<ContactImport?> ConnectGoogleContactsAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _operations.Token);
        using var auth = _services.CreateContactsAuth();
        var client = _gmailAuth?.Client;
        if (client is null && _services.Contacts.Book.Sources.FirstOrDefault(s => s.Kind == "google") is { } existing)
        {
            using var savedAuth = _services.CreateContactsAuth(_services.ContactsCredentials(existing.Id));
            client = savedAuth.Client;
        }
        if (client is null)
        {
            var file = await _services.PickContactsFileAsync(_windowHost?.Handle ?? 0, client: true);
            if (file is null) return null;
            if (new FileInfo(file).Length > 65536) throw new InvalidDataException("Choose a Google Desktop OAuth client JSON smaller than 64 KB.");
            client = GmailClientConfiguration.FromJson(await File.ReadAllTextAsync(file, linked.Token));
        }
        auth.Configure(client);
        await auth.SignInAsync(linked.Token);
        var email = auth.Email ?? throw new InvalidDataException("Google returned no account email.");
        var source = _services.Contacts.Book.Sources.FirstOrDefault(s => s.Kind == "google" && s.Account.Equals(email, StringComparison.OrdinalIgnoreCase));
        source = source is null ? new(ContactBook.NewId(), "google", "Google · " + email, email, true) : source with { Connected = true };
        _services.ContactsCredentials(source.Id).Save(auth.Account);
        using var google = _services.CreateGoogleContacts(auth);
        // A connection is distinct from permission to import its contact records.
        if (!await _services.Contacts.ChangeAsync(b => b.WithSource(source), linked.Token)) return null;
        return new(source, await google.ReadAsync(linked.Token), true);
    }

    private async Task<ContactCandidate[]> ReloadGoogleContactsAsync(ContactSource source, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _operations.Token);
        using var auth = _services.CreateContactsAuth(_services.ContactsCredentials(source.Id));
        if (!auth.IsSignedIn) throw new InvalidOperationException("Reconnect this Google account before reloading contacts.");
        if (!string.Equals(auth.Email, source.Account, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Google authorization does not match this contact source. Reconnect the account.");
        using var google = _services.CreateGoogleContacts(auth);
        return await google.ReadAsync(linked.Token);
    }

    private async Task DisconnectContactSourceAsync(ContactSource source, CancellationToken token)
    {
        using var auth = _services.CreateContactsAuth(_services.ContactsCredentials(source.Id));
        await auth.DisconnectAsync(token);
    }

    private async Task<(string Name, ContactCandidate[] Candidates)?> PickVCardContactsAsync(CancellationToken token)
    {
        var file = await _services.PickContactsFileAsync(_windowHost?.Handle ?? 0);
        return file is null ? null : (Path.GetFileNameWithoutExtension(file), await VCardContacts.ReadAsync(file, token));
    }

    private async Task<ContactCandidate[]> ReadFacebookCandidatesAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _operations.Token);
        var contacts = await _services.Facebook.ReadAsync(linked.Token);
        return contacts.Select(contact =>
        {
            var fields = new List<ContactValue> { new("name", contact.Name) };
            if (!string.IsNullOrWhiteSpace(contact.PhotoUrl)) fields.Add(new("photo", contact.PhotoUrl));
            if (contact.Link is not null && FacebookContactsService.SafeLink(contact.Link))
            { fields.Add(new("profile", contact.Link)); fields.Add(new("messenger", contact.Link)); }
            return new ContactCandidate(contact.Link ?? "snapshot:" + ContactBook.Hash(contact.Name + "\n" + contact.PhotoUrl),
                fields.ToArray(), JsonSerializer.Serialize(contact));
        }).DistinctBy(c => c.RemoteId).ToArray();
    }

    private void ContactsChanged()
    {
        if (!_operations.IsStopping && _facebookPanelOpen) RenderUnifiedContacts();
    }

    private void RenderUnifiedContacts()
    {
        RefreshContactSourceAction();
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
        var count = book.Suggestions().Length;
        ContactsDuplicates.Content = $"Possible duplicates ({count})";
        ContactsDuplicates.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetItemStatus(FacebookButton, $"{book.ListedPeople.Count()} contacts");
        AutomationProperties.SetHelpText(FacebookButton, _facebookError ?? "Open your unified address book");
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
        var menu = new MenuFlyout();
        var details = new MenuFlyoutItem { Text = "Contact details" };
        details.Click += (_, _) => ShowContactProfile(person.Id, card);
        menu.Items.Add(details);
        card.ContextFlyout = menu;
        SizeFacebookContact(card); FacebookAvatars.Children.Add(card);
    }

    private void RefreshContactSourceAction() => ContactsEnableGoogle.Visibility =
        _services.Bridge.BrowserContacts.StateFor("google").State == "permission" ? Visibility.Visible : Visibility.Collapsed;

    private string[] ContactEmailSenders() => _services.Contacts.Book.Sources
        .Where(s => s.Kind is "google" or "google-browser" && s.Connected && ContactChannels.IsEmail(s.Account)).Select(s => s.Account)
        .Concat(_gmailAuth?.Email is { } email ? new[] { email } : []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private async Task OpenContactAsync(string personId, FrameworkElement anchor)
    {
        if (HasAppearanceOverlay || _updateHandoff || _operations.IsStopping) return;
        var book = _services.Contacts.Book;
        var person = book.People.FirstOrDefault(p => p.Id == personId);
        if (person is null) return;
        var channels = ContactChannels.For(book, person);
        var senders = ContactEmailSenders();
        if (channels.Length != 1 || channels[0].Kind == "email" && senders.Length > 1)
        { ShowContactProfile(personId, anchor); return; }
        try { await OpenContactChannelAsync(book, person, channels[0], senders.FirstOrDefault()); }
        catch (Exception) { ShowContactProfile(personId, anchor, "Could not open this channel. Your text was kept. Try again."); }
    }

    private Task OpenContactChannelAsync(ContactBook book, ContactPerson person, ContactChannel channel, string? sender = null)
    {
        if (channel.Kind == "messenger")
        {
            var record = book.For(person).First(r => r.SourceId == channel.SourceId &&
                r.Current.Values.Any(v => v.Kind == "messenger" && v.Value == channel.Address));
            var photo = record.Current.Values.FirstOrDefault(v => v.Kind == "photo" && FacebookContactsService.SafePhoto(v.Value))?.Value ?? "";
            var sourceName = record.Current.Values.FirstOrDefault(v => v.Kind == "name")?.Value ?? book.Name(person);
            ContactProfile.Hide();
            SelectFacebookContact(new(sourceName, photo, channel.Address), book.Name(person));
        }
        else
        {
            ContactProfile.Hide();
            SelectEmailContact(book, person, channel.Address, sender);
        }
        return Task.CompletedTask;
    }

    private void ShowContactProfile(string personId, FrameworkElement anchor, string? error = null)
    {
        var book = _services.Contacts.Book;
        var person = book.People.FirstOrDefault(p => p.Id == personId);
        if (person is null) return;
        var panel = new StackPanel { Spacing = 12, Width = Math.Clamp(XamlRoot.Size.Width - 100, 240, 380) };
        var status = new TextBlock { Text = error ?? "", TextWrapping = TextWrapping.Wrap, FontSize = 12,
            Visibility = error is null ? Visibility.Collapsed : Visibility.Visible };
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        var senders = ContactEmailSenders();
        var sender = new ComboBox { Header = "Gmail sender account", ItemsSource = senders.Length > 0 ? senders : new[] { "Default Google account" },
            SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(sender, "Gmail sender account");
        var channels = ContactChannels.For(book, person);
        if (channels.Any(c => c.Kind == "email") && senders.Length > 1) panel.Children.Add(sender);
        foreach (var channel in channels)
        {
            var button = new Button { Content = channel.Label + " · " + (channel.Kind == "email" ? channel.Address : book.Sources.Single(s => s.Id == channel.SourceId).Label),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(button, channel.Label + " for " + book.Name(person) + (channel.Kind == "email" ? ", " + channel.Address : ""));
            if (channel.Kind == "messenger")
            {
                button.IsEnabled = _services.Facebook.IsConnected && _services.FacebookMessaging.IsAvailable;
                if (!button.IsEnabled) ToolTipService.SetToolTip(button, "Reconnect Facebook or update the HABITAT Chrome extension to use Messenger.");
            }
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                try
                {
                    var account = senders.Length > 0 ? senders[Math.Max(0, sender.SelectedIndex)] : null;
                    await OpenContactChannelAsync(book, person, channel, account);
                }
                catch (Exception exception)
                {
                    status.Text = exception is InvalidOperationException ? exception.Message : "Could not open this channel. Your text was kept.";
                    status.Visibility = Visibility.Visible;
                }
                finally { button.IsEnabled = true; }
            };
            panel.Children.Add(button);
        }
        panel.Children.Add(status);
        if (channels.Length == 0) panel.Children.Add(new TextBlock { Text = "No compatible communication channel is available for this contact.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(ContactsPreferencesView.Profile(book, person));
        var manage = new Button { Content = "Manage or merge contact" };
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
