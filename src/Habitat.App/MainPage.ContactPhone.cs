using Habitat.Features.Contacts;
using Habitat.Features.Destinations;
using Habitat.Features.Facebook;
using Habitat.Features.Composer;

namespace Habitat;

public sealed partial class MainPage
{
    private IReadOnlyDictionary<string, PhoneContactRecipient> _phoneRecipients => _contactDestinations.Phones;
    private PhoneLinkSection? _activePhoneSection;
    private bool IsPhoneContactDestination(string id) => _contactDestinations.Contains(id) && _phoneRecipients.ContainsKey(id);

    private void SelectPhoneContact(ContactBook book, ContactPerson person, string address)
    {
        if (HasAppearanceOverlay || _updateHandoff || _operations.IsStopping) return;
        if (_contactDestinationViews.IsRetiring(ContactDestinationSession.PhoneId(person.Id, address))) return;
        var id = _contactDestinations.RegisterPhone(book, person, address);
        var phone = _phoneRecipients[id].Address;
        _composerDrafts.Isolate(id);
        if (!_destinationSections.ContainsKey(id))
        {
            var section = new PhoneLinkSection(id, (text, token) => _services.SendPhoneLink(phone, text, token),
                (openIfNeeded, token) => _services.ReadPhoneLinkConversation(phone, openIfNeeded, token),
                token => _services.ReviewPhoneLink(phone, token),
                exception => _services.ReportError("Phone Link conversation", exception), _contactDestinationViews.TakePhoneReview(id));
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
        SelectDestination(id);
        SetFacebookOpen(false);
        FocusComposer();
    }

    private async Task ViewPhoneConversationAsync(string personId, string address)
    {
        if (!ContactChannelsEnabled || HasAppearanceOverlay || _updateDialogOpen || _updateHandoff) return;
        var book = _services.Contacts.Book;
        var person = book.People.FirstOrDefault(person => person.Id == personId);
        if (person is null) return;
        var channel = ContactChannels.For(book, person).FirstOrDefault(channel =>
            channel.Kind == "phone" && SameContactAddress("phone", channel.Address, address));
        if (channel is null) return;
        await OpenContactChannelAsync(book, person, channel);
        var id = ContactDestinationSession.PhoneId(personId, channel.Address);
        if (_destination != id || _activePhoneSection is not { } section || section.Destination != id ||
            !ContactChannelsEnabled || _updateDialogOpen || _updateHandoff) return;
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
