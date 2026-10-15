using System.Text.Json;
using System.Text.RegularExpressions;

namespace Habitat.Models;

internal sealed record GmailClientConfiguration(string ClientId, string ClientSecret)
{
    public static GmailClientConfiguration FromJson(string json)
    {
        if (json.Length > 65536) throw new InvalidDataException("The Google client file is too large.");
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("installed", out var installed))
            throw new InvalidDataException("Choose a Google OAuth client of type Desktop app.");
        var id = installed.TryGetProperty("client_id", out var idValue) ? idValue.GetString() : null;
        var secret = installed.TryGetProperty("client_secret", out var secretValue) ? secretValue.GetString() : null;
        if (id is null || !Regex.IsMatch(id, @"^[A-Za-z0-9_-]+\.apps\.googleusercontent\.com$", RegexOptions.CultureInvariant) ||
            string.IsNullOrWhiteSpace(secret))
            throw new InvalidDataException("This file does not contain a valid Google desktop client.");
        return new GmailClientConfiguration(id, secret);
    }
}

internal sealed record GmailTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
internal sealed record GmailAccount(GmailClientConfiguration? Client, GmailTokens? Tokens, string? Email);
internal sealed record GmailMessage(string Id, string ThreadId, string Sender, string Subject, string Preview,
    DateTimeOffset Date, string? Body = null, string? RfcMessageId = null);

internal static class GmailWebLink
{
    public static Uri ForMessage(string email, GmailMessage message)
    {
        var root = "https://mail.google.com/mail/?authuser=" + Uri.EscapeDataString(email);
        var rfcId = message.RfcMessageId?.Trim().Trim('<', '>');
        // Search by the original Message-ID rather than assuming Gmail's web
        // identifiers are identical to its API identifiers.
        if (!string.IsNullOrEmpty(rfcId) && rfcId.Length <= 998 && !rfcId.Any(char.IsWhiteSpace) && !rfcId.Any(char.IsControl))
            return new Uri(root + "#search/" + Uri.EscapeDataString("rfc822msgid:" + rfcId));
        var thread = string.IsNullOrEmpty(message.ThreadId) ? message.Id : message.ThreadId;
        if (!Regex.IsMatch(thread, @"^[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Invalid Gmail message identifier.");
        return new Uri(root + "#all/" + Uri.EscapeDataString(thread));
    }
}
internal sealed record GmailInbox(IReadOnlyList<GmailMessage> Messages, int UnreadCount, bool CountIsEstimate,
    string? NextPageToken, DateTimeOffset UpdatedAt);

internal sealed class GmailSignInRequiredException(string message) : Exception(message);
