using Habitat.Features.Contacts;
using Habitat.Features.Destinations;
using Habitat.Features.Facebook;
using Habitat.Models;
using Habitat.Services;

namespace Habitat;

public sealed partial class MainPage
{
    private IReadOnlyDictionary<string, EmailContactRecipient> _emailRecipients => _contactDestinations.Emails;
    private Func<string, string, string?, CancellationToken, Task<GmailDraft>>? _saveContactEmailDraft = null;
    private bool IsEmailContactDestination(string id) => _contactDestinations.Contains(id) && _emailRecipients.ContainsKey(id);
    private bool IsContactDestination(string id) => _contactDestinations.Contains(id);

    private void SelectEmailContact(ContactBook book, ContactPerson person, string address, string? sender)
    {
        if (HasAppearanceOverlay || _updateHandoff || _operations.IsStopping) return;
        if (_contactDestinationViews.IsRetiring(ContactDestinationSession.EmailId(person.Id, address, sender))) return;
        var id = _contactDestinations.RegisterEmail(book, person, address, sender);
        _composerDrafts.Isolate(id);
        if (!_destinationSections.ContainsKey(id))
        {
            var section = new GmailDraftSection((text, token) =>
            {
                var recipient = _emailRecipients[id];
                return _saveContactEmailDraft is { } save ? save(text, recipient.Address, recipient.Sender, token)
                    : _gmailSession.CreateContactDraftAsync(text, recipient.Address, recipient.Sender, token);
            }, AuthorizeGmailDraftsAsync, () => _emailRecipients[id].Sender ?? _gmailAuth?.Email,
                uri => _launchBrowser(uri), exception => _services.ReportError("Contact email draft", exception), id);
            section.StateChanged += () => { PresentDestinationSection(); UpdateWritingLayout(); UpdateSendState(); };
            section.ClearRequested += () =>
            {
                _composerDrafts.Edit(id, "");
                if (_destination == id) { RestoreComposerDraft(); FocusComposer(); }
            };
            _destinationSections.Add(id, section);
            _composer.Register(id, false, SubmitSectionAsync, _ => section.IsBusy);
            _composer.Register(id, true, _ => section.OpenBrowserAsync(), _ => section.IsBusy);
        }
        SelectDestination(id);
        SetFacebookOpen(false);
        FocusComposer();
    }
}
