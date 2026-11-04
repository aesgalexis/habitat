using Habitat.Features.Contacts;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat.Features.Preferences;

internal sealed partial class ContactsPreferencesView
{
    private string[]? _confirmHideBrowserPeople;
    internal void SelectBrowserSuggestions() { _mode.SelectedIndex = 3; Refresh(); }
    internal void SelectSavedBrowserContacts() { _mode.SelectedIndex = 4; Refresh(); }

    private static string Describe(ContactCandidate candidate) => string.Join(" · ",
        candidate.Values.Where(v => v.Kind is "name" or "email" or "phone").Select(v => v.Value));

    private static string[] SelectedKeys(ListView list, string[] keys) => list.SelectedRanges
        .SelectMany(r => Enumerable.Range(r.FirstIndex, (int)r.Length)).Select(i => keys[i]).ToArray();

    private void ShowBrowserSuggestions()
    {
        var book = _session.Book;
        var suggestions = book.PendingBrowserSuggestions;
        _body.Children.Add(Text("Browser suggestions", 18));
        _body.Children.Add(Text("These addresses appeared in loaded Gmail or Google Contacts rows. They may include businesses and notifications. Nothing is added until you accept it. Ignored suggestions stay ignored after new reads and restarts."));
        Action(_body, "Reload suggestions", () => Task.CompletedTask);
        if (suggestions.Length == 0) { _body.Children.Add(Text("No browser suggestions to review.")); return; }
        var search = new TextBox { PlaceholderText = "Search suggestions", MaxLength = 200 };
        AutomationProperties.SetName(search, "Search browser suggestions by name, address or account");
        var list = new ListView { SelectionMode = ListViewSelectionMode.Multiple, Height = 220 };
        AutomationProperties.SetName(list, "Browser suggestions to review");
        BrowserContactSuggestion[] filtered = [];
        string Description(BrowserContactSuggestion s) => Describe(s.Candidate) + " · " + book.Sources.Single(source => source.Id == s.SourceId).Account;
        void Filter()
        {
            filtered = suggestions.Where(s => ContactBook.Matches(Description(s), search.Text.Trim())).ToArray();
            list.ItemsSource = filtered.Select(Description).ToArray();
        }
        search.TextChanged += (_, _) => Filter(); Filter();
        _body.Children.Add(search); _body.Children.Add(list);
        var selection = Text("0 selected", 12);
        list.SelectionChanged += (_, _) => selection.Text = $"{list.SelectedItems.Count} selected";
        _body.Children.Add(selection);
        async Task Review(bool accept)
        {
            var keys = SelectedKeys(list, filtered.Select(s => ContactBook.RecordId(s.SourceId, s.Candidate.RemoteId)).ToArray());
            if (await _session.ChangeAsync(b => b.ReviewBrowserSuggestions(keys, accept, filtered), _stop.Token))
                _status.Text = accept ? "Selected suggestions added. Possible duplicates are ready for review." : "Selected suggestions ignored. Saved contacts were kept.";
        }
        Action(_body, "Add selected contacts", () => Review(true));
        Action(_body, "Ignore selected suggestions", () => Review(false));
    }

    private void ShowSavedBrowserContacts()
    {
        var book = _session.Book;
        if (_confirmHideBrowserPeople is { } ids)
        {
            _body.Children.Add(Text("Review contacts to hide", 18));
            _body.Children.Add(Text("These contacts will leave the address book. All original fields and history will be kept. You can restore them here. No Google contacts will be changed."));
            foreach (var person in book.SavedBrowserPeople.Where(p => ids.Contains(p.Id))) _body.Children.Add(Profile(book, person));
            Action(_body, "Confirm hide", async () =>
            {
                if (await _session.ChangeAsync(b => b.ReviewSavedBrowserPeople(ids, hide: true), _stop.Token))
                { _confirmHideBrowserPeople = null; _status.Text = "Selected contacts hidden. Original data kept; restore them at any time."; }
            });
            Action(_body, "Cancel", () => { _confirmHideBrowserPeople = null; return Task.CompletedTask; });
            return;
        }
        var people = book.SavedBrowserPeople.OrderBy(p => p.BrowserReviewed).ThenBy(book.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        _body.Children.Add(Text("Review saved browser contacts", 18));
        _body.Children.Add(Text("Review contacts saved from partial Google browser observations, including earlier Gmail senders. Your current contacts stay available until you choose an action. Possible duplicates have a separate review; nothing is merged or deleted automatically."));
        Action(_body, "Review possible duplicates", () => { SelectDuplicates(); return Task.CompletedTask; });
        if (people.Length == 0) { _body.Children.Add(Text("No saved browser contacts to review.")); return; }
        var search = new TextBox { PlaceholderText = "Search saved browser contacts", MaxLength = 200 };
        AutomationProperties.SetName(search, "Search saved browser contacts by name or address");
        var list = new ListView { SelectionMode = ListViewSelectionMode.Multiple, Height = 220 };
        AutomationProperties.SetName(list, "Saved browser contacts to review");
        ContactPerson[] filtered = [];
        string Description(ContactPerson p) => book.Name(p) + " · " + string.Join(", ", book.For(p)
            .SelectMany(r => r.Current.Values).Where(v => v.Kind == "email").Select(v => v.Value).Distinct()) + " · " + book.BrowserReviewReason(p);
        void Filter()
        {
            filtered = people.Where(p => ContactBook.Matches(Description(p), search.Text.Trim())).ToArray();
            list.ItemsSource = filtered.Select(Description).ToArray();
        }
        search.TextChanged += (_, _) => Filter(); Filter();
        _body.Children.Add(search); _body.Children.Add(list);
        string[] Selection() => SelectedKeys(list, filtered.Select(p => p.Id).ToArray());
        Action(_body, "Keep or restore selected contacts", async () =>
        {
            if (await _session.ChangeAsync(b => b.ReviewSavedBrowserPeople(Selection(), hide: false), _stop.Token))
                _status.Text = "Selected contacts kept in your address book.";
        });
        Action(_body, "Review selected contacts to hide", () =>
        {
            var selected = Selection();
            if (selected.Length == 0) throw new InvalidOperationException("Select at least one saved browser contact.");
            _confirmHideBrowserPeople = selected;
            return Task.CompletedTask;
        });
    }
}
