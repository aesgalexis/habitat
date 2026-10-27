using Habitat.Features.Facebook;
using System.Net.Mail;

namespace Habitat.Features.Contacts;

internal sealed record ContactChannel(string Kind, string Address, string Label, string SourceId);
internal static class ContactChannels
{
    internal static ContactChannel[] For(ContactBook book, ContactPerson person) => book.For(person)
        .OrderByDescending(record => book.Source(record).Connected).ThenByDescending(record => record.Current.ObservedAt)
        .SelectMany(record => record.Current.Values.Select(value => (record, value)))
        .Where(x => x.value.Kind == "email" && IsEmail(x.value.Value) ||
            x.value.Kind == "messenger" && FacebookContactsService.SafeLink(x.value.Value))
        .Select(x => new ContactChannel(x.value.Kind, x.value.Value,
            x.value.Kind == "email" ? "Open email draft" : "Message in Messenger", x.record.SourceId))
        .DistinctBy(c => (c.Kind, c.Kind == "messenger" ? ContactBook.FacebookProfileIdentity(c.Address) ?? c.Address :
            ContactBook.NormalizeEmail(c.Address))).ToArray();

    internal static bool IsEmail(string value) => value.Length <= 320 && !value.Any(char.IsControl) &&
        MailAddress.TryCreate(value, out var address) && address.Address.Equals(value, StringComparison.OrdinalIgnoreCase);

    internal static Uri GmailDraft(string email, string? sender, string body = "")
    {
        if (!IsEmail(email)) throw new InvalidDataException("This contact has no valid email address.");
        if (sender is not null && !IsEmail(sender)) throw new InvalidDataException("Choose a valid sender account.");
        return new("https://mail.google.com/mail/?" + (sender is null ? "" : "authuser=" + Uri.EscapeDataString(sender) + "&") +
            "view=cm&fs=1&to=" + Uri.EscapeDataString(email) + "&body=" + Uri.EscapeDataString(body));
    }
}
