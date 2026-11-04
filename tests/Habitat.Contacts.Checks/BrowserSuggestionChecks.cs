using Habitat.Features.Contacts;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class BrowserSuggestionChecks
{
    internal static async Task<int> RunAsync(string root)
    {
        var checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        string[] Keys(ContactBook b) => b.PendingBrowserSuggestions.Select(s => ContactBook.RecordId(s.SourceId, s.Candidate.RemoteId)).ToArray();
        var snapshot = new BrowserContactSnapshot("google", "owner@example.com", "owner@example.com",
            [new(null, "Newsletter ☕", "newsletter@example.com", Page: "Gmail")]);
        var pending = BrowserContactsService.Apply(ContactBook.Empty, snapshot);
        Check(pending.Records.Length == 0 && !pending.ListedPeople.Any() && pending.PendingBrowserSuggestions.Length == 1,
            "A Gmail sender must not become a saved person or an actionable channel before acceptance.");
        Check(ReferenceEquals(pending, BrowserContactsService.Apply(pending, snapshot)), "Unchanged pending observations must avoid storage writes.");
        var source = pending.Sources.Single();
        var changed = snapshot with { Contacts = [new(null, "Updated newsletter name", "newsletter@example.com", Page: "Gmail")] };
        pending = BrowserContactsService.Apply(pending, changed);
        Check(pending.PendingBrowserSuggestions.Length == 1 && pending.PendingBrowserSuggestions[0].Candidate.Values.Contains(new("name", "Updated newsletter name")),
            "A repeated address must update one pending suggestion.");
        var stale = false;
        try { pending.ReviewBrowserSuggestions(Keys(pending), accept: true, expected: BrowserContactsService.Apply(ContactBook.Empty, snapshot).PendingBrowserSuggestions); }
        catch (InvalidOperationException) { stale = true; }
        Check(stale, "Changed suggestion data must be reviewed again before acceptance.");
        var ignored = pending.ReviewBrowserSuggestions(Keys(pending), accept: false);
        Check(!ignored.PendingBrowserSuggestions.Any() && ignored.BrowserSuggestions.Single().Dismissed && ignored.Records.Length == 0,
            "Ignoring must persist a decision without creating or deleting saved contacts.");
        Check(ReferenceEquals(ignored, BrowserContactsService.Apply(ignored, snapshot)), "A dismissed sender must not reappear or rewrite storage on every scan.");
        var store = new ContactStore(Path.Combine(root, "browser-suggestions.json"));
        await store.SaveAsync(ignored, 0);
        var loaded = await store.LoadAsync();
        Check(!BrowserContactsService.Apply(loaded, snapshot).PendingBrowserSuggestions.Any(), "Ignored suggestions must stay ignored after restart.");
        var accepted = pending.ReviewBrowserSuggestions(Keys(pending), accept: true);
        Check(accepted.People.Length == 1 && accepted.Records.Length == 1 && accepted.BrowserSuggestions.Length == 0 &&
            ContactChannels.For(accepted, accepted.People[0]).Single().Address == "newsletter@example.com", "Only explicit acceptance creates an actionable contact.");
        Check(ReferenceEquals(accepted, BrowserContactsService.Apply(accepted, snapshot)) && accepted.Name(accepted.People[0]) == "Updated newsletter name",
            "Later Gmail rows must not overwrite accepted contact names or create another suggestion.");
        var enriched = BrowserContactsService.Apply(pending, snapshot with { Contacts = [new("c123456789", "Contact name", "newsletter@example.com", "+34600111222", Page: "Google Contacts")] });
        enriched = BrowserContactsService.Apply(enriched, snapshot);
        Check(enriched.PendingBrowserSuggestions.Length == 1 && enriched.PendingBrowserSuggestions[0].Candidate.Values.Any(v => v.Kind == "phone"),
            "Google Contacts rows must enrich a pending Gmail address without importing or losing fields.");
        var afterDismiss = BrowserContactsService.Apply(ignored, snapshot with { Contacts = [new("c123456789", "Same sender", "newsletter@example.com", Page: "Google Contacts")] });
        Check(!afterDismiss.PendingBrowserSuggestions.Any(), "A new browser contact ID must not bypass dismissal of the same address.");
        var apiSource = new ContactSource("api-owner", "google", "Google · owner@example.com", "owner@example.com", true);
        var apiCandidate = new ContactCandidate("people/real", [new("name", "Saved Google name"), new("email", "newsletter@example.com")], "original Google JSON");
        var api = pending.Import(apiSource, [apiCandidate], complete: true);
        Check(!api.PendingBrowserSuggestions.Any() && api.People.Length == 1 && ReferenceEquals(api, BrowserContactsService.Apply(api, snapshot)),
            "Contacts already imported through the same account API must not be suggested or overwritten by Gmail.");
        var otherAccount = BrowserContactsService.Apply(api, snapshot with { AccountId = "other@example.com", AccountName = "other@example.com" });
        Check(otherAccount.PendingBrowserSuggestions.Length == 1, "Google API coverage must not be incorrectly attributed to another account.");
        var merged = accepted.Import(apiSource, [apiCandidate]);
        merged = merged.Merge(merged.People[0].Id, merged.People[1].Id);
        Check(merged.Name(merged.People[0]) == "Saved Google name", "An explicitly merged person should use the primary Google Contacts name by default.");
        merged = merged.PreferRecord(merged.People[0].Id, merged.Records.First(r => r.SourceId == source.Id).Id);
        Check(merged.Name(merged.People[0]) == "Updated newsletter name", "An explicit preferred name must still override the default source priority.");

        // Load an actual old-schema shape: no new suggestion property or review flags.
        var legacy = ContactBook.Empty.Import(source, pending.PendingBrowserSuggestions.Select(s => s.Candidate).ToArray());
        var legacyJson = JsonSerializer.SerializeToNode(legacy)!.AsObject();
        legacyJson.Remove("BrowserSuggestions");
        foreach (var person in legacyJson["People"]!.AsArray()) { person!.AsObject().Remove("Hidden"); person.AsObject().Remove("BrowserReviewed"); }
        var legacyPath = Path.Combine(root, "legacy-browser-contacts.json");
        await File.WriteAllTextAsync(legacyPath, legacyJson.ToJsonString());
        var legacyStore = new ContactStore(legacyPath);
        var old = await legacyStore.LoadAsync();
        Check(old.ListedPeople.Count() == 1 && old.SavedBrowserPeople.Length == 1 && !old.People[0].BrowserReviewed && old.BrowserSuggestions.Length == 0,
            "Existing contact files must open without hiding, deleting or automatically reviewing anyone.");
        Check(old.BrowserReviewReason(old.People[0]).StartsWith("Possible automated sender"), "Legacy newsletters should carry a review hint, not an automatic deletion decision.");
        var hidden = old.ReviewSavedBrowserPeople([old.People[0].Id], hide: true);
        Check(!hidden.ListedPeople.Any() && ContactChannels.For(hidden, hidden.People[0]).Length == 0 &&
            hidden.Records.Single().Current.Raw == old.Records.Single().Current.Raw && hidden.Records.Length == old.Records.Length,
            "Confirmed hiding must retain all original source data and stop actionable channel choices.");
        Check(!BrowserContactsService.Apply(hidden, snapshot).ListedPeople.Any(), "A later observation must never silently restore a hidden contact.");
        hidden = await legacyStore.SaveAsync(hidden, old.Revision);
        hidden = await legacyStore.LoadAsync();
        var restored = hidden.ReviewSavedBrowserPeople([hidden.People[0].Id], hide: false);
        Check(restored.ListedPeople.Count() == 1 && restored.People[0].BrowserReviewed && restored.Records.Single() == hidden.Records.Single(),
            "Hidden contacts must survive restart and restore with their original source records intact.");
        var duplicateBook = old.Import(apiSource, [apiCandidate]);
        Check(duplicateBook.Suggestions().Length == 1, "Existing Gmail copies must remain available for manual duplicate review against Google Contacts.");
        duplicateBook = duplicateBook.ReviewSavedBrowserPeople([old.People[0].Id], hide: true);
        Check(duplicateBook.Suggestions().Length == 0 && duplicateBook.ListedPeople.Count() == 1, "Hiding a legacy copy must preserve the API contact and stop duplicate prompts.");
        var valid = false;
        try { old.ReviewSavedBrowserPeople(["missing"], hide: true); } catch (InvalidOperationException) { valid = true; }
        Check(valid, "A stale saved-contact selection must be rejected without partial mutation.");
        valid = false;
        try { pending.ReviewBrowserSuggestions(["missing"], accept: true); } catch (InvalidOperationException) { valid = true; }
        Check(valid, "A stale suggestion selection must not import a different contact.");
        var session = new ContactSession(new ContactStore(Path.Combine(root, "review-validation.json")));
        await session.LoadAsync();
        await session.ChangeAsync(_ => pending);
        valid = false;
        try { await session.ChangeAsync(b => b.ReviewBrowserSuggestions([], accept: true)); }
        catch (ContactReviewException) { valid = true; }
        Check(valid && session.Loaded && session.Error is null && session.Book.PendingBrowserSuggestions.Length == 1,
            "An empty or stale review selection must be recoverable without marking the contact store unreadable.");
        var invalid = pending with { BrowserSuggestions = null! };
        valid = false;
        try { ContactStore.Validate(invalid); } catch (InvalidDataException) { valid = true; }
        Check(valid, "Malformed review state must fail validation while the original file is retained.");
        return checks;
    }
}
