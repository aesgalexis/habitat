using Habitat.Features.Contacts;
using Habitat.Services;

internal static class ContactDestinationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var book = ContactBook.Empty.SaveContact([new("name", "Amiga José"), new("phone", "+34 600 111 222"), new("email", "amiga@example.com")]);
        var person = book.People.Single();
        var session = new ContactDestinationSession();
        var phone = session.RegisterPhone(book, person, "+34 600 111 222");
        var email = session.RegisterEmail(book, person, "amiga@example.com", "sender@example.com");
        check(session.Choose(book, person, ["sender@example.com"], null) is { Channel.Kind: "email", Sender: "sender@example.com" },
            "The last valid channel and sender remain selected independently of controls.");
        var renamed = book.SaveContact([new("name", "Renamed ☕"), new("phone", "+34600111222"), new("email", "amiga@example.com")], person.Id);
        check(session.Reconcile(renamed).Length == 0 && session.Emails[email].Name == "Renamed ☕" && session.Phones[phone].Name == "Renamed ☕",
            "Renaming a contact preserves its destination IDs and updates every cached channel.");
        var withoutEmail = renamed.SaveContact([new("name", "Renamed ☕"), new("phone", "+34600111222")], person.Id);
        check(session.Reconcile(withoutEmail).SequenceEqual([email]) && !session.Contains(email) && session.Emails.ContainsKey(email),
            "An invalid recipient is disabled immediately while its metadata remains available for an in-flight provider to drain.");
        session.Forget(email);
        check(!session.Emails.ContainsKey(email) && session.Contains(phone), "Retirement releases only the obsolete channel.");

        var evictedPreference = new ContactDestinationSession();
        evictedPreference.Forget(evictedPreference.RegisterEmail(book, person, "amiga@example.com", "sender@example.com"));
        check(evictedPreference.Choose(book, person, ["sender@example.com"], null) is { Channel.Kind: "email" },
            "Evicting a view keeps its valid channel preference.");
        evictedPreference.Reconcile(withoutEmail);
        check(evictedPreference.Choose(book, person, ["sender@example.com"], null) is { Channel.Kind: "phone" },
            "A removed channel clears the preference even after its view was evicted.");

        var drafts = new ComposerDrafts();
        drafts.Isolate(phone); drafts.Edit(phone, "Unsent original 雪"); drafts.Retire(phone);
        check(drafts.Text(phone) == "Unsent original 雪" && drafts.SaveForRestart().Local[phone] == "Unsent original 雪",
            "Retiring views preserves original unsent contact text, including restart recovery.");
        drafts.Edit(phone, ""); drafts.Retire(phone);
        check(!drafts.SaveForRestart().Local.ContainsKey(phone), "Empty retired drafts release their local state.");

        var retained = new ContactDestinationSession();
        var ids = new List<string>();
        for (var index = 0; index < 45; index++)
        {
            var synthetic = ContactBook.Empty.SaveContact([new("name", "Synthetic " + index), new("phone", "+34600111" + index.ToString("D3"))]);
            ids.Add(retained.RegisterPhone(synthetic, synthetic.People.Single(), synthetic.Records.Single().Current.Values.Single(v => v.Kind == "phone").Value));
        }
        var selected = ids[^1];
        var evicted = retained.EvictionCandidates(selected, id => id == ids[0] || id == ids[1]);
        check(evicted.Length == 12 && !evicted.Contains(selected) && !evicted.Contains(ids[0]) && !evicted.Contains(ids[1]),
            "The contact view budget protects selection, unsent drafts and active/review-required deliveries.");
        foreach (var id in evicted) retained.Forget(id);
        check(retained.Phones.Count == ContactDestinationSession.MaximumInactiveViews + 1,
            "Unprotected inactive recipient views have a bounded session cache.");

        var browser = new BrowserContactsService();
        browser.DisableSources(); browser.Configure(new(true, true)); browser.RequestSetup("facebook");
        var owner = new object(); browser.Attach(owner, true);
        check(!browser.Settings.Allows("facebook") && !browser.Settings.Allows("google") && !browser.TryTake(out _),
            "An old enabled profile cannot override the desktop's fixed manual-contacts policy.");
    }
}
