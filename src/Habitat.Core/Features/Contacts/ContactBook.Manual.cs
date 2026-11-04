using System.Text.Json;

namespace Habitat.Features.Contacts;

internal sealed partial record ContactBook
{
    // A manually created person is the destination identity. Editing keeps that
    // identity and its record, regardless of changes to the person's channels.
    internal ContactBook SaveContact(IReadOnlyList<ContactValue> input, string? personId = null)
    {
        var values = PrepareManualValues(input);
        CheckManualDuplicates(values, People.Where(p => p.Id != personId)
            .Select(p => (Name(p), For(p).SelectMany(r => r.Current.Values).ToArray())));
        var person = personId is null ? null : People.FirstOrDefault(p => p.Id == personId)
            ?? throw new ContactReviewException("This contact is no longer available. Reload contacts.");
        var record = person is null ? null : For(person).SingleOrDefault(r => Source(r).Kind == "local");
        if (person is not null && (record is null || person.RecordIds.Length != 1))
            throw new ContactReviewException("This contact cannot be edited in the manual address book.");
        return Import(ManualSource, [new(record?.RemoteId ?? NewId(), values, JsonSerializer.Serialize(values))]);
    }

    private static readonly ContactSource ManualSource = new("local", "local", "HABITAT", "local", true);

    private static ContactValue[] PrepareManualValues(IReadOnlyList<ContactValue> input)
    {
        var values = input.Select(v => v with { Kind = v.Kind.Trim(), Value = v.Value.Trim(), Label = v.Label.Trim() })
            .Where(v => v.Value.Length > 0).Distinct().ToArray();
        if (values.Length == 0) throw new ContactReviewException("Enter at least one contact detail.");
        foreach (var value in values)
        {
            if (value.Kind.Length == 0) throw new ContactReviewException("Choose a label for each contact detail.");
            if (value.Kind == "email" && !ContactChannels.IsEmail(value.Value))
                throw new ContactReviewException("Enter a valid email address.");
            if (value.Kind == "phone" && NormalizePhone(value.Value).Length == 0)
                throw new ContactReviewException("Enter a valid phone number, including the country code when available.");
            if (value.Kind == "messenger" && FacebookProfileIdentity(value.Value) is null)
                throw new ContactReviewException("Enter a Facebook profile link for Messenger.");
        }
        return values;
    }

    private static void CheckManualDuplicates(ContactValue[] values, IEnumerable<(string Name, ContactValue[] Values)> others)
    {
        var keys = values.Select(Identity).Where(k => k is not null).ToHashSet();
        foreach (var (name, existing) in others)
        {
            var sameIdentity = existing.Select(Identity).Any(k => k is not null && keys.Contains(k));
            // Identical name-only/custom-only submissions are also idempotent.
            // Sharing just a name does not conflate people with different details.
            var identical = existing.Length == values.Length && existing.All(v => values.Any(candidate =>
                candidate.Kind == v.Kind && string.Equals(candidate.Value, v.Value, StringComparison.OrdinalIgnoreCase) && candidate.Label == v.Label));
            if (sameIdentity || identical)
                throw new ContactReviewException("These details already belong to " + name + ". Edit the existing contact.");
        }
    }

    internal ContactBook RemoveContact(string personId)
    {
        var person = People.Single(p => p.Id == personId);
        var ids = person.RecordIds.ToHashSet();
        return this with
        {
            People = People.Where(p => p.Id != personId).ToArray(),
            Records = Records.Where(r => !ids.Contains(r.Id)).ToArray(),
            Merges = Merges.Where(m => !m.Before.SelectMany(p => p.RecordIds).Any(ids.Contains)).ToArray()
        };
    }

    private static string? Identity(ContactValue value) => value.Kind switch
    {
        "email" => "email:" + NormalizeEmail(value.Value),
        "phone" when NormalizePhone(value.Value) is { Length: > 0 } phone => "phone:" + phone,
        "messenger" when FacebookProfileIdentity(value.Value) is { } profile => "messenger:" + profile,
        _ => null
    };
}
