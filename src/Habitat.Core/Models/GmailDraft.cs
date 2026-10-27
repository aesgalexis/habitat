using System.Text;
using System.Text.RegularExpressions;
using System.Net.Mail;

namespace Habitat.Models;

internal sealed record GmailDraft(string Id, string Subject, string RfcMessageId)
{
    // Eight opening words keep the subject short without changing the body.
    internal static string SubjectFrom(string text) => string.Join(" ",
        Regex.Matches(text, @"\S+").Cast<Match>().Take(8).Select(match => match.Value));

    internal static string EncodeMessage(string text, string subject, string rfcMessageId, string? recipient = null)
    {
        if (recipient is not null && (recipient.Length > 320 || recipient.Any(char.IsControl) ||
            !MailAddress.TryCreate(recipient, out var address) || address.Address != recipient))
            throw new InvalidDataException("Choose a valid email recipient.");
        // Fold encoded words so Unicode subjects (including long opening words)
        // remain valid MIME headers. Never interpolate user text as raw headers.
        var words = new List<string>();
        var chunk = new StringBuilder();
        foreach (var rune in subject.EnumerateRunes())
        {
            if (Encoding.UTF8.GetByteCount(chunk.ToString()) + rune.Utf8SequenceLength > 42)
            {
                words.Add("=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(chunk.ToString())) + "?=");
                chunk.Clear();
            }
            chunk.Append(rune.ToString());
        }
        if (chunk.Length > 0) words.Add("=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(chunk.ToString())) + "?=");
        var mime = "MIME-Version: 1.0\r\nMessage-ID: <" + rfcMessageId + ">\r\n" +
            (recipient is null ? "" : "To: " + recipient + "\r\n") + "Subject: " + string.Join("\r\n ", words) +
            "\r\nContent-Type: text/plain; charset=UTF-8\r\nContent-Transfer-Encoding: base64\r\n\r\n" +
            Convert.ToBase64String(Encoding.UTF8.GetBytes(text), Base64FormattingOptions.InsertLineBreaks);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(mime)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public Uri BrowserUri(string? email) => new("https://mail.google.com/mail/?authuser=" + Uri.EscapeDataString(email ?? "") +
        "#search/" + Uri.EscapeDataString("rfc822msgid:" + RfcMessageId));
    public static Uri DraftsUri(string? email) => new("https://mail.google.com/mail/?authuser=" + Uri.EscapeDataString(email ?? "") + "#drafts");
}

internal sealed class GmailDraftAuthorizationException(string message) : Exception(message);
