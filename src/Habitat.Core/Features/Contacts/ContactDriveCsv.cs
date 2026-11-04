using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace Habitat.Features.Contacts;

internal enum ContactDriveConflictChoice { Local, Drive }

internal sealed class ContactDriveConflictException(string message) : Exception(message);

internal static class ContactDriveCsv
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly string[] Header = ["Contact ID", "Type", "Label", "Value"];
    internal const int MaxBytes = 16 * 1024 * 1024;

    internal static byte[] Encode(IReadOnlyDictionary<string, ContactValue[]> contacts)
    {
        var csv = new StringBuilder();
        WriteRow(Header);
        foreach (var (id, values) in contacts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            foreach (var value in values)
                WriteRow([id, EscapeCell(value.Kind), EscapeCell(value.Label), EscapeCell(value.Value)]);
        if (Utf8.GetByteCount(csv.ToString()) > MaxBytes) throw new InvalidDataException("The contacts CSV is too large to sync.");
        return Utf8.GetBytes(csv.ToString());

        void WriteRow(string[] row) => csv.AppendJoin(',', row.Select(s => "\"" + s.Replace("\"", "\"\"") + "\"")).Append("\r\n");
    }

    internal static Dictionary<string, ContactValue[]> Decode(byte[] bytes)
    {
        try
        {
            if (bytes.Length > MaxBytes) throw InvalidCsv();
            using var reader = new TextFieldParser(new StringReader(Utf8.GetString(bytes).TrimStart('\uFEFF')))
            {
                TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false
            };
            reader.SetDelimiters(",");
            if (reader.ReadFields() is not { } header || !header.SequenceEqual(Header)) throw InvalidCsv();
            var contacts = new Dictionary<string, List<ContactValue>>(StringComparer.Ordinal);
            while (!reader.EndOfData)
            {
                if (reader.ReadFields() is not { Length: 4 } row || !Guid.TryParseExact(row[0], "N", out var id)
                    || row[0] != id.ToString("N")) throw InvalidCsv();
                if (!contacts.TryGetValue(row[0], out var values)) contacts.Add(row[0], values = []);
                var value = new ContactValue(UnescapeCell(row[1]), UnescapeCell(row[3]), UnescapeCell(row[2]));
                if (string.IsNullOrWhiteSpace(value.Kind) || string.IsNullOrWhiteSpace(value.Value) || values.Contains(value))
                    throw InvalidCsv();
                values.Add(value);
            }
            return contacts.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is MalformedLineException or DecoderFallbackException)
        { throw InvalidCsv(); }
    }

    private static InvalidDataException InvalidCsv() =>
        new("The contacts CSV in Google Drive is invalid. Restore a HABITAT CSV before syncing.");

    // Spreadsheet apps can interpret contact text as a formula. Double a literal
    // leading apostrophe so escaping remains reversible when HABITAT reads its CSV.
    private static bool NeedsEscape(string text)
    {
        var trimmed = text.TrimStart();
        return text.StartsWith('\'') || text.StartsWith('\t') || text.StartsWith('\r') ||
            text.StartsWith('\n') || (trimmed.Length > 0 && "=+-@＝＋－＠".Contains(trimmed[0]));
    }

    private static string EscapeCell(string text) => NeedsEscape(text) ? "'" + text : text;
    private static string UnescapeCell(string text) => text.StartsWith('\'') && NeedsEscape(text[1..]) ? text[1..] : text;

    internal static bool Same(ContactValue[]? left, ContactValue[]? right) =>
        left is null || right is null ? left is null && right is null : left.SequenceEqual(right);

    internal static Dictionary<string, ContactValue[]> Merge(
        IReadOnlyDictionary<string, ContactValue[]> baseline, IReadOnlyDictionary<string, ContactValue[]> local,
        IReadOnlyDictionary<string, ContactValue[]> drive, ContactDriveConflictChoice? choice)
    {
        var merged = new Dictionary<string, ContactValue[]>(StringComparer.Ordinal);
        var conflicts = new List<string>();
        foreach (var id in local.Keys.Union(drive.Keys))
        {
            var previous = baseline.GetValueOrDefault(id);
            var here = local.GetValueOrDefault(id);
            var there = drive.GetValueOrDefault(id);
            ContactValue[]? selected;
            if (Same(here, there) || Same(there, previous)) selected = here;
            else if (Same(here, previous)) selected = there;
            else
            {
                selected = choice == ContactDriveConflictChoice.Drive ? there : here;
                conflicts.Add((here ?? there)?.FirstOrDefault(v => v.Kind == "name")?.Value ?? "Unnamed contact");
            }
            if (selected is not null) merged.Add(id, selected);
        }
        if (choice is null && conflicts.Count > 0)
            throw new ContactDriveConflictException("Changed on both sides: " + string.Join(", ", conflicts.Take(5)) +
                ". Choose which version to use for conflicting contacts.");
        return merged;
    }
}

internal sealed partial record ContactBook
{
    internal Dictionary<string, ContactValue[]> DriveContacts() => People
        .Where(p => p.RecordIds.Length == 1 && For(p)[0].SourceId == ManualSource.Id)
        .ToDictionary(p => p.Id, p => For(p)[0].Current.Values, StringComparer.Ordinal);

    internal ContactBook ApplyDriveContacts(IReadOnlyDictionary<string, ContactValue[]> contacts)
    {
        var local = DriveContacts();
        if (local.Count == contacts.Count && local.All(pair => ContactDriveCsv.Same(pair.Value, contacts.GetValueOrDefault(pair.Key))))
            return this;

        var next = this;
        foreach (var id in local.Keys.Except(contacts.Keys)) next = next.RemoveContact(id);
        foreach (var (id, input) in contacts)
        {
            if (ContactDriveCsv.Same(input, local.GetValueOrDefault(id))) continue;
            var values = PrepareManualValues(input);
            if (!values.SequenceEqual(input)) throw new InvalidDataException("The contacts CSV contains invalid contact details.");
            var person = next.People.FirstOrDefault(p => p.Id == id);
            if (person is not null && !local.ContainsKey(id))
                throw new InvalidDataException("A Drive contact identifier conflicts with an existing contact.");
            var remoteId = person is null ? id : next.For(person)[0].RemoteId;
            var before = next.People.Select(p => p.Id).ToHashSet();
            next = next.Import(ManualSource, [new(remoteId, values, JsonSerializer.Serialize(values))]);
            if (person is null)
            {
                if (next.People.Length != before.Count + 1) throw new InvalidDataException("Repeated contact identifier in Google Drive.");
                next = next with { People = next.People.Select(p => before.Contains(p.Id) ? p : p with { Id = id }).ToArray() };
            }
        }
        foreach (var (id, values) in contacts)
        {
            if (ContactDriveCsv.Same(values, local.GetValueOrDefault(id))) continue;
            CheckManualDuplicates(values, next.People.Where(p => p.Id != id)
                .Select(p => (next.Name(p), next.For(p).SelectMany(r => r.Current.Values).ToArray())));
        }
        ContactStore.Validate(next);
        return next;
    }
}
