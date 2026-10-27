using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.CompilerServices;

namespace Habitat.Features.Contacts;

internal sealed record ContactValue(string Kind, string Value, string Label = "");
internal sealed record ContactSnapshot(ContactValue[] Values, string Raw, DateTimeOffset ObservedAt);
internal sealed record ContactRecord(string Id, string SourceId, string RemoteId, ContactSnapshot Current,
    ContactSnapshot[] History, bool Missing = false);
internal sealed record ContactSource(string Id, string Kind, string Label, string Account, bool Connected,
    DateTimeOffset? LastImported = null);
internal sealed record ContactPerson(string Id, string[] RecordIds, string? PreferredRecordId = null);
internal sealed record ContactMerge(string Id, string PersonId, ContactPerson[] Before, DateTimeOffset At);
internal sealed record ContactCandidate(string RemoteId, ContactValue[] Values, string Raw);
internal sealed record ContactSuggestion(string LeftId, string RightId, string Reason);

// A person is a local grouping of source records. Linking never edits a provider.
internal sealed record ContactBook(int Schema, long Revision, ContactSource[] Sources, ContactRecord[] Records,
    ContactPerson[] People, ContactMerge[] Merges, string[] DismissedPairs)
{
    internal static ContactBook Empty => new(1, 0, [], [], [], [], []);
    private sealed record Index(Dictionary<string, ContactRecord> Records, Dictionary<string, ContactSource> Sources);
    private static readonly ConditionalWeakTable<ContactBook, Index> Indices = new();
    private Index Lookup => Indices.GetValue(this, b => new(b.Records.ToDictionary(r => r.Id), b.Sources.ToDictionary(s => s.Id)));
    internal ContactRecord[] For(ContactPerson person) => person.RecordIds.Select(id => Lookup.Records[id]).ToArray();
    internal string Name(ContactPerson person)
    {
        var records = For(person).OrderByDescending(r => r.Id == person.PreferredRecordId).ToArray();
        return records.SelectMany(r => r.Current.Values).FirstOrDefault(v => v.Kind == "name")?.Value
            ?? records.SelectMany(r => r.Current.Values).FirstOrDefault(v => v.Kind is "email" or "phone")?.Value
            ?? "Unnamed contact";
    }
    internal ContactSource Source(ContactRecord record) => Lookup.Sources[record.SourceId];
    internal static string NewId() => Guid.NewGuid().ToString("N");
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static string RecordId(string source, string remote) => Hash(source + "\n" + remote);

    internal ContactBook WithSource(ContactSource source)
    {
        if (Sources.Any(s => s.Id != source.Id && s.Kind == source.Kind &&
            string.Equals(s.Account, source.Account, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This account is already added. Use its existing source to import or reload contacts.");
        return this with { Sources = Sources.Where(s => s.Id != source.Id).Append(source).ToArray() };
    }

    internal ContactBook Import(ContactSource source, IReadOnlyList<ContactCandidate> candidates, bool complete = false)
    {
        if (candidates.Count == 0) throw new InvalidOperationException("Select at least one contact to import.");
        if (candidates.Select(c => c.RemoteId).Distinct(StringComparer.Ordinal).Count() != candidates.Count)
            throw new InvalidDataException("The source returned repeated contact identifiers. Nothing was imported.");
        var now = DateTimeOffset.UtcNow;
        var book = WithSource(source with { LastImported = now });
        var records = Records.ToDictionary(r => r.Id);
        var people = People.ToList();
        var seen = new HashSet<string>();
        foreach (var candidate in candidates)
        {
            var id = RecordId(source.Id, candidate.RemoteId);
            seen.Add(id);
            var snapshot = new ContactSnapshot(candidate.Values.Distinct().ToArray(), candidate.Raw, now);
            if (records.TryGetValue(id, out var old))
            {
                var unchanged = old.Current.Raw == snapshot.Raw && old.Current.Values.SequenceEqual(snapshot.Values);
                records[id] = old with { Current = unchanged ? old.Current : snapshot,
                    History = unchanged ? old.History : old.History.Append(old.Current).ToArray(), Missing = false };
            }
            else
            {
                records.Add(id, new(id, source.Id, candidate.RemoteId, snapshot, []));
                people.Add(new(NewId(), [id]));
            }
        }
        if (complete)
            foreach (var record in records.Values.Where(r => r.SourceId == source.Id && !seen.Contains(r.Id)).ToArray())
                records[record.Id] = record with { Missing = true };
        return book with { Records = records.Values.ToArray(), People = people.ToArray() };
    }

    internal ContactBook Merge(string leftId, string rightId)
    {
        if (leftId == rightId) throw new InvalidOperationException("Choose two different people to merge.");
        var left = People.Single(p => p.Id == leftId);
        var right = People.Single(p => p.Id == rightId);
        var merged = left with { RecordIds = left.RecordIds.Concat(right.RecordIds).ToArray() };
        return this with { People = People.Where(p => p.Id != rightId).Select(p => p.Id == leftId ? merged : p).ToArray(),
            Merges = Merges.Append(new(NewId(), leftId, [left, right], DateTimeOffset.UtcNow)).ToArray() };
    }

    internal ContactMerge? UndoableMerge(string personId)
    {
        var person = People.FirstOrDefault(p => p.Id == personId);
        return person is null ? null : Merges.LastOrDefault(m => m.PersonId == personId &&
            m.Before.SelectMany(p => p.RecordIds).ToHashSet().SetEquals(person.RecordIds));
    }

    internal ContactBook UndoMerge(string personId)
    {
        var merge = UndoableMerge(personId) ?? throw new InvalidOperationException("This merge can no longer be undone.");
        var restored = this with { People = People.Where(p => p.Id != personId).Concat(merge.Before).ToArray(),
            Merges = Merges.Where(m => m.Id != merge.Id).ToArray() };
        // Undo is an explicit decision to keep these copies separate, including
        // when they carry the same provider identity. A later scan must honour it.
        return FacebookProfiles(merge.Before[0]).Intersect(FacebookProfiles(merge.Before[1])).Any()
            ? restored.Dismiss(merge.Before[0].Id, merge.Before[1].Id) : restored;
    }

    internal static string? FacebookProfileIdentity(string value)
    {
        if (!Habitat.Features.Facebook.FacebookContactsService.SafeLink(value)) return null;
        var uri = new Uri(value);
        var path = uri.AbsolutePath.Trim('/');
        // A conversation ID is scoped to its signed-in account, not a profile.
        if (path.StartsWith("messages/", StringComparison.Ordinal)) return null;
        return path.ToLowerInvariant() + uri.Query;
    }

    private IEnumerable<string> FacebookProfiles(ContactPerson person) => For(person)
        .Where(r => !r.Missing && Source(r).Kind == "facebook")
        .SelectMany(r => r.Current.Values.Where(v => v.Kind is "profile" or "messenger"))
        .Select(v => FacebookProfileIdentity(v.Value)).OfType<string>().Distinct();

    internal ContactBook DeduplicateFacebookProfiles()
    {
        var identities = Records.Where(r => !r.Missing && Source(r).Kind == "facebook")
            .SelectMany(r => r.Current.Values.Where(v => v.Kind is "profile" or "messenger")
                .Select(v => (Record: r.Id, Profile: FacebookProfileIdentity(v.Value))))
            .Where(x => x.Profile is not null).GroupBy(x => x.Profile).Where(g => g.Count() > 1);
        var owners = People.SelectMany(p => p.RecordIds.Select(id => (id, p))).ToDictionary(x => x.id, x => x.p);
        var dismissed = DismissedPairs.ToHashSet();
        var book = this;
        foreach (var identity in identities)
        {
            var first = identity.First().Record;
            foreach (var record in identity.Skip(1))
            {
                var left = owners[first]; var right = owners[record.Record];
                if (left.Id == right.Id || Pairs(left, right).Any(dismissed.Contains)) continue;
                book = book.Merge(left.Id, right.Id);
                var person = book.People.Single(p => p.Id == left.Id);
                foreach (var id in person.RecordIds) owners[id] = person;
            }
        }
        return book;
    }

    internal ContactBook PreferRecord(string personId, string recordId)
    {
        var person = People.Single(p => p.Id == personId);
        if (!person.RecordIds.Contains(recordId)) throw new InvalidOperationException("This record does not belong to the person.");
        return this with { People = People.Select(p => p.Id == personId ? p with { PreferredRecordId = recordId } : p).ToArray() };
    }

    internal ContactBook Dismiss(string leftId, string rightId)
    {
        var pairs = Pairs(People.Single(p => p.Id == leftId), People.Single(p => p.Id == rightId));
        return this with { DismissedPairs = DismissedPairs.Concat(pairs).Distinct().ToArray() };
    }

    internal ContactSuggestion[] Suggestions()
    {
        var owners = People.SelectMany(p => p.RecordIds.Select(r => (r, p))).ToDictionary(x => x.r, x => x.p);
        var index = new Dictionary<string, List<ContactPerson>>(StringComparer.Ordinal);
        foreach (var record in Records.Where(r => !r.Missing))
        foreach (var field in record.Current.Values.Where(v => v.Kind is "email" or "phone" ||
            v.Kind is "profile" or "messenger" && Source(record).Kind == "facebook" &&
            Habitat.Features.Facebook.FacebookContactsService.SafeLink(v.Value) &&
            !new Uri(v.Value).AbsolutePath.StartsWith("/messages/", StringComparison.Ordinal)))
        {
            var normalized = field.Kind == "email" ? (ContactChannels.IsEmail(field.Value) ? NormalizeEmail(field.Value) : "") :
                field.Kind == "phone" ? NormalizePhone(field.Value) : field.Value;
            if (normalized.Length == 0) continue;
            var key = (field.Kind is "profile" or "messenger" ? "profile" : field.Kind) + ":" + normalized;
            if (!index.TryGetValue(key, out var group)) index[key] = group = [];
            if (!group.Any(p => p.Id == owners[record.Id].Id)) group.Add(owners[record.Id]);
        }
        var dismissed = DismissedPairs.ToHashSet();
        var found = new Dictionary<string, ContactSuggestion>();
        foreach (var (key, group) in index)
        {
            // A shared switchboard or mailing-list address is not a useful identity hint.
            if (group.Count > 10) continue;
            for (var i = 0; i < group.Count; i++)
            for (var j = i + 1; j < group.Count; j++)
            {
                var left = group[i]; var right = group[j];
                if (Pairs(left, right).Any(dismissed.Contains)) continue;
                found.TryAdd(Pair(left.Id, right.Id), new(left.Id, right.Id,
                    key.StartsWith("email:", StringComparison.Ordinal) ? "Same email address" :
                    key.StartsWith("profile:", StringComparison.Ordinal) ? "Same Facebook profile" : "Same phone number"));
                if (found.Count >= 100) return found.Values.ToArray();
            }
        }
        return found.Values.ToArray();
    }

    private static IEnumerable<string> Pairs(ContactPerson left, ContactPerson right) =>
        left.RecordIds.SelectMany(l => right.RecordIds.Select(r => Pair(l, r)));
    private static string Pair(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + ":" + b : b + ":" + a;
    internal static string NormalizeEmail(string value) => value.Trim().ToUpperInvariant();
    internal static string NormalizePhone(string value)
    {
        value = value.Trim();
        if (value.Count(c => c == '+') > 1 || value.Contains('+') && !value.StartsWith('+')) return "";
        // Never strip extensions or infer a country: that can join different people.
        if (value.Any(c => !char.IsAsciiDigit(c) && c is not ('+' or ' ' or '-' or '(' or ')' or '.'))) return "";
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        if (value.StartsWith("00", StringComparison.Ordinal)) return digits.Length is >= 9 and <= 17 ? "+" + digits[2..] : "";
        return digits.Length is >= 7 and <= 15 ? (value.StartsWith('+') ? "+" : "") + digits : "";
    }

    internal static bool Matches(string text, string query) => CultureInfo.InvariantCulture.CompareInfo.IndexOf(text, query,
        CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
}
