using Habitat.Features.Contacts;
using Habitat.Models;
using Habitat.Services;
using System.Net;
using System.Text;
using System.Text.Json;

var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
async Task Reject<T>(Func<Task> action, string message) where T : Exception
{
    try { await action(); throw new Exception(message); } catch (T) { checks++; }
}
ContactCandidate Candidate(string id, string name, string email, string phone = "") => new(id,
    new[] { new ContactValue("name", name), new("email", email), new("phone", phone) }.Where(v => v.Value.Length > 0).ToArray(),
    JsonSerializer.Serialize(new { id, name, email, phone }));
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "isolated-contacts", Guid.NewGuid().ToString("N")));
if (!root.Contains(Path.DirectorySeparatorChar + "artifacts" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Contact checks require an output directory inside artifacts.");
Directory.CreateDirectory(root);
var path = Path.Combine(root, "contacts.json");
var store = new ContactStore(path);
var book = await store.LoadAsync();
Check(book.People.Length == 0 && book.Sources.Length == 0 && !File.Exists(path), "Fresh storage must stay empty without creating or reading sources.");
var unreadable = Path.Combine(root, "directory-instead-of-file");
Directory.CreateDirectory(unreadable);
await Reject<UnauthorizedAccessException>(() => new ContactStore(unreadable).LoadAsync(), "An unreadable contacts path must not masquerade as an empty address book.");
var phone = new ContactSource(ContactBook.NewId(), "vcard", "Personal phone", "Personal phone", false);
var google = new ContactSource(ContactBook.NewId(), "google", "Google · one@example.com", "one@example.com", true);
var secondGoogle = new ContactSource(ContactBook.NewId(), "google", "Google · two@example.com", "two@example.com", true);
var a = Candidate("phone-ana", "Ana ☕", "ana@example.com", "+34 612 345 678");
var b = Candidate("people/ana", "Ana García", "ANA@example.com", "0034612345678");
book = book.Import(phone, [a]).Import(google, [b]);
Check(book.People.Length == 2 && book.Sources.Length == 2 && book.Records.Length == 2, "Imports from different sources must stay separate until reviewed.");
var suggestion = book.Suggestions().Single();
Check(suggestion.Reason is "Same email address" or "Same phone number", "Strong identifiers must produce a reviewable suggestion.");
Check(book.Import(phone, [a]).People.Length == 2, "Repeating a file import must not duplicate source records.");
Check(book.Import(phone, [a]).Records.All(r => r.History.Length == 0), "Unchanged imports must not inflate history.");
var left = book.People.Single(p => book.For(p).Single().SourceId == phone.Id);
var right = book.People.Single(p => book.For(p).Single().SourceId == google.Id);
var separate = book.Dismiss(left.Id, right.Id);
Check(separate.Suggestions().Length == 0, "Rejected suggestions must be remembered.");
Check(separate.Import(google, [b with { Raw = b.Raw + " " }]).Suggestions().Length == 0, "Reloading an account must not resurrect rejected pairs.");
var merged = book.Merge(left.Id, right.Id);
Check(merged.People.Length == 1 && merged.Records.Length == 2 && merged.For(merged.People[0]).Length == 2, "Merge must only link records, preserving both sources.");
Check(merged.Suggestions().Length == 0, "Merged people must not be suggested again.");
Check(merged.Name(merged.People[0]) == "Ana ☕", "Merge must retain the first person's name until a preference is chosen.");
var chosen = merged.PreferRecord(left.Id, right.RecordIds[0]);
Check(chosen.Name(chosen.People[0]) == "Ana García", "A preferred source name must be selectable.");
var updated = Candidate("people/ana", "Ana García Updated", "new@example.com", "+34 612 345 678");
merged = merged.Import(google, [updated]);
Check(merged.People.Length == 1 && merged.Records.Single(r => r.SourceId == google.Id).History[0].Values.Contains(new("email", "ANA@example.com")), "Updates must preserve historical values and confirmed groupings.");
var undone = merged.UndoMerge(left.Id);
Check(undone.People.Length == 2 && undone.Records.Single(r => r.SourceId == google.Id).Current.Values.Contains(new("email", "new@example.com")), "Undo must restore groupings without rolling back later source updates.");
merged = merged.Import(secondGoogle, [Candidate("people/ana-2", "Ana Work", "work@example.com")]);
var third = merged.People.Single(p => p.Id != left.Id);
merged = merged.Merge(left.Id, third.Id);
Check(merged.People.Single().RecordIds.Length == 3, "Nested merges must keep every source record.");
merged = merged.UndoMerge(left.Id).UndoMerge(left.Id);
Check(merged.People.Length == 3 && merged.Records.Length == 3, "Nested merges must be undoable in reverse order.");
var missing = book.Import(phone, [Candidate("other", "Other person", "other@example.com")], true);
Check(missing.Records.Single(r => r.RemoteId == a.RemoteId).Missing && missing.Records.Length == 3, "Records absent from a complete import must be retained and marked missing.");
var partial = book.Import(phone, [Candidate("other", "Other person", "other@example.com")]);
Check(!partial.Records.Single(r => r.RemoteId == a.RemoteId).Missing, "A partial import must not mark unseen records as removed.");
var sameName = ContactBook.Empty.Import(phone, [Candidate("a", "Same name", "one@example.com"), Candidate("b", "Same name", "two@example.com")]);
Check(sameName.Suggestions().Length == 0, "A name alone must never establish identity.");
Check(ContactBook.NormalizePhone("0034 (612) 345-678") == ContactBook.NormalizePhone("+34 612345678"), "International prefix variants should match.");
Check(ContactBook.NormalizePhone("612345678") != ContactBook.NormalizePhone("+34612345678"), "Never infer missing country codes.");
Check(ContactBook.NormalizePhone("612345678 x2") == "" && ContactBook.NormalizePhone("612+345678") == "", "Extensions and malformed numbers must not create identity matches.");
await Reject<InvalidOperationException>(() => Task.FromResult(book.Merge(left.Id, left.Id)), "A person cannot be merged with itself.");
await Reject<InvalidOperationException>(() => Task.FromResult(book.WithSource(google with { Id = ContactBook.NewId() })), "The same account cannot become another source.");

var cardText = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:person-1\r\nFN:Amiga José ☕\r\nN:José;Amiga;;;\r\nEMAIL;TYPE=HOME:amiga@example.com\r\nTEL;TYPE=CELL:+34 600 111 222\r\nNOTE:First line\\nSecond\\, line\r\n continued\r\nitem1.URL:https://example.com/person\r\nitem1.X-ABLabel:_$!<Home>!$_\r\nX-CUSTOM:Original source data\r\nEND:VCARD\r\n";
var cards = VCardContacts.Parse(cardText);
Check(cards.Single().RemoteId == "uid:person-1" && cards[0].Values.Contains(new("name", "Amiga José ☕")), "vCard UID and original Unicode names must survive.");
Check(cards[0].Values.Contains(new("note", "First line\nSecond, linecontinued")), "Unfolding and escaped commas/newlines must work.");
Check(cards[0].Values.Contains(new("url", "https://example.com/person", "Home")), "Grouped Apple labels must retain field association.");
Check(cards[0].Raw.Contains("X-CUSTOM:Original source data", StringComparison.Ordinal) && cards[0].Values.Any(v => v.Kind == "vcard"), "Unknown vCard fields must be retained in values and raw source.");
Check(VCardContacts.Parse(cardText + cardText).Length == 1, "Identical repeated vCards must not create duplicate records.");
var qp = VCardContacts.Parse("BEGIN:VCARD\nVERSION:2.1\nFN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:Jos=C3=\n=A9\nEND:VCARD");
Check(qp[0].Values.Single(v => v.Kind == "name").Value == "José", "Quoted-printable soft breaks and Unicode must decode correctly.");
var foldedQp = VCardContacts.Parse("BEGIN:VCARD\nVERSION:2.1\nFN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:Jos=C3=\n =A9 😀\nEND:VCARD");
Check(foldedQp[0].Values.Single(v => v.Kind == "name").Value == "José 😀", "Quoted-printable folding must preserve unencoded surrogate pairs too.");
var structured = VCardContacts.Parse("BEGIN:VCARD\nVERSION:4.0\nN:Family;Given;;;\nTEL;VALUE=uri:tel:+34600111222\nEND:VCARD");
Check(structured[0].Values.Contains(new("name", "Given Family")) && structured[0].Values.Contains(new("phone", "+34600111222")), "Structured names and URI phones must parse.");
await Reject<InvalidDataException>(() => Task.FromResult(VCardContacts.Parse(cardText + cardText.Replace("Amiga José ☕", "Different person", StringComparison.Ordinal))), "Conflicting UIDs must reject the whole import.");
await Reject<InvalidDataException>(() => Task.FromResult(VCardContacts.Parse("BEGIN:VCARD\nFN:unfinished")), "Incomplete files must not cause partial imports.");
await Reject<InvalidDataException>(() => Task.FromResult(VCardContacts.Parse("not a vCard")), "Invalid files must be rejected.");
var cardPath = Path.Combine(root, "phone.vcf");
await File.WriteAllTextAsync(cardPath, cardText, Encoding.UTF8);
Check((await VCardContacts.ReadAsync(cardPath, default)).Single().Values.Contains(new("name", "Amiga José ☕")), "File import must support UTF-8 with BOM.");

book = await store.SaveAsync(book, 0);
Check(File.Exists(Path.Combine(path + ".history", "000000000001.json")), "The very first saved agenda must have its own recovery copy.");
var reopened = await new ContactStore(path).LoadAsync();
Check(reopened.People.Length == 2 && reopened.Revision == 1 && reopened.Sources.Length == 2, "Contacts and provenance must survive process restarts.");
var recoverPath = Path.Combine(root, "recovery-check.json");
await new ContactStore(recoverPath).SaveAsync(book, 0);
File.Move(recoverPath, recoverPath + ".preserved");
await Reject<InvalidDataException>(() => new ContactStore(recoverPath).LoadAsync(), "A missing main file with a recovery copy must never be treated as an empty agenda.");
book = await store.SaveAsync(book.Merge(left.Id, right.Id), book.Revision);
Check(Directory.EnumerateFiles(path + ".history", "*.json").Count() == 2, "Every committed revision must have a recovery copy, including the previous address book.");
await Reject<InvalidOperationException>(() => new ContactStore(path).SaveAsync(reopened, reopened.Revision), "Stale sessions must not overwrite newer changes.");
var afterConflict = await store.LoadAsync();
Check(afterConflict.People.Length == 1 && afterConflict.Revision == 2, "A rejected stale save must preserve the committed merge.");
var session = new ContactSession(new ContactStore(path));
await session.LoadAsync();
Check(session.Loaded && session.Error is null, "Session loads saved data.");
Check(await session.ChangeAsync(bk => bk.UndoMerge(left.Id)) && session.Book.People.Length == 2, "Session supports persisted undo.");
await store.SaveAsync(session.Book, session.Book.Revision);
Check(!await session.ChangeAsync(bk => bk.Merge(left.Id, right.Id)) && session.Error is not null, "A session conflict must block further writes until reload.");
await session.LoadAsync();
Check(session.Error is null && session.Loaded, "Explicit reload must recover from a stale revision.");
await File.WriteAllTextAsync(path, "{bad-json");
await session.LoadAsync();
Check(!session.Loaded && session.Error is not null && session.Book.People.Length == 2, "Corrupt storage must retain the last loaded snapshot and block writes.");
Check(!await session.ChangeAsync(bk => ContactBook.Empty) && await File.ReadAllTextAsync(path) == "{bad-json", "Corrupt storage must never be replaced by an empty address book.");
await Reject<InvalidDataException>(() => Task.Run(() => ContactStore.Validate(ContactBook.Empty with { Records = [book.Records[0]] })), "Orphaned source records must fail validation.");

var contactBook = ContactBook.Empty.Import(phone, cards);
var contact = contactBook.People.Single();
var channels = ContactChannels.For(contactBook, contact);
Check(channels.Length == 1 && channels[0].Kind == "email", "Phone numbers and profiles must not invent iMessage or other channel availability.");
var fb = new ContactSource(ContactBook.NewId(), "facebook", "Facebook · personal", "personal", false);
contactBook = contactBook.Import(fb, [new("fb/1", [new("name", "Friend"), new("messenger", "https://www.facebook.com/messages/t/123")], "snapshot")]);
Check(ContactChannels.For(contactBook, contactBook.People.Last()).Single().Kind == "messenger", "Verified Facebook links may provide the existing Messenger route.");
var uri = ContactChannels.GmailDraft("amiga@example.com", "sender@example.com", "Body & subject=should-stay-body\n☕");
Check(uri.Host == "mail.google.com" && uri.Query.Contains("authuser=sender%40example.com", StringComparison.Ordinal) &&
    uri.Query.Contains("%26%20subject%3D", StringComparison.Ordinal), "Email drafts must encode the selected sender, recipient and editor body without parameter injection.");
Check(!ContactChannels.IsEmail("bad\r\nBcc:other@example.com"), "Contact addresses must reject header injection.");
await Reject<InvalidDataException>(() => Task.FromResult(ContactChannels.GmailDraft("invalid", null)), "Invalid email channels must not launch.");

var authUrl = GmailAuthService.AuthorizationUrl("client", "http://127.0.0.1:1/oauth/callback", "state", "challenge", contactsOnly: true);
var scope = Uri.UnescapeDataString(new Uri(authUrl).Query);
Check(scope.Contains(GmailAuthService.ContactsScope, StringComparison.Ordinal) && scope.Contains(GmailAuthService.EmailScope, StringComparison.Ordinal) &&
    !scope.Contains("gmail.", StringComparison.Ordinal), "Contacts OAuth must request read-only contacts and account identity without Gmail access.");
var credentials = new Credentials(new(new("synthetic.apps.googleusercontent.com", "secret"), new("synthetic-token", "refresh", DateTimeOffset.UtcNow.AddHours(1)), "one@example.com"));
using var auth = new GmailAuthService(credentials, contactsOnly: true);
var requests = new List<Uri>();
using var client = new HttpClient(new Handler(request =>
{
    Check(request.Method == HttpMethod.Get && request.Headers.Authorization?.Parameter == "synthetic-token", "Google contacts must use authenticated reads only.");
    requests.Add(request.RequestUri!);
    return requests.Count == 1 ? Response("{\"connections\":[{\"resourceName\":\"people/1\",\"names\":[{\"displayName\":\"User 雪\"}],\"emailAddresses\":[{\"value\":\"first@example.com\",\"type\":\"home\"}],\"customFutureField\":{\"keep\":true}}],\"nextPageToken\":\"page & two\"}")
        : Response("{\"connections\":[{\"resourceName\":\"people/2\",\"phoneNumbers\":[{\"value\":\"+34600111222\"}]}]}");
}));
using var api = new GoogleContactsService(auth, client);
var fetched = await api.ReadAsync(default);
Check(fetched.Length == 2 && fetched[0].Values.Contains(new("name", "User 雪")), "Google must read all pages and preserve original names.");
Check(requests[1].Query.Contains("pageToken=page%20%26%20two", StringComparison.Ordinal) && fetched[0].Raw.Contains("customFutureField", StringComparison.Ordinal), "Pagination tokens must be encoded and original Google records retained.");
using var deniedClient = new HttpClient(new Handler(_ => Response("{}", HttpStatusCode.Forbidden)));
using var denied = new GoogleContactsService(auth, deniedClient);
await Reject<InvalidOperationException>(() => denied.ReadAsync(default), "Missing People API or consent must produce actionable recovery.");
using var brokenClient = new HttpClient(new Handler(_ => Response("{\"nextPageToken\":\"same\"}")));
using var broken = new GoogleContactsService(auth, brokenClient);
await Reject<InvalidDataException>(() => broken.ReadAsync(default), "Repeated page tokens must not loop or commit partial data.");
using var cancel = new CancellationTokenSource(); cancel.Cancel();
await Reject<OperationCanceledException>(() => store.LoadAsync(cancel.Token), "Storage work must honor cancellation before starting.");
var browser = new BrowserContactsService();
var owner = new object();
browser.Attach(owner, true);
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
void Push(BrowserContactSnapshot snapshot, object? sender = null)
{
    using var packet = JsonDocument.Parse(JsonSerializer.Serialize(new { snapshots = new[] { snapshot } }, jsonOptions));
    browser.Accept(sender ?? owner, packet.RootElement, jsonOptions);
}
var fbSnapshot = new BrowserContactSnapshot("facebook", "https://www.facebook.com/account.owner", "Owner",
    [new(null, "Friend ☕", ProfileUrl: "https://www.facebook.com/friend.person", PhotoUrl: "https://scontent.fbcdn.net/f.jpg", Page: "Facebook")]);
Push(fbSnapshot);
Check(browser.TryTake(out var auto) && auto?.Provider == "facebook" && auto.Contacts[0].Name == "Friend ☕", "An authenticated opted-in browser observation is queued for automatic saving.");
var autoBook = BrowserContactsService.Apply(ContactBook.Empty, auto!);
Check(autoBook.People.Length == 1 && autoBook.Sources.Single().Account == fbSnapshot.AccountId, "Automatic Facebook reads populate the agenda with actual account provenance when available.");
Check(ReferenceEquals(BrowserContactsService.Apply(autoBook, fbSnapshot), autoBook), "Unchanged automatic observations must be no-ops, avoiding duplicate records and recovery files.");
var changedFb = fbSnapshot with { Contacts = [fbSnapshot.Contacts[0] with { Name = "Updated friend" }] };
var autoChanged = BrowserContactsService.Apply(autoBook, changedFb);
Check(autoChanged.People.Length == 1 && autoChanged.Records.Single().History.Length == 1, "Automatic updates preserve source identity and prior values.");
var anotherFbAccount = fbSnapshot with { AccountId = "https://www.facebook.com/another.owner", AccountName = "Another account" };
var sameProfile = BrowserContactsService.Apply(autoBook, anotherFbAccount);
Check(sameProfile.People.Length == 1 && sameProfile.Records.Length == 2 && sameProfile.Sources.Length == 2 && sameProfile.Merges.Length == 1,
    "Copies of the exact same Facebook profile must appear once while retaining both original accounts and an undoable grouping.");
sameProfile = sameProfile.UndoMerge(sameProfile.People[0].Id);
Check(sameProfile.People.Length == 2 && BrowserContactsService.Apply(sameProfile, anotherFbAccount).People.Length == 2 && sameProfile.Suggestions().Length == 0,
    "Undoing an exact-profile grouping must keep the copies separate across later automatic scans.");
var oldFacebook = new ContactSource(ContactBook.NewId(), "facebook", "Previous Facebook import", "Previous import", false);
var oldCandidate = new ContactCandidate(fbSnapshot.Contacts[0].ProfileUrl!,
    [new("name", "Previous friend name"), new("profile", fbSnapshot.Contacts[0].ProfileUrl!), new("messenger", fbSnapshot.Contacts[0].ProfileUrl!)], "Original import data");
var legacyBook = ContactBook.Empty.Import(oldFacebook, [oldCandidate]);
var repairedImport = BrowserContactsService.Apply(legacyBook, fbSnapshot);
Check(repairedImport.People.Length == 1 && repairedImport.Records.Length == 2 && repairedImport.Records.Any(r => r.Current.Raw == "Original import data"),
    "An automatic scan after a manual import must preserve the original data without displaying the same profile twice.");
Check(ContactChannels.For(repairedImport, repairedImport.People[0]).Single().SourceId == BrowserContactsService.Source(fbSnapshot).Id,
    "The direct Messenger action must use the active observed identity rather than an older disconnected import.");
var variant = fbSnapshot with { Contacts = [fbSnapshot.Contacts[0] with { ProfileUrl = "https://facebook.com/FRIEND.PERSON/" }] };
var canonical = BrowserContactsService.Apply(legacyBook, variant);
Check(canonical.People.Length == 1 && ContactChannels.For(canonical, canonical.People[0]).Length == 1,
    "Equivalent Facebook host, case and trailing-slash variants must not duplicate people or Messenger actions.");
var duplicateBook = legacyBook.Import(BrowserContactsService.Source(fbSnapshot), [oldCandidate]);
var duplicatePath = Path.Combine(root, "previous-duplicates.json");
var duplicateStore = new ContactStore(duplicatePath);
await duplicateStore.SaveAsync(duplicateBook, 0);
var migratedSession = new ContactSession(duplicateStore);
await migratedSession.LoadAsync();
Check(migratedSession.Loaded && migratedSession.Error is null && migratedSession.Book.People.Length == 1 && migratedSession.Book.Records.Length == 2 && migratedSession.Book.Revision == 2,
    "Loading a previous duplicate agenda must repair and durably save its exact Facebook profile groups.");
Check((await new ContactStore(Path.Combine(duplicatePath + ".history", "000000000001.json")).LoadAsync()).People.Length == 2,
    "Repairing existing duplicates must retain the complete original agenda in recovery history.");
await migratedSession.LoadAsync();
Check(migratedSession.Book.Revision == 2, "Reloading a repaired agenda must not create more revisions or merges.");
await migratedSession.ChangeAsync(bk => bk.UndoMerge(bk.People[0].Id));
await migratedSession.LoadAsync();
Check(migratedSession.Book.People.Length == 2 && migratedSession.Book.Revision == 3,
    "Explicitly undone duplicate repairs must survive storage and restart.");
var dismissedCopies = duplicateBook.Dismiss(duplicateBook.People[0].Id, duplicateBook.People[1].Id);
Check(ReferenceEquals(dismissedCopies, dismissedCopies.DeduplicateFacebookProfiles()),
    "An earlier Keep separate decision must take priority over automatic duplicate repair.");
var threadCopies = ContactBook.Empty.Import(oldFacebook, [oldCandidate with { Values = [new("name", "Friend"), new("messenger", "https://www.facebook.com/messages/t/thread.one")] }])
    .Import(BrowserContactsService.Source(fbSnapshot), [oldCandidate with { Values = [new("name", "Friend"), new("messenger", "https://www.facebook.com/messages/t/thread.one")] }]);
Check(ReferenceEquals(threadCopies, threadCopies.DeduplicateFacebookProfiles()),
    "Account-scoped conversation IDs must never be treated as global Facebook profile identities.");
var vcardProfile = legacyBook.Import(phone, [oldCandidate]);
Check(ReferenceEquals(vcardProfile, vcardProfile.DeduplicateFacebookProfiles()),
    "A profile field in another provider must still require the user's merge review.");
var differentProfile = BrowserContactsService.Apply(autoBook, anotherFbAccount with { Contacts = [fbSnapshot.Contacts[0] with { ProfileUrl = "https://www.facebook.com/different.person" }] });
Check(differentProfile.Suggestions().Length == 0, "A matching Facebook name with a different profile must not suggest a merge.");
Push(fbSnapshot, new object());
Check(!browser.TryTake(out _), "Unowned extension connections cannot supply snapshots.");
Push(fbSnapshot);
browser.Configure(new(Facebook: false));
Check(!browser.TryTake(out _) && browser.StateFor("facebook").State == "disabled", "Turning a provider off discards queued observations before they are saved.");
Push(fbSnapshot);
Check(!browser.TryTake(out _), "Disabled sources reject new observations.");
browser.Configure(new());
Push(fbSnapshot with { Contacts = [fbSnapshot.Contacts[0] with { ProfileUrl = "https://evil.test/profile" }] });
Check(!browser.TryTake(out _), "Automatic snapshots reject unsafe profile links.");
Push(fbSnapshot with { Provider = "unknown" });
Check(!browser.TryTake(out _), "Unknown automatic providers cannot inject contact sources.");
var googleSnapshot = new BrowserContactSnapshot("google", "owner@example.com", "owner@example.com",
    [new("c123456789", "Friend 雪", "friend@example.com", "+34 600 111 222", Page: "Google Contacts"),
     new("c123456789", "Friend 雪", "other@example.com", Page: "Google Contacts")]);
var autoGoogle = BrowserContactsService.Apply(ContactBook.Empty, googleSnapshot);
Check(autoGoogle.People.Length == 1 && autoGoogle.Records.Single().Current.Values.Count(v => v.Kind == "email") == 2,
    "A Google contact's multiple emails must stay on one original source record.");
var gmailObservation = googleSnapshot with { Contacts = [new(null, "Friend 雪", "friend@example.com", Page: "Gmail")] };
autoGoogle = BrowserContactsService.Apply(autoGoogle, gmailObservation);
Check(autoGoogle.People.Length == 1 && autoGoogle.Records.Single().Current.Values.Any(v => v.Kind == "phone") &&
    autoGoogle.Records.Single().Current.Values.Count(v => v.Kind == "email") == 2, "A partial Gmail read must enrich the same contact without deleting previously observed phones or other emails.");
var otherAccount = googleSnapshot with { AccountId = "work@example.com", AccountName = "work@example.com" };
autoGoogle = BrowserContactsService.Apply(autoGoogle, otherAccount);
Check(autoGoogle.Sources.Length == 2 && autoGoogle.People.Length == 2 && autoGoogle.Suggestions().Length == 1,
    "Different open accounts retain their own origins; matching people await merge review.");
Push(googleSnapshot with { AccountId = "gmail-slot-0" });
Check(!browser.TryTake(out _), "A Gmail account index is not a persistent account identity.");
var unknownFb = BrowserContactsService.Apply(ContactBook.Empty, fbSnapshot with { AccountId = "unidentified", AccountName = "Chrome (account not identified)" });
Check(unknownFb.Sources.Single().Label.Contains("not identified", StringComparison.Ordinal), "Unknown Facebook account provenance must remain explicit.");
browser.RequestSetup("google");
using (var browserTick = JsonDocument.Parse(JsonSerializer.Serialize(browser.Tick(owner), jsonOptions)))
{
    var setupId = browserTick.RootElement.GetProperty("setup").GetProperty("id").GetString();
    Check(browserTick.RootElement.GetProperty("google").GetBoolean(), "An opted-in extension receives automatic source settings.");
    using var completed = JsonDocument.Parse(JsonSerializer.Serialize(new { setupDone = setupId }));
    browser.Accept(owner, completed.RootElement, jsonOptions);
    Check(!JsonSerializer.Serialize(browser.Tick(owner), jsonOptions).Contains(setupId!, StringComparison.Ordinal), "Permission setup acknowledgements prevent reopening the same page.");
}
browser.Detach(owner);
Check(browser.StateFor("google").State == "disconnected", "Closed extension connections surface a waiting state without deleting saved records.");
await Reject<ArgumentException>(() => Task.Run(() => browser.RequestSetup("unknown")), "Unknown provider setup requests are rejected.");
var noOpSession = new ContactSession(new ContactStore(Path.Combine(root, "no-op.json")));
await noOpSession.LoadAsync();
await noOpSession.ChangeAsync(bk => BrowserContactsService.Apply(bk, fbSnapshot));
var autoRevision = noOpSession.Book.Revision;
await noOpSession.ChangeAsync(bk => BrowserContactsService.Apply(bk, fbSnapshot));
Check(noOpSession.Book.Revision == autoRevision, "Identical automatic syncs do not write new storage revisions.");
foreach (var address in new[] { "noreply@example.com", "No-Reply@example.com", "no_reply@example.com", "no.reply@example.com",
    "donotreply@example.com", "do-not-reply@example.com", "no-replies@example.com", "noreply+account@example.com", "service.noreply@example.com", "noreply123@example.com" })
    Check(ContactChannels.IsNoReply(address), "No-reply address variants must be excluded: " + address);
foreach (var address in new[] { "reply@example.com", "support@example.com", "jane@noreply.example.com", "jane.noreplying@example.com" })
    Check(!ContactChannels.IsNoReply(address), "A real address must not be excluded because of its domain or an unrelated name: " + address);
var automatedCandidate = Candidate("auto-noreply", "Automated company", "no-reply@example.com");
var automatedBook = ContactBook.Empty.Import(google, [automatedCandidate]);
Check(!automatedBook.ListedPeople.Any() && automatedBook.Records.Single().Current.Raw == automatedCandidate.Raw &&
    ContactChannels.For(automatedBook, automatedBook.People[0]).Length == 0,
    "Previously saved no-reply-only contacts must disappear from the agenda and channel choices without deleting original data.");
automatedBook = automatedBook.Import(secondGoogle, [automatedCandidate]);
Check(automatedBook.Suggestions().Length == 0, "Hidden automated senders must not generate duplicate suggestions.");
var automatedPath = Path.Combine(root, "automated-senders.json");
var automatedStore = new ContactStore(automatedPath);
await automatedStore.SaveAsync(automatedBook, 0);
var automatedSession = new ContactSession(automatedStore);
await automatedSession.LoadAsync();
Check(!automatedSession.Book.ListedPeople.Any() && automatedSession.Book.Records.Length == 2 && automatedSession.Book.Revision == 1,
    "The no-reply filter must apply after restart without rewriting or destroying the original agenda.");
var mixedBook = ContactBook.Empty.Import(google, [automatedCandidate with { Values = [new("name", "Real person"),
    new("email", "noreply@example.com"), new("email", "real@example.com")] }]);
Check(mixedBook.ListedPeople.Count() == 1 && ContactChannels.For(mixedBook, mixedBook.People[0]).Single().Address == "real@example.com",
    "A contact with a real address must stay visible while its no-reply address is excluded as an email channel.");
var automatedSnapshot = googleSnapshot with { Contacts = [new(null, "Automated sender", "do-not-reply@example.com", Page: "Gmail")] };
var ignored = BrowserContactsService.Apply(ContactBook.Empty, automatedSnapshot);
Check(ignored.Records.Length == 0 && !ignored.ListedPeople.Any() && ReferenceEquals(ignored, BrowserContactsService.Apply(ignored, automatedSnapshot)),
    "New browser no-reply rows must not become contact records or cause repeated storage writes.");
Console.WriteLine($"PASS: {checks} contact checks; opt-in sources, lossless import, merge/undo, suggestions, storage conflicts, Google pagination, no-reply filtering and channel routing. Synthetic data: {root}");

static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(send(request)); }
}
sealed class Credentials(GmailAccount account) : IGmailCredentialStore
{
    public GmailAccount Load() => account;
    public void Save(GmailAccount value) => account = value;
}
