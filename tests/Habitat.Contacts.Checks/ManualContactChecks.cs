using Habitat.Features.Contacts;
using Habitat.Services;

internal static class ManualContactChecks
{
    internal static async Task<int> RunAsync(string root)
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        void Reject(Action action)
        {
            try { action(); throw new Exception("An invalid or duplicate contact was accepted."); }
            catch (ContactReviewException) { checks++; }
        }
        var paths = AppDataPaths.Isolated(Path.Combine(root, "manual-destinations"));
        Directory.CreateDirectory(paths.DesktopDirectory);
        var legacy = Path.Combine(paths.DesktopDirectory, "contacts.json");
        await File.WriteAllTextAsync(legacy, "Legacy address book must remain untouched, even if unreadable.");
        var session = new ContactSession(new(paths.ContactsPath));
        await session.LoadAsync();
        Check(session.Loaded && session.Book.People.Length == 0 && !File.Exists(paths.ContactsPath),
            "Upgrading must start with zero destinations without reading or deleting the legacy address book.");
        Check(paths.ContactsPath.EndsWith("contact-destinations.json"), "Manual contacts must use independent persistent storage.");
        Reject(() => session.Book.SaveContact([]));
        Reject(() => session.Book.SaveContact([new("email", "bad email")]));
        Reject(() => session.Book.SaveContact([new("phone", "123")]));
        Reject(() => session.Book.SaveContact([new("messenger", "https://example.com/person")]));
        var fields = new ContactValue[] { new("name", "Amiga José ☕"), new("phone", "+34 600 111 222"),
            new("email", "amiga@example.com"), new("note", "Dato original 雪", "Personal") };
        Check(await session.ChangeAsync(b => b.SaveContact(fields)), "Explicit creation must save the contact.");
        var id = session.Book.People.Single().Id;
        var recordId = session.Book.Records.Single().Id;
        Check(session.Book.ListedPeople.Count() == 1 && ContactChannels.For(session.Book, session.Book.People[0]).Length == 2,
            "A person's phone and email must create one card with multiple channels.");
        Reject(() => session.Book.SaveContact([new("name", "Another name"), new("email", " AMIGA@EXAMPLE.COM ")]));
        Reject(() => session.Book.SaveContact([new("phone", "0034 (600) 111-222")]));
        Check(await session.ChangeAsync(b => b.SaveContact(fields.Select(v => v.Kind == "name" ? v with { Value = "Nombre nuevo" } : v).ToArray(), id)),
            "Editing an existing person must be supported.");
        Check(session.Book.People.Single().Id == id && session.Book.Records.Single().Id == recordId &&
            session.Book.Records.Single().History.Length == 1 && session.Book.Records.Single().Current.Values.Contains(fields[3]),
            "Editing must preserve destination identity, custom details and earlier values without a duplicate.");
        var restarted = new ContactSession(new(paths.ContactsPath));
        await restarted.LoadAsync();
        Check(restarted.Loaded && restarted.Book.People.Single().Id == id && restarted.Book.Name(restarted.Book.People[0]) == "Nombre nuevo",
            "Only manually added contacts must survive a restart.");
        Check(await File.ReadAllTextAsync(legacy) == "Legacy address book must remain untouched, even if unreadable.",
            "Creating manual destinations must leave legacy data intact.");
        Reject(() => restarted.Book.SaveContact([new("email", "amiga@example.com")]));
        var two = restarted.Book.SaveContact([new("name", "Nombre nuevo"), new("email", "different@example.com")]);
        Check(two.People.Length == 2, "Different people with the same name must remain distinct.");
        Reject(() => two.SaveContact([new("email", "DIFFERENT@example.com")], id));
        var nameOnly = ContactBook.Empty.SaveContact([new("name", "Solo nombre")]);
        Check(nameOnly.ListedPeople.Count() == 1 && ContactChannels.For(nameOnly, nameOnly.People[0]).Length == 0,
            "A name alone must be a selectable contact even without a sending channel.");
        Reject(() => nameOnly.SaveContact([new("name", "solo nombre")]));
        var emailOnly = ContactBook.Empty.SaveContact([new("email", "noreply@example.com")]);
        Check(emailOnly.ListedPeople.Count() == 1 && ContactChannels.For(emailOnly, emailOnly.People[0]).Length == 1,
            "Explicit user-entered addresses must not be silently filtered as automatic suggestions.");
        var messenger = ContactBook.Empty.SaveContact([new("messenger", "https://www.facebook.com/manual.friend")]);
        Reject(() => messenger.SaveContact([new("messenger", "https://facebook.com/Manual.Friend/")]));
        Check(await restarted.ChangeAsync(b => b.RemoveContact(id)), "Explicit removal must save.");
        await restarted.LoadAsync();
        Check(restarted.Book.People.Length == 0 && restarted.Book.Records.Length == 0,
            "Removing the last manual contact must persist an empty address book.");
        Check(await restarted.ChangeAsync(b => b.SaveContact([new("phone", "0034600111222")])),
            "A removed contact must not reserve its number forever.");
        Check(restarted.Book.People.Single().Id != id, "Adding a new contact must create its own identity and drafts.");
        return checks;
    }
}
