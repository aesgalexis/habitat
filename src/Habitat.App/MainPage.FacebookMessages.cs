using Habitat.Features.Composer;
using Habitat.Features.Destinations;
using Habitat.Features.Facebook;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Security.Cryptography;
using System.Text;

namespace Habitat;

public sealed partial class MainPage
{
    private readonly Dictionary<string, FacebookContact> _facebookRecipients = new();
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
    private readonly TextBlock ContactMessageChannel = new()
    {
        Text = "Facebook", FontSize = 12, MinHeight = 16, Opacity = 0.65,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
        Visibility = Visibility.Collapsed, IsHitTestVisible = false
    };
    private bool IsFacebookDestination(string id) => _facebookRecipients.ContainsKey(id);
    private static string FacebookRecipientId(FacebookContact contact) => "facebook-contact:" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contact.Link ?? contact.Name + "\n" + contact.PhotoUrl)));

    private void SelectFacebookContact(FacebookContact contact)
    {
        if (HasAppearanceOverlay || _updateHandoff || _operations.IsStopping) return;
        var id = FacebookRecipientId(contact);
        _facebookRecipients[id] = contact;
        _composerDrafts.Isolate(id);
        if (!_destinationSections.ContainsKey(id))
        {
            var section = new MessengerSection(id, (text, token) => _services.FacebookMessaging.SendAsync(contact.Link!, contact.Name, text, token));
            section.StateChanged += () => { PresentDestinationSection(); UpdateWritingLayout(); UpdateSendState(); };
            section.ClearRequested += () =>
            {
                _composerDrafts.Edit(id, "");
                if (_destination == id) { RestoreComposerDraft(); FocusComposer(); }
            };
            _destinationSections.Add(id, section);
            _composer.Register(id, false, SubmitFacebookMessageAsync,
                _ => section.IsBusy || contact.Link is null, exclusive: true);
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
        AutomationProperties.SetName(ContactMessageChannel, "Message channel: Facebook");
        surface.Children.Add(ContactMessageHeader);
        _root.Children.Add(ContactMessageChannel);
    }

    private void UpdateContactMessagePresentation()
    {
        var active = _facebookRecipients.TryGetValue(_destination, out var contact);
        ContactMessageHeader.Visibility = ContactMessageChannel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (!active) return;
        ContactMessageName.Text = contact!.Name;
        AutomationProperties.SetName(ContactMessageHeader, contact.Name + ", active message destination");
        if (!ReferenceEquals(_displayedContact, contact))
        {
            if (ContactMessageHeader.Children.Count > 1) ContactMessageHeader.Children.RemoveAt(1);
            var avatar = CreateFacebookAvatar(contact);
            avatar.Width = avatar.Height = 28;
            ((TextBlock)avatar.Children[1]).FontSize = 13;
            ContactMessageHeader.Children.Add(avatar);
            _displayedContact = contact;
        }
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
        ContactMessageHeader.Visibility = IsFacebookDestination(_destination) && _root.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PositionContactMessageIndicators()
    {
        ContactMessageChannel.HorizontalAlignment = SendButton.HorizontalAlignment;
        ContactMessageChannel.VerticalAlignment = SendButton.VerticalAlignment;
        ContactMessageChannel.Width = SendButton.ActualWidth > 0 ? SendButton.ActualWidth : double.NaN;
        ContactMessageChannel.Margin = SendButton.VerticalAlignment == VerticalAlignment.Bottom
            ? new Thickness(0, 0, SendButton.Margin.Right, Math.Max(0, SendButton.Margin.Bottom - 20))
            : new Thickness(0, SendButton.Margin.Top + (SendButton.ActualHeight > 0 ? SendButton.ActualHeight : 38) + 3, 0, 0);
    }

    private void PaintContactMessageHeader()
    {
        ContactMessageName.Foreground = ContactMessageChannel.Foreground = TextBrush();
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
