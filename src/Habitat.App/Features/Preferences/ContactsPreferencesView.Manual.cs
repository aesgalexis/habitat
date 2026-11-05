using Habitat.Features.Contacts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat.Features.Preferences;

internal sealed partial class ContactsPreferencesView
{
    private bool _contactEditorOpen, _confirmContactRemoval;
    private string? _editingContact;
    private readonly List<ContactValue> _contactEditValues = new();
    private readonly List<(TextBox Input, ComboBox? Kind, TextBox? Label)> _contactFormRows = new();
    private static readonly (string Kind, string Label)[] ContactFields =
    [
        ("name", "Name"), ("phone", "Phone"), ("email", "Email"), ("messenger", "Messenger profile"),
        ("organization", "Organization"), ("url", "Website"), ("address", "Address"),
        ("birthday", "Birthday"), ("note", "Notes"), ("other", "Other")
    ];

    internal void NewContact() => BeginContactEdit(null);

    private void CaptureContactForm()
    {
        if (!_contactEditorOpen || _contactFormRows.Count != _contactEditValues.Count) return;
        for (var i = 0; i < _contactFormRows.Count; i++)
        {
            var row = _contactFormRows[i];
            _contactEditValues[i] = _contactEditValues[i] with
            {
                Value = row.Input.Text,
                Kind = row.Kind is { SelectedIndex: >= 0 } kind ? ContactFields[kind.SelectedIndex].Kind : _contactEditValues[i].Kind,
                Label = row.Label?.Text ?? _contactEditValues[i].Label
            };
        }
    }

    private void BeginContactEdit(string? personId)
    {
        _editingContact = personId; _contactEditorOpen = true; _confirmContactRemoval = false;
        _status.Text = "";
        _contactEditValues.Clear();
        var person = _session.Book.People.FirstOrDefault(p => p.Id == personId);
        var values = person is null ? [] : _session.Book.For(person).SelectMany(r => r.Current.Values).ToList();
        foreach (var kind in new[] { "name", "phone", "email" })
        {
            var value = values.FirstOrDefault(v => v.Kind == kind);
            _contactEditValues.Add(value ?? new(kind, ""));
            if (value is not null) values.Remove(value);
        }
        _contactEditValues.AddRange(values);
        Refresh();
    }

    private void ShowManualContacts()
    {
        if (_contactEditorOpen) { ShowContactEditor(); return; }
        var book = _session.Book;
        _body.Children.Add(Text($"{book.ListedPeople.Count()} contacts", 15));
        Action(_body, "Add contact", () => { NewContact(); return Task.CompletedTask; });
        ShowDriveContactsSync();
        if (!book.ListedPeople.Any())
        {
            _body.Children.Add(Text("No contacts yet. Add your first contact to use it as a destination."));
            return;
        }
        var search = new TextBox { PlaceholderText = "Search contacts", Text = _query, MaxLength = 200 };
        AutomationProperties.SetName(search, "Search contacts by name, email or phone");
        var results = new StackPanel { Spacing = 8 };
        void Filter()
        {
            results.Children.Clear();
            foreach (var person in book.ListedPeople.Where(p => ContactBook.Matches(book.Name(p), _query) ||
                book.For(p).Any(r => r.Current.Values.Any(v => ContactBook.Matches(v.Value, _query))))
                .OrderBy(book.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var details = book.For(person).SelectMany(r => r.Current.Values).Where(v => v.Kind is "phone" or "email").Select(v => v.Value);
                var card = Panel(book.Name(person), string.Join(" · ", details));
                Action(card, "Edit contact", () => { BeginContactEdit(person.Id); return Task.CompletedTask; });
                results.Children.Add(Card(card));
            }
            if (results.Children.Count == 0) results.Children.Add(Text("No contacts found."));
        }
        search.TextChanged += (_, _) => { _query = search.Text.Trim(); Filter(); };
        _body.Children.Add(search); _body.Children.Add(results); Filter();
    }

    private void ShowDriveContactsSync()
    {
        if (_drive is null) return;
        var panel = Panel("Google Drive", _drive.HasLink
            ? "Sync your manually created contacts with HABITAT Contacts.csv in My Drive."
            : "Connect to sync your manually created contacts, including the ones on this PC, with a readable CSV in My Drive.");
        if (_drive.Email is { } email) panel.Children.Add(Text(email));
        if (_drive.Status is { } message)
        {
            var status = Text(message);
            AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            panel.Children.Add(status);
        }
        if (!_drive.Connected)
        {
            Action(panel, _drive.HasLink ? "Reconnect Google Drive" : "Connect Google Drive",
                () => _drive.ConnectAsync(_stop.Token), _drive.CanConnect && !_drive.Busy);
            if (!_drive.CanConnect)
                panel.Children.Add(Text("This build needs a Google Drive sign-in configuration."));

            if (_pickDriveClient is { } pickClient)
            {
                var advanced = new StackPanel { Spacing = 8 };
                advanced.Children.Add(Text("Use the same custom Google OAuth client if you already sync contacts with one."));
                Action(advanced, "Choose custom OAuth client…", () => _drive.ConnectAsync(_stop.Token, pickClient), !_drive.Busy);
                panel.Children.Add(new Expander { Header = "Advanced setup", Content = advanced });
            }
        }
        else
        {
            Action(panel, "Sync now", () => _drive.SyncAsync(_stop.Token), !_drive.Busy);
            if (_drive.HasConflict)
            {
                Action(panel, "Keep local conflicting contacts", () => _drive.SyncAsync(_stop.Token, ContactDriveConflictChoice.Local), !_drive.Busy);
                Action(panel, "Keep Drive conflicting contacts", () => _drive.SyncAsync(_stop.Token, ContactDriveConflictChoice.Drive), !_drive.Busy);
            }
            Action(panel, "Disconnect Google Drive", () => _drive.DisconnectAsync(_stop.Token), !_drive.Busy);
        }
        _body.Children.Add(Card(panel));
    }

    private void ShowContactEditor()
    {
        _contactFormRows.Clear();
        _body.Children.Add(Text(_editingContact is null ? "New contact" : "Edit contact", 18));
        for (var index = 0; index < _contactEditValues.Count; index++)
        {
            var position = index;
            var value = _contactEditValues[position];
            var row = new StackPanel { Spacing = 6 };
            var label = ContactFields.FirstOrDefault(f => f.Kind == value.Kind).Label ?? "Other";
            var input = new TextBox { Header = label, Text = value.Value, MaxLength = 4000,
                AcceptsReturn = value.Kind == "note", TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetName(input, label);
            ComboBox? detailKind = null;
            TextBox? detailLabel = null;
            if (index >= 3)
            {
                var kind = new ComboBox { ItemsSource = ContactFields.Select(f => f.Label).ToArray(),
                    SelectedIndex = Math.Max(0, Array.FindIndex(ContactFields, f => f.Kind == value.Kind)),
                    HorizontalAlignment = HorizontalAlignment.Stretch };
                AutomationProperties.SetName(kind, "Contact detail type");
                kind.SelectionChanged += (_, _) =>
                {
                    if (kind.SelectedIndex < 0) return;
                    var selected = ContactFields[kind.SelectedIndex];
                    input.Header = selected.Label;
                    input.AcceptsReturn = selected.Kind == "note";
                    AutomationProperties.SetName(input, selected.Label);
                };
                row.Children.Add(kind);
                detailKind = kind;
            }
            row.Children.Add(input);
            if (index >= 3)
            {
                var customLabel = new TextBox { Header = "Label (optional)", Text = value.Label, MaxLength = 200 };
                AutomationProperties.SetName(customLabel, "Contact detail label");
                row.Children.Add(customLabel);
                detailLabel = customLabel;
            }
            _contactFormRows.Add((input, detailKind, detailLabel));
            _body.Children.Add(row);
        }
        Action(_body, "Add detail", () => { _contactEditValues.Add(new("note", "")); return Task.CompletedTask; });
        Action(_body, _editingContact is null ? "Add contact" : "Save contact", async () =>
        {
            if (await _session.ChangeAsync(b => b.SaveContact(_contactEditValues, _editingContact), _stop.Token))
            { _contactEditorOpen = false; _status.Text = _editingContact is null ? "Contact added." : "Contact saved."; }
        });
        Action(_body, "Cancel", () => { _contactEditorOpen = false; _confirmContactRemoval = false; return Task.CompletedTask; });
        if (_editingContact is null) return;
        if (!_confirmContactRemoval)
            Action(_body, "Remove contact", () => { _confirmContactRemoval = true; return Task.CompletedTask; });
        else
        {
            _body.Children.Add(Text("Remove this contact from HABITAT?"));
            Action(_body, "Confirm remove", async () =>
            {
                if (await _session.ChangeAsync(b => b.RemoveContact(_editingContact), _stop.Token))
                { _contactEditorOpen = false; _confirmContactRemoval = false; _status.Text = "Contact removed."; }
            });
            Action(_body, "Keep contact", () => { _confirmContactRemoval = false; return Task.CompletedTask; });
        }
    }
}
