using Habitat.Models;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Habitat.Services;

internal static class GmailMessageParser
{
    private const int MaxBodyCharacters = 150000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

    public static GmailMessage Parse(JsonElement root, bool includeBody)
    {
        root.TryGetProperty("payload", out var payload);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("headers", out var values))
            foreach (var header in values.EnumerateArray())
            {
                var name = String(header, "name");
                if (name.Length > 0) headers[name] = DecodeHeader(String(header, "value"));
            }
        var date = DateTimeOffset.UnixEpoch;
        if (long.TryParse(String(root, "internalDate"), out var milliseconds) && milliseconds is >= 0 and <= 253402300799999)
            date = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        else if (headers.TryGetValue("Date", out var headerDate) && DateTimeOffset.TryParse(headerDate, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var parsedDate)) date = parsedDate;

        string? body = null;
        if (includeBody && payload.ValueKind == JsonValueKind.Object)
        {
            var plain = new List<string>();
            var html = new List<string>();
            CollectBody(payload, plain, html, 0);
            body = plain.Count > 0 ? string.Join("\n\n", plain) : HtmlToText(string.Join("\n\n", html));
            if (string.IsNullOrWhiteSpace(body)) body = "No text body available. Attachments are not downloaded.";
            if (body.Length > MaxBodyCharacters) body = body[..MaxBodyCharacters] + "\n\n[Message truncated]";
        }
        return new GmailMessage(String(root, "id"), String(root, "threadId"),
            headers.GetValueOrDefault("From", "Unknown sender"), headers.GetValueOrDefault("Subject", "(No subject)"),
            WebUtility.HtmlDecode(String(root, "snippet")), date, body, headers.GetValueOrDefault("Message-ID"));
    }

    private static void CollectBody(JsonElement part, List<string> plain, List<string> html, int depth)
    {
        if (depth > 12 || plain.Count + html.Count >= 100 || plain.Sum(text => text.Length) + html.Sum(text => text.Length) > MaxBodyCharacters * 2 ||
            String(part, "filename").Length > 0) return;
        var mime = String(part, "mimeType");
        if (mime is "text/plain" or "text/html" && part.TryGetProperty("body", out var body))
        {
            var encoded = String(body, "data");
            if (encoded.Length > 0 && encoded.Length < 4000000)
            {
                try
                {
                    var bytes = Convert.FromBase64String(encoded.Replace('-', '+').Replace('_', '/').PadRight((encoded.Length + 3) / 4 * 4, '='));
                    var charset = "utf-8";
                    if (part.TryGetProperty("headers", out var headers))
                        foreach (var header in headers.EnumerateArray())
                            if (String(header, "name").Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                            {
                                var match = Regex.Match(String(header, "value"), "charset\\s*=\\s*[\"']?([^\\s;\"']+)", RegexOptions.IgnoreCase, RegexTimeout);
                                if (match.Success) charset = match.Groups[1].Value;
                            }
                    Encoding encoding;
                    try { encoding = Encoding.GetEncoding(charset); }
                    catch (ArgumentException) { encoding = Encoding.UTF8; }
                    var text = encoding.GetString(bytes);
                    (mime == "text/plain" ? plain : html).Add(text.Length > MaxBodyCharacters + 1 ? text[..(MaxBodyCharacters + 1)] : text);
                }
                catch (FormatException) { }
            }
        }
        if (part.TryGetProperty("parts", out var parts))
            foreach (var child in parts.EnumerateArray()) CollectBody(child, plain, html, depth + 1);
    }

    internal static string HtmlToText(string html)
    {
        // Emails are untrusted data. Never render HTML or load remote images,
        // links, scripts or attachment content in the application.
        try
        {
            var text = Regex.Replace(html, @"<(script|style|head)\b[^>]*>[\s\S]*?</\1\s*>", "", RegexOptions.IgnoreCase, RegexTimeout);
            text = Regex.Replace(text, @"<!--[\s\S]*?-->", "", RegexOptions.None, RegexTimeout);
            text = Regex.Replace(text, @"<\s*(br\b[^>]*|/(p|div|li|h[1-6]|tr)\s*)>", "\n", RegexOptions.IgnoreCase, RegexTimeout);
            text = Regex.Replace(text, @"<[^>]*>", "", RegexOptions.None, RegexTimeout);
            text = WebUtility.HtmlDecode(text).Replace('\u00a0', ' ');
            return Regex.Replace(text, @"\n[ \t]*\n(?:[ \t]*\n)+", "\n\n", RegexOptions.None, RegexTimeout).Trim();
        }
        catch (RegexMatchTimeoutException) { return "This HTML-only message could not be converted safely to plain text."; }
    }

    internal static string DecodeHeader(string value) => Regex.Replace(value, @"=\?([^?]+)\?([bBqQ])\?([^?]*)\?=", match =>
    {
        try
        {
            var encoding = Encoding.GetEncoding(match.Groups[1].Value);
            var data = match.Groups[3].Value;
            if (match.Groups[2].Value.Equals("B", StringComparison.OrdinalIgnoreCase)) return encoding.GetString(Convert.FromBase64String(data));
            var bytes = new List<byte>();
            for (var i = 0; i < data.Length; i++)
            {
                if (data[i] == '=' && i + 2 < data.Length && byte.TryParse(data.Substring(i + 1, 2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var decoded)) { bytes.Add(decoded); i += 2; }
                else bytes.Add(data[i] == '_' ? (byte)' ' : (byte)data[i]);
            }
            return encoding.GetString(bytes.ToArray());
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException) { return match.Value; }
    }, RegexOptions.None, RegexTimeout);

    private static string String(JsonElement root, string property) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
