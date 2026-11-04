namespace Habitat.Features.Contacts;

internal static class PhoneLinkMessage
{
    internal static Uri RecipientUri(string address)
    {
        var phone = ContactBook.NormalizePhone(address);
        if (phone.Length == 0) throw new InvalidDataException("Choose a contact with a valid phone number without an extension.");
        return new("sms:" + phone);
    }
    // Windows activates Phone Link's SMS composer. This URI never requests sending.
    internal static Uri DraftUri(string address, string text)
    {
        var phone = ContactBook.NormalizePhone(address);
        if (phone.Length == 0) throw new InvalidDataException("Choose a contact with a valid phone number without an extension.");
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Write a message before opening Phone Link.");
        var uri = "sms:" + phone + "?body=" + Uri.EscapeDataString(text);
        if (uri.Length > 32000) throw new InvalidDataException("Shorten this message before opening Phone Link.");
        return new(uri);
    }
}
