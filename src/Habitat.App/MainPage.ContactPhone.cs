using Habitat.Features.Contacts;
using Habitat.Features.Destinations;
using Habitat.Features.Facebook;
using Habitat.Features.Composer;

namespace Habitat;

public sealed partial class MainPage
{
    private sealed record PhoneContactRecipient(string Name, string Address, FacebookContact Display);
    private readonly Dictionary<string, PhoneContactRecipient> _phoneRecipients = new();
    private PhoneLinkSection? _activePhoneSection;
    private bool IsPhoneContactDestination(string id) => _phoneRecipients.ContainsKey(id);

    private void SelectPhoneContact(ContactBook book, ContactPerson person, string address)
    {
        if (HasAppearanceOverlay || _updateHandoff || _operations.IsStopping) return;
        var phone = ContactBook.NormalizePhone(address);
        if (phone.Length == 0) throw new InvalidOperationException("Choose a contact with a valid phone number without an extension.");
        var id = "phone-contact:" + ContactBook.Hash(person.Id + "\n" + phone);
        var name = book.Name(person);
        var photo = book.For(person).SelectMany(r => r.Current.Values).FirstOrDefault(v => v.Kind == "photo" && FacebookContactsService.SafePhoto(v.Value))?.Value ?? "";
        _phoneRecipients[id] = new(name, phone, new(name, photo));
        _composerDrafts.Isolate(id);
        if (!_destinationSections.ContainsKey(id))
        {
            var section = new PhoneLinkSection(id, (text, token) => _services.SendPhoneLink(phone, text, token),
                (openIfNeeded, token) => _services.ReadPhoneLinkConversation(phone, openIfNeeded, token),
                token => _services.ReviewPhoneLink(phone, token),
                exception => _services.ReportError("Phone Link conversation", exception));
            section.StateChanged += () => { PresentDestinationSection(); UpdateWritingLayout(); UpdateSendState(); };
            section.ClearRequested += () =>
            {
                _composerDrafts.Edit(id, "");
                if (_destination == id) { RestoreComposerDraft(); FocusComposer(); }
            };
            _destinationSections.Add(id, section);
            foreach (var external in new[] { false, true })
                _composer.Register(id, external, SubmitPhoneMessageAsync, _ => section.IsBusy, exclusive: true);
        }
        AssociateContactChannel(id, person.Id, "phone", phone);
        SelectDestination(id);
        SetFacebookOpen(false);
        FocusComposer();
    }

    private async Task ViewPhoneConversationAsync(string personId, string address)
    {
        if (!ContactChannelsEnabled || HasAppearanceOverlay || _updateDialogOpen || _updateHandoff) return;
        var book = _services.Contacts.Book;
        var person = book.People.FirstOrDefault(person => person.Id == personId);
        if (person is null || !ContactChannels.For(book, person).Any(channel =>
            channel.Kind == "phone" && SameContactAddress("phone", channel.Address, address))) return;
        ContactProfile.Hide();
        SelectPhoneContact(book, person, address);
        if (_activePhoneSection is not { } section) return;
        using var focus = _services.KeepPhoneLinkBackground(_windowHost?.Handle ?? 0);
        await section.OpenConversationAsync();
        if (_destination == section.Destination) FocusComposer();
    }

    private async Task SubmitPhoneMessageAsync(ComposerCoordinator.Submission request)
    {
        var section = (PhoneLinkSection)_destinationSections[request.Destination];
        using var focus = _services.KeepPhoneLinkBackground(_windowHost?.Handle ?? 0);
        request.Accept();
        await section.SubmitAsync(request.Text);
        if (section.WasSubmitted) request.Clear();
        if (_destination == request.Destination)
        {
            FocusComposer();
        }
    }
}
