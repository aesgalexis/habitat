using Habitat.Features.Contacts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Habitat.Features.Preferences;

internal sealed record ContactImport(ContactSource Source, ContactCandidate[] Candidates, bool Complete = false);

internal sealed class ContactsPreferencesView : StackPanel
{
    private readonly ContactSession _session;
    private readonly Func<CancellationToken, Task<ContactImport?>> _connectGoogle;
    private readonly Func<ContactSource, CancellationToken, Task<ContactCandidate[]>> _reloadGoogle;
    private readonly Func<CancellationToken, Task<(string Name, ContactCandidate[] Candidates)?>> _pickVCard;
    private readonly Func<CancellationToken, Task<ContactCandidate[]>> _readFacebook;
    private readonly Func<ContactSource, CancellationToken, Task> _disconnect;
    private readonly BrowserContactsService? _browser;
    private readonly Func<string, bool, bool>? _setBrowserSource;
    private readonly Func<string, Task>? _enableBrowserAccess;
    private readonly Dictionary<string, (ToggleSwitch Toggle, TextBlock Status)> _browserRows = new();
    private bool _advanced, _updatingBrowserToggle;
    private readonly CancellationTokenSource _stop = new();
    private Task _pending = Task.CompletedTask;
    private bool _busy;
    private string _facebookAccount = "", _importName = "", _query = "";
    private string? _selectedPerson, _mergeTarget;
    private ContactSuggestion? _review;
    private ContactImport? _preview;
    private ListView? _previewList;
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "Sources", "People", "Possible duplicates" }, SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _status = Text("", 13);
    private readonly StackPanel _body = new() { Spacing = 12 };
    private readonly ContentControl _bodyHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    internal ContactImport? Preview => _preview;
    internal bool IsBusy => _busy;

    internal ContactsPreferencesView(ContactSession session,
        Func<CancellationToken, Task<ContactImport?>>? connectGoogle = null,
        Func<ContactSource, CancellationToken, Task<ContactCandidate[]>>? reloadGoogle = null,
        Func<CancellationToken, Task<(string Name, ContactCandidate[] Candidates)?>>? pickVCard = null,
        Func<CancellationToken, Task<ContactCandidate[]>>? readFacebook = null,
        Func<ContactSource, CancellationToken, Task>? disconnect = null,
        BrowserContactsService? browser = null, Func<string, bool, bool>? setBrowserSource = null,
        Func<string, Task>? enableBrowserAccess = null)
    {
        _session = session;
        _connectGoogle = connectGoogle ?? (_ => Task.FromResult<ContactImport?>(null));
        _reloadGoogle = reloadGoogle ?? ((_, _) => Task.FromResult<ContactCandidate[]>([]));
        _pickVCard = pickVCard ?? (_ => Task.FromResult<(string, ContactCandidate[])?>(null));
        _readFacebook = readFacebook ?? (_ => Task.FromResult<ContactCandidate[]>([]));
        _disconnect = disconnect ?? ((_, _) => Task.CompletedTask);
        _browser = browser; _setBrowserSource = setBrowserSource; _enableBrowserAccess = enableBrowserAccess;
        Spacing = 12;
        AutomationProperties.SetName(_mode, "Contacts settings section");
        AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        _bodyHost.Content = _body;
        Children.Add(_mode); Children.Add(_status); Children.Add(_bodyHost);
        _mode.SelectionChanged += (_, _) => { _review = null; Refresh(); };
        Refresh();
    }

    internal async Task StopAsync() { _stop.Cancel(); await _pending; _stop.Dispose(); }
    internal void SelectPerson(string id) { _selectedPerson = id; _mode.SelectedIndex = 1; Refresh(); }
    internal void SelectDuplicates() { _mode.SelectedIndex = 2; Refresh(); }
    internal async Task InitializeAsync()
    {
        if (!_session.Loaded) await _session.LoadAsync(_stop.Token);
        Refresh();
    }

    private void Action(StackPanel panel, string label, Func<Task> action, bool enabled = true)
    {
        var button = new Button { Content = label, IsEnabled = !_busy && enabled };
        AutomationProperties.SetName(button, label);
        button.Click += async (_, _) => await RunAsync(action);
        panel.Children.Add(button);
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || _stop.IsCancellationRequested) return;
        _busy = true; _status.Text = "Working…"; _mode.IsEnabled = false;
        _bodyHost.IsEnabled = false;
        _pending = Work();
        await _pending;
        async Task Work()
        {
            try { await action(); }
            catch (OperationCanceledException) { _status.Text = "Action cancelled. Your contacts were kept."; }
            catch (Exception exception)
            {
                _status.Text = exception is InvalidDataException or InvalidOperationException or Habitat.Models.GmailSignInRequiredException
                    ? exception.Message : "Could not complete this action. Your existing contacts were kept. Retry or reconnect the source.";
            }
            finally
            {
                _busy = false; _mode.IsEnabled = true; _bodyHost.IsEnabled = true;
                if (_status.Text == "Working…") _status.Text = "";
                Refresh();
            }
        }
    }

    internal void Refresh()
    {
        _browserRows.Clear();
        _body.Children.Clear();
        if (_session.Error is not null || !_session.Loaded)
        {
            _body.Children.Add(Text(_session.Error ?? "Load your contacts to get started."));
            Action(_body, "Reload contacts", async () => { await _session.LoadAsync(_stop.Token); });
            return;
        }
        if (_preview is not null) { ShowPreview(); return; }
        if (_review is not null) { ShowReview(); return; }
        switch (_mode.SelectedIndex)
        {
            case 1: ShowPeople(); break;
            case 2: ShowSuggestions(); break;
            default: ShowSources(); break;
        }
    }

    private void ShowSources()
    {
        var book = _session.Book;
        _body.Children.Add(Text("Enable a source once. HABITAT adds and updates people automatically while its page is open in Chrome. Saved contacts stay available when the page closes."));
        BrowserSource("facebook", "Facebook", "People loaded in Facebook Contacts or All friends. Uses your open Facebook session.");
        BrowserSource("google", "Gmail and Google Contacts", "Names and email addresses loaded in Gmail, plus contact rows in an open Google Contacts tab. Only the loaded people are available; this does not read mail bodies or the entire address book.");
        _body.Children.Add(Text("Existing Chrome permissions are used automatically. Turning a source off stops new reads and keeps saved contacts. Possible duplicate people still need your review.", 12));
        Action(_body, _advanced ? "Hide other contact options" : "Other contact options", () => { _advanced = !_advanced; return Task.CompletedTask; });
        RefreshBrowserState();
        if (!_advanced) return;
        _body.Children.Add(Text("Optional file imports and full Google API access. These use a separate import review.", 12));
        var google = Panel("Google Contacts", "Read contacts from one or more Google accounts. Email actions open Gmail drafts; this connection does not grant permission to send email.");
        google.Children.Add(Text("First connection: select a Desktop OAuth client JSON with People API enabled, or reuse the client already configured for Gmail. Contacts permission is requested separately.", 12));
        Action(google, "Connect Google account", async () =>
        {
            var import = await _connectGoogle(_stop.Token);
            if (import is null) return;
            BeginPreview(import);
        });
        _body.Children.Add(Card(google));
        foreach (var source in book.Sources.OrderBy(s => s.Label))
        {
            var count = book.Records.Count(r => r.SourceId == source.Id);
            var description = source.Kind switch
            {
                "google" => source.Connected ? "Connected · read-only Google Contacts" : "Disconnected · saved contacts retained",
                "google-browser" => "Automatic · people observed in your open Google pages",
                "facebook" => source.Connected ? "Automatic · people observed in your open Facebook page" : "Saved Facebook snapshot",
                "vcard" => "Imported file · import again using the same source name to update", _ => "Contacts created in HABITAT"
            };
            var panel = Panel(source.Label, description + $" · {count} records");
            if (source.LastImported is { } at) panel.Children.Add(Text("Last import: " + at.ToLocalTime().ToString("g", System.Globalization.CultureInfo.GetCultureInfo("en-GB")), 12));
            if (source.Kind == "google")
            {
                if (source.Connected)
                {
                    Action(panel, "Reload contacts", async () => BeginPreview(new(source, await _reloadGoogle(source, _stop.Token), true)));
                    Action(panel, "Disconnect", async () =>
                    {
                        await _disconnect(source, _stop.Token);
                        await _session.ChangeAsync(b => b.WithSource(source with { Connected = false }), _stop.Token);
                    });
                }
                else panel.Children.Add(Text("Use Connect Google account to reconnect this account.", 12));
            }
            _body.Children.Add(Card(panel));
        }
        var vcard = Panel("Phone and other accounts", "Import a .vcf export from your phone, iCloud, Outlook or another address book. Original fields are retained, including fields HABITAT cannot display as channels.");
        var importName = new TextBox { Header = "Import source name", PlaceholderText = "e.g. Personal phone", Text = _importName, MaxLength = 160 };
        AutomationProperties.SetName(importName, "Import source name");
        importName.TextChanged += (_, _) => _importName = importName.Text;
        vcard.Children.Add(importName);
        Action(vcard, "Import vCard…", async () =>
        {
            _importName = importName.Text;
            var file = await _pickVCard(_stop.Token);
            if (file is null) return;
            var label = string.IsNullOrWhiteSpace(_importName) ? file.Value.Name : _importName.Trim();
            var existing = _session.Book.Sources.FirstOrDefault(s => s.Kind == "vcard" && s.Account.Equals(label, StringComparison.OrdinalIgnoreCase));
            BeginPreview(new(existing ?? new(ContactBook.NewId(), "vcard", label, label, false), file.Value.Candidates));
        });
        _body.Children.Add(Card(vcard));
        var facebook = Panel("Facebook / Messenger", "Import a partial snapshot through the HABITAT Chrome extension. Choose the account name yourself and check the signed-in Facebook account before each import. This is not a full friends sync.");
        var account = new TextBox { Header = "Facebook account name", PlaceholderText = "Name of the signed-in Facebook account", Text = _facebookAccount, MaxLength = 160 };
        AutomationProperties.SetName(account, "Facebook account name");
        account.TextChanged += (_, _) => _facebookAccount = account.Text;
        facebook.Children.Add(account);
        Action(facebook, "Preview Facebook contacts", async () =>
        {
            var label = account.Text.Trim();
            _facebookAccount = label;
            if (label.Length == 0) throw new InvalidOperationException("Enter the Facebook account name before reading its contacts.");
            var contacts = await _readFacebook(_stop.Token);
            var existing = _session.Book.Sources.FirstOrDefault(s => s.Kind == "facebook" && s.Account.Equals(label, StringComparison.OrdinalIgnoreCase));
            BeginPreview(new(existing ?? new(ContactBook.NewId(), "facebook", "Facebook · " + label, label, false), contacts));
        });
        _body.Children.Add(Card(facebook));
        _body.Children.Add(Text("iMessage and other messaging services will appear only when a compatible integration is available. A phone number alone does not establish availability.", 12));
    }

    private void BrowserSource(string provider, string title, string description)
    {
        var state = _browser?.StateFor(provider);
        var setting = provider == "facebook" ? _browser?.Settings.Facebook : _browser?.Settings.Google;
        var toggle = new ToggleSwitch { Header = title, OnContent = "On", OffContent = "Off",
            IsOn = setting ?? state?.Allowed == true, IsEnabled = _setBrowserSource is not null };
        AutomationProperties.SetName(toggle, "Automatic " + title + " contacts");
        var status = Text("", 12);
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        _browserRows[provider] = (toggle, status);
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(toggle); panel.Children.Add(Text(description, 12)); panel.Children.Add(status);
        toggle.Toggled += async (_, _) =>
        {
            if (_updatingBrowserToggle) return;
            var enabled = toggle.IsOn;
            await RunAsync(async () =>
            {
                if (_setBrowserSource?.Invoke(provider, enabled) != true)
                    throw new InvalidOperationException("Could not save this contact source. Try again.");
                if (enabled && _browser?.StateFor(provider).Allowed != true && _enableBrowserAccess is not null)
                    await _enableBrowserAccess(provider);
            });
        };
        Action(panel, "Enable " + title + " in Chrome", async () =>
        {
            if (_setBrowserSource?.Invoke(provider, true) != true) throw new InvalidOperationException("Could not save this contact source. Try again.");
            if (_enableBrowserAccess is not null) await _enableBrowserAccess(provider);
        }, _enableBrowserAccess is not null);
        _body.Children.Add(Card(panel));
    }

    internal void RefreshBrowserState()
    {
        if (_browser is null || _stop.IsCancellationRequested) return;
        foreach (var (provider, row) in _browserRows)
        {
            var state = _browser.StateFor(provider);
            var setting = provider == "facebook" ? _browser.Settings.Facebook : _browser.Settings.Google;
            _updatingBrowserToggle = true;
            try { row.Toggle.IsOn = setting ?? state.Allowed; }
            finally { _updatingBrowserToggle = false; }
            var saved = _session.Book.Sources.Where(s => provider == "facebook" ? s.Kind == "facebook" : s.Kind == "google-browser").Select(s => s.Id).ToHashSet();
            var count = _session.Book.Records.Count(r => saved.Contains(r.SourceId));
            row.Status.Text = state.State switch
            {
                "disabled" => "Off · saved contacts kept",
                "disconnected" => "Waiting for the HABITAT Chrome extension",
                "extension-update" => "Reload HABITAT Browser Bridge 0.10.8 or later in Chrome",
                "permission" => "Enable access in Chrome once to start automatic contacts",
                "waiting-for-tab" => provider == "facebook" ? "Waiting for an open Facebook tab" : "Waiting for an open Gmail or Google Contacts tab",
                "login" => "Sign in to the open provider tab",
                "account-unavailable" => "Waiting for the signed-in account address to appear in Google’s account button",
                "waiting-for-contacts" => provider == "facebook" ? "Facebook is open · waiting for Contacts or All friends to load" : "Google is open · waiting for people to load",
                "ready" => $"Automatic · {count} saved contacts · {state.Count} people loaded in Chrome",
                _ => "Checking open tabs…"
            };
        }
    }

    internal void BeginPreview(ContactImport import)
    {
        _preview = import;
        _status.Text = import.Candidates.Length == 0 ? "No contacts were returned. Your saved contacts were kept." : "";
    }

    private void ShowPreview()
    {
        var preview = _preview!;
        _body.Children.Add(Text("Review import · " + preview.Source.Label, 18));
        _body.Children.Add(Text($"{preview.Candidates.Length} source records. Select the contacts to import. Existing records from this source will be updated; possible matches in other sources will stay separate until you merge them."));
        _previewList = new ListView { SelectionMode = ListViewSelectionMode.Multiple, Height = 220,
            ItemsSource = preview.Candidates.Select(c => string.Join(" · ", c.Values.Where(v => v.Kind is "name" or "email" or "phone").Take(4).Select(v => v.Value))).ToArray() };
        AutomationProperties.SetName(_previewList, "Contacts to import");
        _previewList.SelectAll();
        _body.Children.Add(_previewList);
        var list = _previewList;
        var selection = Text($"{list.SelectedItems.Count} selected", 12);
        list.SelectionChanged += (_, _) => selection.Text = $"{list.SelectedItems.Count} selected";
        _body.Children.Add(selection);
        Action(_body, "Import selected contacts", async () =>
        {
            var indices = list.SelectedRanges.SelectMany(r => Enumerable.Range(r.FirstIndex, (int)r.Length)).ToArray();
            var selected = indices.Select(i => preview.Candidates[i]).ToArray();
            if (selected.Length == 0) throw new InvalidOperationException("Select at least one contact to import.");
            if (await _session.ChangeAsync(b => b.Import(preview.Source, selected, preview.Complete && selected.Length == preview.Candidates.Length), _stop.Token))
            { _preview = null; _status.Text = $"Imported {selected.Length} records. Possible duplicates are ready for review."; }
        }, preview.Candidates.Length > 0);
        Action(_body, "Cancel import", () => { _preview = null; return Task.CompletedTask; });
    }

    private void ShowPeople()
    {
        var book = _session.Book;
        var search = new TextBox { PlaceholderText = "Search contacts", Text = _query, MaxLength = 200 };
        AutomationProperties.SetName(search, "Search contacts by name, email or phone");
        _body.Children.Add(search);
        var results = new ListView { Height = 160, SelectionMode = ListViewSelectionMode.Single };
        AutomationProperties.SetName(results, "People in your address book");
        ContactPerson[] people = [];
        void Filter()
        {
            people = book.People.Where(p => ContactBook.Matches(book.Name(p), _query) ||
                book.For(p).Any(r => r.Current.Values.Any(v => v.Kind is "email" or "phone" && ContactBook.Matches(v.Value, _query))))
                .OrderBy(book.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            results.ItemsSource = people.Select(p => book.Name(p) + " · " + book.For(p).Length + " source records").ToArray();
            results.SelectedIndex = Array.FindIndex(people, p => p.Id == _selectedPerson);
        }
        search.TextChanged += (_, _) => { _query = search.Text.Trim(); Filter(); };
        var detail = new StackPanel { Spacing = 10 };
        results.SelectionChanged += (_, _) =>
        {
            if (results.SelectedIndex < 0 || results.SelectedIndex >= people.Length) { detail.Children.Clear(); return; }
            _selectedPerson = people[results.SelectedIndex].Id;
            RenderPerson(detail, people[results.SelectedIndex]);
        };
        Filter();
        _body.Children.Add(results);
        if (book.People.Length == 0) _body.Children.Add(Text("Your address book is empty. Connect a source or create a contact."));
        _body.Children.Add(detail);
        var create = Panel("New contact", "Create a contact in HABITAT.");
        var name = new TextBox { Header = "Name", MaxLength = 200 };
        var email = new TextBox { Header = "Email", MaxLength = 320 };
        var phone = new TextBox { Header = "Phone", MaxLength = 100 };
        foreach (var field in new[] { name, email, phone }) { AutomationProperties.SetName(field, field.Header.ToString()); create.Children.Add(field); }
        Action(create, "Create contact", async () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Enter a name for the contact.");
            if (email.Text.Length > 0 && !ContactChannels.IsEmail(email.Text.Trim())) throw new InvalidOperationException("Enter a valid email address.");
            var fields = new[] { new ContactValue("name", name.Text.Trim()), new("email", email.Text.Trim()), new("phone", phone.Text.Trim()) }.Where(v => v.Value.Length > 0).ToArray();
            var source = new ContactSource("local", "local", "HABITAT", "local", true);
            var candidate = new ContactCandidate(ContactBook.NewId(), fields, System.Text.Json.JsonSerializer.Serialize(fields));
            await _session.ChangeAsync(b => b.Import(source, [candidate]), _stop.Token);
        });
        _body.Children.Add(Card(create));
    }

    private void RenderPerson(StackPanel detail, ContactPerson person)
    {
        detail.Children.Clear();
        var book = _session.Book;
        detail.Children.Add(Profile(book, person));
        var alternatives = book.People.Where(p => p.Id != person.Id).OrderBy(book.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var target = new ComboBox { Header = "Merge with", PlaceholderText = "Choose another person", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = alternatives.Select(p => book.Name(p) + " · " + string.Join(", ", book.For(p).Select(r => book.Source(r).Label).Distinct())).ToArray() };
        AutomationProperties.SetName(target, "Choose a person to merge with");
        target.SelectionChanged += (_, _) => _mergeTarget = target.SelectedIndex >= 0 ? alternatives[target.SelectedIndex].Id : null;
        _mergeTarget = null;
        detail.Children.Add(target);
        Action(detail, "Review merge", () =>
        {
            if (_mergeTarget is null) throw new InvalidOperationException("Choose another person to review the merge.");
            _review = new(person.Id, _mergeTarget, "Manual merge");
            return Task.CompletedTask;
        }, alternatives.Length > 0);
        if (book.UndoableMerge(person.Id) is not null)
            Action(detail, "Undo last merge", async () => { await _session.ChangeAsync(b => b.UndoMerge(person.Id), _stop.Token); });
        foreach (var record in book.For(person).Where(r => r.Current.Values.Any(v => v.Kind == "name")))
            Action(detail, "Use name from " + book.Source(record).Label, async () => { await _session.ChangeAsync(b => b.PreferRecord(person.Id, record.Id), _stop.Token); });
    }

    private void ShowSuggestions()
    {
        var book = _session.Book;
        var suggestions = book.Suggestions();
        _body.Children.Add(Text("Suggestions use matching email addresses, phone numbers or Facebook profile links. A matching name alone is not enough. Nothing is merged automatically."));
        if (suggestions.Length == 0) _body.Children.Add(Text("No possible duplicates to review."));
        foreach (var suggestion in suggestions)
        {
            var panel = Panel(book.Name(book.People.Single(p => p.Id == suggestion.LeftId)) + " / " + book.Name(book.People.Single(p => p.Id == suggestion.RightId)), suggestion.Reason);
            Action(panel, "Review suggestion", () => { _review = suggestion; return Task.CompletedTask; });
            _body.Children.Add(Card(panel));
        }
    }

    private void ShowReview()
    {
        var book = _session.Book;
        var suggestion = _review!;
        var left = book.People.FirstOrDefault(p => p.Id == suggestion.LeftId);
        var right = book.People.FirstOrDefault(p => p.Id == suggestion.RightId);
        if (left is null || right is null) { _review = null; Refresh(); return; }
        _body.Children.Add(Text("Review merge", 18));
        _body.Children.Add(Text(suggestion.Reason + ". Review both people below. All fields and sources will be retained, and you can undo the merge."));
        var comparison = new Grid { ColumnSpacing = 12 };
        comparison.ColumnDefinitions.Add(new ColumnDefinition()); comparison.ColumnDefinitions.Add(new ColumnDefinition());
        comparison.Children.Add(Profile(book, left));
        var other = Profile(book, right); Grid.SetColumn(other, 1); comparison.Children.Add(other);
        _body.Children.Add(comparison);
        Action(_body, "Confirm merge", async () =>
        {
            if (await _session.ChangeAsync(b => b.Merge(left.Id, right.Id), _stop.Token))
            { _review = null; _selectedPerson = left.Id; _status.Text = "Contacts merged. All source data was kept."; }
        });
        Action(_body, "Keep separate", async () =>
        {
            if (await _session.ChangeAsync(b => b.Dismiss(left.Id, right.Id), _stop.Token))
            { _review = null; _status.Text = "Contacts kept separate. This suggestion will not be shown again."; }
        });
        Action(_body, "Back", () => { _review = null; return Task.CompletedTask; });
    }

    internal static StackPanel Profile(ContactBook book, ContactPerson person)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Text(book.Name(person), 18));
        foreach (var record in book.For(person))
        {
            var source = book.Source(record);
            var fields = Panel(source.Label, record.Missing ? "Not in the latest complete import · original data retained" : source.Account);
            foreach (var value in record.Current.Values.Where(v => v.Kind != "photo"))
                fields.Children.Add(Text(Label(value.Kind) + (value.Label.Length == 0 ? "" : " (" + value.Label + ")") + ": " + value.Value, 13));
            if (record.History.Length > 0)
            {
                var history = new StackPanel { Spacing = 8 };
                foreach (var value in record.History.SelectMany(s => s.Values).Distinct().Except(record.Current.Values).Where(v => v.Kind != "photo"))
                    history.Children.Add(Text(Label(value.Kind) + ": " + value.Value, 12));
                if (history.Children.Count == 0) history.Children.Add(Text("Earlier source versions were retained.", 12));
                fields.Children.Add(Disclosure("previous values", history));
            }
            var raw = new TextBox { Text = record.Current.Raw, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180 };
            AutomationProperties.SetName(raw, "Original contact source data");
            fields.Children.Add(Disclosure("original source data", raw));
            panel.Children.Add(Card(fields));
        }
        return panel;
    }

    private static string Label(string kind) => kind switch
    {
        "name" => "Name", "email" => "Email", "phone" => "Phone", "messenger" => "Messenger", "profile" => "Profile",
        "organization" => "Organization", "job" => "Job title", "address" => "Address", "note" => "Note", "birthday" => "Birthday",
        "url" => "Website", "nickname" => "Nickname", "im" => "Messaging address", _ => "Source field"
    };
    private static StackPanel Disclosure(string title, UIElement content)
    {
        var panel = new StackPanel { Spacing = 8 };
        var button = new Button { Content = "Show " + title };
        content.Visibility = Visibility.Collapsed;
        button.Click += (_, _) =>
        {
            var visible = content.Visibility == Visibility.Collapsed;
            content.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            button.Content = (visible ? "Hide " : "Show ") + title;
            AutomationProperties.SetName(button, button.Content.ToString());
        };
        AutomationProperties.SetName(button, "Show " + title);
        panel.Children.Add(button); panel.Children.Add(content); return panel;
    }
    private static TextBlock Text(string value, double size = 13) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private static StackPanel Panel(string title, string description)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Text(title, 15)); panel.Children.Add(Text(description, 12)); return panel;
    }
    private static Border Card(UIElement content) => new() { Child = content, Padding = new Thickness(12), CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 128, 128, 128)) };
}
