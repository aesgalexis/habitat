using Habitat.Features.Facebook;

namespace Habitat.Features.Contacts;

internal sealed record ContactChannelSelection(string PersonId, string Kind, string Address, string? Sender = null);
internal sealed record EmailContactRecipient(string Name, string Address, string? Sender, FacebookContact Display);
internal sealed record PhoneContactRecipient(string Name, string Address, FacebookContact Display);

// Owns recipient identity, channel preference, reconciliation and cache policy.
// Views and launchers never determine whether a saved recipient is still valid.
internal sealed class ContactDestinationSession
{
    internal const int MaximumInactiveViews = 32;
    private readonly Dictionary<string, EmailContactRecipient> _emails = new();
    internal IReadOnlyDictionary<string, EmailContactRecipient> Emails => _emails;
    private readonly Dictionary<string, PhoneContactRecipient> _phones = new();
    internal IReadOnlyDictionary<string, PhoneContactRecipient> Phones => _phones;
    private readonly Dictionary<string, FacebookContact> _messenger = new();
    internal IReadOnlyDictionary<string, FacebookContact> Messenger => _messenger;
    private readonly Dictionary<string, FacebookContact> _messengerRecipients = new();
    internal IReadOnlyDictionary<string, FacebookContact> MessengerRecipients => _messengerRecipients;
    private readonly Dictionary<string, FacebookContact> _unavailable = new();
    internal IReadOnlyDictionary<string, FacebookContact> Unavailable => _unavailable;
    private readonly Dictionary<string, ContactChannelSelection> _channels = new();
    internal IReadOnlyDictionary<string, ContactChannelSelection> Channels => _channels;
    private readonly Dictionary<string, ContactChannelSelection> _preferred = new();
    private readonly HashSet<string> _retired = [];
    private readonly Dictionary<string, long> _used = new();
    private long _revision;

    private IEnumerable<string> Ids => _emails.Keys.Concat(_phones.Keys).Concat(_messenger.Keys).Concat(_unavailable.Keys).Distinct();
    internal bool Contains(string id) => !_retired.Contains(id) &&
        (_emails.ContainsKey(id) || _phones.ContainsKey(id) || _messenger.ContainsKey(id) || _unavailable.ContainsKey(id));
    internal void Touch(string id)
    {
        if (!Contains(id)) return;
        _used[id] = ++_revision;
        if (_channels.TryGetValue(id, out var channel)) _preferred[channel.PersonId] = channel;
    }
    private string Added(string id) { _retired.Remove(id); Touch(id); return id; }
    internal static string PhoneId(string personId, string address) => "phone-contact:" + ContactBook.Hash(personId + "\n" + ContactBook.NormalizePhone(address));
    internal static string EmailId(string personId, string address, string? sender) => "email-contact:" +
        ContactBook.Hash(personId + "\n" + ContactBook.NormalizeEmail(address) + "\n" + sender?.ToUpperInvariant());
    internal static string MessengerId(FacebookContact contact, string? personId) => "facebook-contact:" + ContactBook.Hash(personId is null
        ? contact.Link ?? contact.Name + "\n" + contact.PhotoUrl : personId + "\n" + contact.Link);

    internal string RegisterPhone(ContactBook book, ContactPerson person, string address)
    {
        var phone = ContactBook.NormalizePhone(address);
        if (phone.Length == 0) throw new InvalidOperationException("Choose a contact with a valid phone number without an extension.");
        var id = PhoneId(person.Id, phone);
        var name = book.Name(person);
        _phones[id] = new(name, phone, new(name, Photo(book, person)));
        Associate(id, person.Id, "phone", phone);
        return Added(id);
    }

    internal string RegisterEmail(ContactBook book, ContactPerson person, string address, string? sender)
    {
        if (!ContactChannels.IsEmail(address) || sender is not null && !ContactChannels.IsEmail(sender))
            throw new InvalidOperationException("Choose a contact with a valid email address.");
        var id = EmailId(person.Id, address, sender);
        var name = book.Name(person);
        _emails[id] = new(name, address, sender, new(name, Photo(book, person)));
        Associate(id, person.Id, "email", address, sender);
        return Added(id);
    }

    internal string RegisterMessenger(FacebookContact contact, string? displayName, string? personId)
    {
        var id = MessengerId(contact, personId);
        _messenger[id] = displayName is null ? contact : contact with { Name = displayName };
        _messengerRecipients[id] = contact;
        if (personId is not null) Associate(id, personId, "messenger", contact.Link!);
        else _channels.Remove(id);
        return Added(id);
    }

    internal string RegisterUnavailable(ContactBook book, ContactPerson person)
    {
        var id = "contact-person:" + person.Id;
        _unavailable[id] = new(book.Name(person), "");
        return Added(id);
    }

    private void Associate(string id, string personId, string kind, string address, string? sender = null)
    {
        var selection = new ContactChannelSelection(personId, kind, address, sender);
        _channels[id] = selection;
        _preferred[personId] = selection;
    }

    internal (ContactChannel Channel, string? Sender)? Choose(ContactBook book, ContactPerson person, string[] senders, string? gmailSender)
    {
        var channels = ContactChannels.For(book, person);
        if (channels.Length == 0) return null;
        var preferred = _preferred.GetValueOrDefault(person.Id);
        var channel = channels.FirstOrDefault(c => c.Kind == preferred?.Kind && SameAddress(c.Kind, c.Address, preferred.Address)) ??
            channels.OrderBy(c => c.Kind == "phone" ? 0 : c.Kind == "email" ? 1 : 2).First();
        var sender = preferred?.Sender is { } previous && senders.Contains(previous, StringComparer.OrdinalIgnoreCase) ? previous :
            book.Sources.FirstOrDefault(s => s.Id == channel.SourceId && ContactChannels.IsEmail(s.Account))?.Account ?? gmailSender ?? senders.FirstOrDefault();
        return (channel, sender);
    }

    internal string[] Reconcile(ContactBook book)
    {
        var people = book.People.ToDictionary(person => person.Id);
        var removed = new HashSet<string>();
        foreach (var (id, selection) in _channels.ToArray())
        {
            if (!people.TryGetValue(selection.PersonId, out var person) || !HasChannel(person, selection))
            { removed.Add(id); continue; }
            var name = book.Name(person);
            if (_emails.TryGetValue(id, out var email)) _emails[id] = email with { Name = name, Display = email.Display with { Name = name } };
            if (_phones.TryGetValue(id, out var phone)) _phones[id] = phone with { Name = name, Display = phone.Display with { Name = name } };
            if (_messenger.TryGetValue(id, out var messenger))
            {
                _messenger[id] = messenger with { Name = name };
                if (book.For(person).All(r => book.Source(r).Kind == "local"))
                    _messengerRecipients[id] = _messengerRecipients[id] with { Name = name };
            }
        }
        foreach (var id in _unavailable.Keys.ToArray())
        {
            var person = book.People.FirstOrDefault(p => "contact-person:" + p.Id == id);
            if (person is null || ContactChannels.For(book, person).Length > 0) removed.Add(id);
            else _unavailable[id] = _unavailable[id] with { Name = book.Name(person) };
        }
        foreach (var (personId, selection) in _preferred.ToArray())
            if (!people.TryGetValue(personId, out var person) || !HasChannel(person, selection)) _preferred.Remove(personId);
        foreach (var id in removed) Invalidate(id);
        return removed.ToArray();

        bool HasChannel(ContactPerson person, ContactChannelSelection selection) => ContactChannels.For(book, person)
            .Any(channel => channel.Kind == selection.Kind && SameAddress(channel.Kind, channel.Address, selection.Address));
    }

    internal string[] EvictionCandidates(string selected, Func<string, bool> protect)
    {
        var inactive = Ids.Where(id => id != selected && !_retired.Contains(id)).ToArray();
        var excess = inactive.Length - MaximumInactiveViews;
        return excess <= 0 ? [] : inactive.Where(id => !protect(id)).OrderBy(id => _used.GetValueOrDefault(id)).Take(excess).ToArray();
    }

    internal void Invalidate(string id) => _retired.Add(id);
    internal void Forget(string id)
    {
        _emails.Remove(id); _phones.Remove(id); _messenger.Remove(id); _messengerRecipients.Remove(id); _unavailable.Remove(id);
        _channels.Remove(id);
        _retired.Remove(id); _used.Remove(id);
    }

    internal static bool SameAddress(string kind, string left, string right) => kind == "phone"
        ? ContactBook.NormalizePhone(left) == ContactBook.NormalizePhone(right) : kind == "messenger"
        ? (ContactBook.FacebookProfileIdentity(left) ?? left) == (ContactBook.FacebookProfileIdentity(right) ?? right)
        : ContactBook.NormalizeEmail(left) == ContactBook.NormalizeEmail(right);

    private static string Photo(ContactBook book, ContactPerson person) => book.For(person).SelectMany(r => r.Current.Values)
        .FirstOrDefault(v => v.Kind == "photo" && FacebookContactsService.SafePhoto(v.Value))?.Value ?? "";
}
