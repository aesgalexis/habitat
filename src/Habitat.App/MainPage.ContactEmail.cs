using Habitat.Features.Contacts;
using Habitat.Features.Destinations;
using Habitat.Features.Facebook;
using Habitat.Models;
using Habitat.Services;

namespace Habitat;

public sealed partial class MainPage
{
    private sealed record EmailContactRecipient(string Name, string Address, string? Sender, FacebookContact Display);
    private readonly Dictionary<string, EmailContactRecipient> _emailRecipients = new();
    private Func<string, string, string?, CancellationToken, Task<GmailDraft>>? _saveContactEmailDraft = null;
    private bool IsEmailContactDestination(string id) => _emailRecipients.ContainsKey(id);
    private bool IsContactDestination(string id) => IsFacebookDestination(id) || IsEmailContactDestination(id);

    private void SelectEmailContact(ContactBook book, ContactPerson person, string address, string? sender)
    {
        if (HasAppearanceOverlay || _updateHandoff || _operations.IsStopping) return;
        if (!ContactChannels.IsEmail(address) || ContactChannels.IsNoReply(address) ||
            sender is not null && !ContactChannels.IsEmail(sender))
            throw new InvalidOperationException("Choose a contact with a replyable email address.");
        var id = "email-contact:" + ContactBook.Hash(ContactBook.NormalizeEmail(address) + "\n" + sender?.ToUpperInvariant());
        var name = book.Name(person);
        var photo = book.For(person).SelectMany(r => r.Current.Values).FirstOrDefault(v => v.Kind == "photo" && FacebookContactsService.SafePhoto(v.Value))?.Value ?? "";
        _emailRecipients[id] = new(name, address, sender, new(name, photo));
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
