using System.Text;
using System.Text.RegularExpressions;

namespace Habitat.Features.Contacts;

internal static class VCardContacts
{
    internal const int MaxFileBytes = 16 * 1024 * 1024;

    internal static async Task<ContactCandidate[]> ReadAsync(string path, CancellationToken token)
    {
        if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException("Choose a vCard file smaller than 16 MB.");
        var bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        if (bytes.Length > MaxFileBytes) throw new InvalidDataException("Choose a vCard file smaller than 16 MB.");
        // Older exports with a non-UTF8 charset can be read losslessly as Latin1 first.
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(bytes); }
        return Parse(text.TrimStart('\uFEFF'));
    }

    internal static ContactCandidate[] Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxFileBytes) throw new InvalidDataException("Choose a vCard file smaller than 16 MB.");
        var lines = new List<string>();
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (lines.Count > 0 && lines[^1].EndsWith('=') &&
                lines[^1].Contains("ENCODING=QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase))
                lines[^1] = lines[^1][..^1] + (line.StartsWith(' ') || line.StartsWith('\t') ? line[1..] : line);
            else if (lines.Count > 0 && (line.StartsWith(' ') || line.StartsWith('\t')))
                lines[^1] += line[1..];
            else lines.Add(line);
        }
        var cards = new List<ContactCandidate>();
        List<string>? card = null;
        foreach (var line in lines)
        {
            if (line.Equals("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                if (card is not null) throw new InvalidDataException("The vCard file contains an unfinished contact. Nothing was imported.");
                card = [line];
            }
            else if (line.Equals("END:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                if (card is null) throw new InvalidDataException("The vCard file is incomplete. Nothing was imported.");
                card.Add(line);
                cards.Add(ParseCard(card));
                card = null;
                if (cards.Count > 20000) throw new InvalidDataException("Import up to 20,000 contacts at a time.");
            }
            else if (card is not null) card.Add(line);
            else if (!string.IsNullOrWhiteSpace(line)) throw new InvalidDataException("Choose a valid .vcf contact export.");
        }
        if (card is not null || cards.Count == 0) throw new InvalidDataException("Choose a complete vCard file containing at least one contact.");
        var unique = new Dictionary<string, ContactCandidate>();
        foreach (var candidate in cards)
        {
            if (unique.TryGetValue(candidate.RemoteId, out var prior) && prior.Raw != candidate.Raw)
                throw new InvalidDataException("Two vCards share an identifier but contain different data. Fix the export before importing.");
            unique[candidate.RemoteId] = candidate;
        }
        return unique.Values.ToArray();
    }

    private static ContactCandidate ParseCard(List<string> lines)
    {
        string? uid = null;
        var fields = new List<ContactValue>();
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parsed = new List<(string Header, string Key, string Value, string Group)>();
        foreach (var line in lines.Skip(1).SkipLast(1).Where(l => l.Length > 0))
        {
            var colon = Colon(line);
            if (colon < 1) throw new InvalidDataException("The vCard file contains an invalid field. Nothing was imported.");
            var header = line[..colon];
            var property = header.Split(';')[0];
            var dot = property.LastIndexOf('.');
            var group = dot < 0 ? "" : property[..dot];
            var key = (dot < 0 ? property : property[(dot + 1)..]).ToUpperInvariant();
            var value = line[(colon + 1)..];
            if (header.Contains("ENCODING=QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase)) value = DecodeQuoted(value, header);
            if (key == "X-ABLABEL") labels[group] = Unescape(value).Replace("_$!<", "", StringComparison.Ordinal).Replace(">!$_", "", StringComparison.Ordinal);
            parsed.Add((header, key, value, group));
        }
        foreach (var (header, key, encoded, group) in parsed)
        {
            var value = Unescape(encoded);
            var label = labels.GetValueOrDefault(group) ?? Label(header);
            var kind = key switch
            {
                "FN" => "name", "EMAIL" => "email", "TEL" => "phone", "ORG" => "organization",
                "TITLE" => "job", "ADR" => "address", "NOTE" => "note", "BDAY" => "birthday",
                "URL" => "url", "PHOTO" => "photo", "IMPP" => "im", "NICKNAME" => "nickname", _ => "vcard"
            };
            if (key == "UID") uid = value;
            if (key is "VERSION" or "UID" or "N" or "X-ABLABEL") continue;
            if (key == "TEL" && value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) value = Uri.UnescapeDataString(value[4..]);
            if (key == "EMAIL" && value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) value = Uri.UnescapeDataString(value[7..]);
            if (key is "ORG" or "ADR") value = string.Join(", ", Split(encoded, ';').Where(s => !string.IsNullOrWhiteSpace(s)).Select(Unescape));
            if (kind == "vcard") label = header;
            if (!string.IsNullOrWhiteSpace(value)) fields.Add(new(kind, value, label));
        }
        if (!fields.Any(f => f.Kind == "name"))
        {
            var n = parsed.FirstOrDefault(p => p.Key == "N").Value;
            if (n is not null)
            {
                var parts = Split(n, ';').Select(Unescape).ToArray();
                var name = string.Join(" ", new[] { 3, 1, 2, 0, 4 }.Where(i => i < parts.Length).Select(i => parts[i]).Where(p => p.Length > 0));
                if (name.Length > 0) fields.Insert(0, new("name", name));
            }
        }
        if (fields.Count == 0) throw new InvalidDataException("A vCard has no readable contact fields. Nothing was imported.");
        var raw = string.Join("\r\n", lines);
        // UID is authoritative within this source. Without one, use a conservative
        // fingerprint; uncertain identity changes become reviewable suggestions.
        var identity = fields.Where(f => f.Kind is "name" or "email" or "phone")
            .Select(f => f.Kind + ":" + (f.Kind == "phone" ? ContactBook.NormalizePhone(f.Value) : f.Value.Trim().ToUpperInvariant()))
            .Order(StringComparer.Ordinal).ToArray();
        return new(string.IsNullOrWhiteSpace(uid) ? "fingerprint:" + ContactBook.Hash(identity.Length > 0 ? string.Join("\n", identity) : raw) : "uid:" + uid,
            fields.Distinct().ToArray(), raw);
    }

    private static int Colon(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            if (line[i] == ':' && !quoted) return i;
        }
        return -1;
    }
    private static string Label(string header) => string.Join(", ", header.Split(';').Skip(1)
        .Where(s => s.StartsWith("TYPE=", StringComparison.OrdinalIgnoreCase) || !s.Contains('='))
        .Select(s => s.StartsWith("TYPE=", StringComparison.OrdinalIgnoreCase) ? s[5..].Trim('"') : s));
    private static IEnumerable<string> Split(string value, char delimiter)
    {
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\') { i++; continue; }
            if (value[i] != delimiter) continue;
            yield return value[start..i]; start = i + 1;
        }
        yield return value[start..];
    }
    private static string Unescape(string value) => Regex.Replace(value, @"\\([nN,;\\])", m => m.Groups[1].Value is "n" or "N" ? "\n" : m.Groups[1].Value);
    private static string DecodeQuoted(string value, string header)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var charset = Regex.Match(header, "(?:^|;)CHARSET=([^;]+)", RegexOptions.IgnoreCase).Groups[1].Value.Trim('"');
        Encoding encoding;
        try { encoding = charset.Length == 0 ? Encoding.UTF8 : Encoding.GetEncoding(charset); }
        catch (ArgumentException) { throw new InvalidDataException("The vCard uses an unsupported character encoding. Export it as UTF-8 and retry."); }
        using var bytes = new MemoryStream();
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '=' && i + 2 < value.Length && byte.TryParse(value.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                bytes.Write(encoding.GetBytes(value[start..i]));
                bytes.WriteByte(b); i += 2; start = i + 1;
            }
        }
        bytes.Write(encoding.GetBytes(value[start..]));
        return encoding.GetString(bytes.ToArray());
    }
}
