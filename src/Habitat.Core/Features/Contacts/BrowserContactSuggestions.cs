namespace Habitat.Features.Contacts;

internal sealed record BrowserContactSuggestion(string SourceId, ContactCandidate Candidate, bool Dismissed = false);
internal sealed class ContactReviewException(string message) : InvalidOperationException(message);

internal sealed partial record ContactBook
{
    // Optional in older schema-1 files. Observations never become people until accepted.
    public BrowserContactSuggestion[] BrowserSuggestions { get; init; } = [];

    internal BrowserContactSuggestion[] PendingBrowserSuggestions => BrowserSuggestions
        .Where(s => !s.Dismissed && !HasSavedBrowserCandidate(s.SourceId, s.Candidate)).ToArray();

    private bool HasSavedBrowserCandidate(string sourceId, ContactCandidate candidate)
    {
        var account = Sources.Single(s => s.Id == sourceId).Account;
        var emails = candidate.Values.Where(v => v.Kind == "email").Select(v => NormalizeEmail(v.Value)).ToHashSet();
        return Records.Any(r => r.SourceId == sourceId && r.RemoteId == candidate.RemoteId ||
            Source(r).Kind == "google" && Source(r).Account.Equals(account, StringComparison.OrdinalIgnoreCase) &&
            r.Current.Values.Any(v => v.Kind == "email" && emails.Contains(NormalizeEmail(v.Value))));
    }

    internal ContactBook ObserveBrowserCandidates(ContactSource source, ContactCandidate[] candidates)
    {
        var existing = Sources.FirstOrDefault(s => s.Id == source.Id);
        var next = existing is null ? WithSource(source) : this;
        var suggestions = BrowserSuggestions.ToDictionary(s => RecordId(s.SourceId, s.Candidate.RemoteId));
        var changed = false;
        foreach (var candidate in candidates)
        {
            if (next.HasSavedBrowserCandidate(source.Id, candidate)) continue;
            var key = RecordId(source.Id, candidate.RemoteId);
            if (suggestions.TryGetValue(key, out var old) && (old.Dismissed || old.Candidate.Raw == candidate.Raw)) continue;
            suggestions[key] = new(source.Id, candidate);
            changed = true;
        }
        return changed ? next with { BrowserSuggestions = suggestions.Values.ToArray() } : next;
    }

    internal ContactBook ReviewBrowserSuggestions(string[] keys, bool accept, BrowserContactSuggestion[]? expected = null)
    {
        var selected = keys.ToHashSet();
        if (selected.Count == 0) throw new ContactReviewException("Select at least one suggestion to review.");
        var pending = PendingBrowserSuggestions.Where(s => selected.Contains(RecordId(s.SourceId, s.Candidate.RemoteId))).ToArray();
        if (pending.Length != selected.Count) throw new ContactReviewException("Suggestions changed. Review the current list and try again.");
        if (expected is not null && pending.Any(s => !expected.Any(e => e.SourceId == s.SourceId &&
            e.Candidate.RemoteId == s.Candidate.RemoteId && e.Candidate.Raw == s.Candidate.Raw)))
            throw new ContactReviewException("Suggestions changed. Reload suggestions and review them before adding contacts.");
        var next = this;
        foreach (var group in pending.GroupBy(s => s.SourceId))
            if (accept) next = next.Import(Sources.Single(s => s.Id == group.Key), group.Select(s => s.Candidate).ToArray());
        return next with { BrowserSuggestions = next.BrowserSuggestions
            .Where(s => !accept || !selected.Contains(RecordId(s.SourceId, s.Candidate.RemoteId)))
            .Select(s => selected.Contains(RecordId(s.SourceId, s.Candidate.RemoteId)) ? s with { Dismissed = true } : s).ToArray() };
    }

    internal ContactPerson[] SavedBrowserPeople => People.Where(p => For(p).All(r => Source(r).Kind == "google-browser")).ToArray();

    internal ContactBook ReviewSavedBrowserPeople(string[] ids, bool hide)
    {
        var selected = ids.ToHashSet();
        if (selected.Count == 0) throw new ContactReviewException("Select at least one saved browser contact.");
        if (SavedBrowserPeople.Count(p => selected.Contains(p.Id)) != selected.Count)
            throw new ContactReviewException("Contacts changed. Review the current list and try again.");
        return this with { People = People.Select(p => selected.Contains(p.Id)
            ? p with { Hidden = hide, BrowserReviewed = true } : p).ToArray() };
    }

    internal string BrowserReviewReason(ContactPerson person)
    {
        if (person.Hidden) return "Hidden · original data kept";
        if (person.BrowserReviewed) return "Reviewed · kept in your address book";
        var emails = For(person).SelectMany(r => r.Current.Values).Where(v => v.Kind == "email").Select(v => v.Value).ToArray();
        if (emails.Any(ContactChannels.IsNoReply)) return "Automatic sender address · review before keeping";
        if (emails.Any(e => new[] { "notifications", "newsletter", "notification", "mailer", "alerts", "notify" }
            .Contains(e.Split('@')[0], StringComparer.OrdinalIgnoreCase))) return "Possible automated sender · review before keeping";
        return "Saved from a partial browser observation · review this contact";
    }
}
