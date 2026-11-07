using Habitat.Features.Composer;
using Habitat.Features.Destinations;
using Habitat.Features.Facebook;
using Habitat.Features.Contacts;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Habitat;

public sealed partial class MainPage
{
    private IReadOnlyDictionary<string, FacebookContact> _facebookRecipients => _contactDestinations.Messenger;
    private IReadOnlyDictionary<string, FacebookContact> _facebookSendRecipients => _contactDestinations.MessengerRecipients;
    private FacebookContact? _displayedContact;
    private readonly Grid ContactMessageHeader = new()
    {
        Height = 40, ColumnSpacing = 8, Margin = new Thickness(0, 20, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        Visibility = Visibility.Collapsed
    };
    private readonly TextBlock ContactMessageName = new()
    {
        FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, MaxLines = 1,
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
    };
    private bool IsFacebookDestination(string id) => _contactDestinations.Contains(id) && _facebookRecipients.ContainsKey(id);
    private static string FacebookRecipientId(FacebookContact contact) => ContactDestinationSession.MessengerId(contact, null);
    private void SelectFacebookContact(FacebookContact contact, string? displayName = null, string? personId = null)
    {
        if (HasAppearanceOverlay || _updateHandoff || _operations.IsStopping) return;
        if (_contactDestinationViews.IsRetiring(ContactDestinationSession.MessengerId(contact, personId))) return;
        var id = _contactDestinations.RegisterMessenger(contact, displayName, personId);
        _composerDrafts.Isolate(id);
        if (!_destinationSections.ContainsKey(id))
        {
            var section = new MessengerSection(id, (text, token) =>
            {
                var recipient = _facebookSendRecipients[id];
                return _services.FacebookMessaging.SendAsync(recipient.Link!, recipient.Name, text, token);
            });
            section.StateChanged += () => { PresentDestinationSection(); UpdateWritingLayout(); UpdateSendState(); };
            section.ClearRequested += () =>
            {
                _composerDrafts.Edit(id, "");
                if (_destination == id) { RestoreComposerDraft(); FocusComposer(); }
            };
            _destinationSections.Add(id, section);
            _composer.Register(id, false, SubmitFacebookMessageAsync,
                _ => section.IsBusy || _facebookSendRecipients[id].Link is null || !_services.FacebookMessaging.IsAvailable, exclusive: true);
        }
        SelectDestination(id);
        SetFacebookOpen(false);
        FocusComposer();
    }

    private void BuildContactMessageIndicators(Grid surface)
    {
        ContactMessageHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ContactMessageHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(ContactMessageName, 1);
        ContactMessageHeader.Children.Add(ContactMessageName);
        surface.Children.Add(ContactMessageHeader);
        _root.Children.Add(ContactChannelPicker);
        AutomationProperties.SetName(ContactChannelPicker, "Contact channels");
    }

    private void UpdateContactMessagePresentation()
    {
        var facebook = _facebookRecipients.TryGetValue(_destination, out var contact);
        var email = _emailRecipients.GetValueOrDefault(_destination);
        var phone = _phoneRecipients.GetValueOrDefault(_destination);
        contact ??= email?.Display ?? phone?.Display ?? _unavailableContacts.GetValueOrDefault(_destination);
        var active = contact is not null;
        ContactMessageHeader.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        UpdateContactChannelPicker();
        if (!active) return;
        ContactMessageName.Text = contact!.Name;
        var address = email?.Address ?? phone?.Address;
        AutomationProperties.SetName(ContactMessageHeader, contact.Name + (address is null ? "" : ", " + address) + ", active message destination");
        if (!ReferenceEquals(_displayedContact, contact))
        {
            if (ContactMessageHeader.Children.Count > 1) ContactMessageHeader.Children.RemoveAt(1);
            var avatar = CreateFacebookAvatar(contact);
            avatar.Width = avatar.Height = 28;
            ((TextBlock)avatar.Children[1]).FontSize = 13;
            ContactMessageHeader.Children.Add(avatar);
            _displayedContact = contact;
        }
        var book = _services.Contacts.Book;
        var personId = _contactChannelSelections.GetValueOrDefault(_destination)?.PersonId;
        var person = book.People.FirstOrDefault(p => p.Id == personId || "contact-person:" + p.Id == _destination);
        var hasFacebook = facebook || person is not null && book.For(person).Any(r => book.Source(r).Kind == "facebook" ||
            r.Current.Values.Any(v => v.Kind == "messenger"));
        var displayedAvatar = ContactMessageHeader.Children.OfType<Grid>().Single();
        displayedAvatar.Children[2].Visibility = hasFacebook ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PositionContactMessageHeader(Grid surface)
    {
        // The writing column is centered and capped at 867 px. Anchor the
        // recipient to the sidebar instead, including on wide windows.
        var left = _widgets.Width + FacebookRailGap +
            (_facebookPanelOpen ? FacebookSidebar.Width + FacebookRailGap : 0);
        ContactMessageHeader.Margin = new Thickness(left, 20, 0, 0);
        ContactMessageHeader.Width = Math.Min(280, Math.Max(0,
            surface.ActualWidth - surface.Padding.Left - surface.Padding.Right - DestinationRailInset - left));
        ContactMessageHeader.Visibility = IsContactDestination(_destination) && _root.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PositionContactMessageIndicators()
    {
        ContactChannelPicker.HorizontalAlignment = SendButton.HorizontalAlignment;
        ContactChannelPicker.VerticalAlignment = SendButton.VerticalAlignment;
        ContactChannelPicker.MaxWidth = Math.Max(120, _root.ActualWidth);
        ContactChannelPicker.Margin = SendButton.VerticalAlignment == VerticalAlignment.Bottom
            ? new Thickness(0, 0, SendButton.Margin.Right, 8)
            : new Thickness(0, SendButton.Margin.Top + (SendButton.ActualHeight > 0 ? SendButton.ActualHeight : 38) + 5, 0, 0);
    }

    private void PaintContactMessageHeader()
    {
        ContactMessageName.Foreground = TextBrush();
        PaintContactChannelPicker();
        if (ContactMessageHeader.Children.OfType<Grid>().FirstOrDefault() is not { } avatar) return;
        ((TextBlock)avatar.Children[1]).Foreground = TextBrush();
        if (((Ellipse)avatar.Children[0]).Fill is SolidColorBrush) ((Ellipse)avatar.Children[0]).Fill = FacebookAvatarFallback();
        PaintFacebookPresence((Ellipse)avatar.Children[2]);
    }

    private async Task SubmitFacebookMessageAsync(ComposerCoordinator.Submission request)
    {
        var section = (MessengerSection)_destinationSections[request.Destination];
        request.Accept();
        await section.SubmitAsync(request.Text);
        if (section.WasSubmitted) request.Clear();
        if (_destination == request.Destination) FocusComposer();
    }
}
