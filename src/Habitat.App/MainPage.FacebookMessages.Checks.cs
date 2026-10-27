#if HABITAT_UI_CHECKS
using Habitat.Features.Facebook;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckFacebookMessagingUiAsync(string log)
    {
        var service = _services.FacebookMessaging;
        var contacts = _services.Facebook;
        var destination = _destination;
        var draft = Composer.Text;
        var fake = new CheckedFacebookMessaging();
        var a = new FacebookContact("Synthetic recipient A ☕", "https://scontent.fbcdn.net/a.jpg", "https://www.facebook.com/messages/t/synthetic.a");
        var b = new FacebookContact("Synthetic recipient B 雪", "https://scontent.fbcdn.net/b.jpg", "https://www.facebook.com/messages/t/synthetic.b");
        try
        {
            _services.FacebookMessaging = fake;
            _services.Facebook = new CheckedFacebook();
            foreach (var dark in new[] { false, true })
            {
                RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
                PaintSurface();
                var hover = ((SolidColorBrush)FacebookButton.Resources["ButtonBackgroundPointerOver"]).Color;
                if (hover != ((SolidColorBrush)TasksButton.Resources["ButtonBackgroundPointerOver"]).Color || hover.A == 0 ||
                    !ReferenceEquals(FacebookButton.Template, TasksButton.Template) ||
                    FacebookButton.CornerRadius != TasksButton.CornerRadius)
                    throw new InvalidOperationException("The people icon must share the other sidebar widgets' hover colors, template and corner radius.");
            }
            SetFacebookOpen(true);
            await WaitForUpdateUiAsync(() => !_facebookBusy);
            AddFacebookAvatar(a);
            ((IInvokeProvider)new ButtonAutomationPeer((Button)FacebookAvatars.Children.Last()).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(40);
            var aId = _destination;
            if (Composer.Text != "" || ComposerHint.Text != "Message Synthetic recipient A ☕…" ||
                SendButton.Content as string != "Send message" || _destinationButtons.ContainsKey(aId) ||
                DestinationRail.Children.Count != _destinationCatalog.Enabled.Count ||
                _destinationSections[aId].HasContent || Composer.TextAlignment != TextAlignment.Center || fake.Calls != 0 ||
                _facebookPanelOpen || FacebookSidebar.Visibility != Visibility.Collapsed ||
                ContactMessageHeader.Visibility != Visibility.Visible || ContactMessageName.Text != a.Name ||
                ContactMessageChannel.Visibility != Visibility.Visible || ContactMessageChannel.Text != "Facebook")
                throw new InvalidOperationException("Selecting a contact must collapse its sidebar, retain its header and channel, and enter its centered destination without sending.");
            CheckContactMessageIndicatorLayout();
            var surface = (Grid)Content;
            var surfaceWidth = surface.Width;
            var sidebar = _sidebarManualExpanded;
            try
            {
                surface.Width = 1900;
                foreach (var expanded in new[] { false, true })
                {
                    _sidebarManualExpanded = expanded;
                    UpdateWritingLayout();
                    surface.UpdateLayout();
                    CheckContactMessageIndicatorLayout();
                    RebuildDestinationRail();
                    if (_destinationButtons.Keys.Any(IsFacebookDestination) ||
                        DestinationRail.Children.Count != _destinationCatalog.Enabled.Count)
                        throw new InvalidOperationException("Contacts must stay out of the right destination rail after rebuilding it.");
                }
            }
            finally
            {
                surface.Width = surfaceWidth;
                _sidebarManualExpanded = sidebar;
                UpdateWritingLayout();
                surface.UpdateLayout();
            }
            Composer.Text = "Synthetic draft A · never sent to a real browser";
            var textA = Composer.Text;
            SetFacebookOpen(true);
            await WaitForUpdateUiAsync(() => !_facebookBusy);
            AddFacebookAvatar(b);
            ((IInvokeProvider)new ButtonAutomationPeer((Button)FacebookAvatars.Children.Last()).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(40);
            var bId = _destination;
            if (Composer.Text.Length != 0 || _facebookPanelOpen || ContactMessageName.Text != b.Name ||
                _destinationButtons.Keys.Any(IsFacebookDestination))
                throw new InvalidOperationException("Changing recipients must collapse the panel, update the header and isolate drafts.");
            Composer.Text = "Synthetic draft B";
            SelectDestination(aId);
            if (Composer.Text != textA || fake.Calls != 0) throw new InvalidOperationException("Selecting destinations must preserve drafts without sending.");
            fake.Result = new("not-sent", "recipient");
            await LaunchDestinationAsync(aId, Composer.Text);
            if (Composer.Text != textA || fake.Link != a.Link || fake.Text != textA || !_destinationSections[aId].HasContent)
                throw new InvalidOperationException("A rejected synthetic submission must preserve the recipient's draft.");
            CheckContactMessageIndicatorLayout();
            fake.Result = new("uncertain");
            await LaunchDestinationAsync(aId, Composer.Text);
            if (Composer.Text != textA) throw new InvalidOperationException("An uncertain result must retain the draft for review.");
            fake.Result = new("submitted");
            await LaunchDestinationAsync(aId, Composer.Text);
            if (Composer.Text != "" || fake.Calls != 3) throw new InvalidOperationException("Only confirmed Messenger submission clears that recipient's draft, without automatic retries.");
            SelectDestination(bId);
            if (Composer.Text != "Synthetic draft B") throw new InvalidOperationException("Another recipient's draft changed during submission.");
            SelectFacebookContact(new("No verified address", "https://scontent.fbcdn.net/unknown.jpg"));
            if (SendButton.IsEnabled || fake.Calls != 3) throw new InvalidOperationException("A contact without a verified link must not admit a send.");
            SelectDestination(destination);
            if (Composer.Text != draft || ContactMessageHeader.Visibility != Visibility.Collapsed || ContactMessageChannel.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Regular destinations must restore their draft and hide contact-specific indicators.");
            File.AppendAllText(log, "PASS: people widget native hover; selected contact anchored beside collapsed/expanded sidebar on wide windows, absent from the right destination rail after selection/rebuild; Facebook below Send message in welcome/results; isolated drafts, no send on selection, rejection/uncertainty preservation, clearing on confirmed submission only; messaging fully intercepted, no real messages\n");
        }
        finally
        {
            _services.FacebookMessaging = service;
            SetFacebookOpen(false);
            _services.Facebook = contacts;
            SelectDestination(destination);
            foreach (var id in _facebookRecipients.Keys.ToArray())
                if (_destinationSections.Remove(id, out var section)) { await section.StopAsync(); section.Dispose(); }
            _facebookRecipients.Clear();
            FacebookAvatars.Children.Clear();
            RebuildDestinationRail(); UpdateDestinationPresentation();
        }
    }

    private void CheckContactMessageIndicatorLayout()
    {
        UpdateWritingLayout();
        var surface = (Grid)Content;
        surface.UpdateLayout();
        var header = ContactMessageHeader.TransformToVisual(surface).TransformPoint(new());
        var sidebarRight = _widgets.TransformToVisual(surface).TransformPoint(new(_widgets.ActualWidth, 0)).X;
        var label = ContactMessageChannel.TransformToVisual(_root).TransformPoint(new());
        var send = SendButton.TransformToVisual(_root).TransformPoint(new(0, SendButton.ActualHeight));
        if (!ReferenceEquals(ContactMessageHeader.Parent, surface) || Math.Abs(header.X - sidebarRight - FacebookRailGap) > 1 ||
            header.Y < 0 || header.Y + ContactMessageHeader.ActualHeight > 100 ||
            ContactMessageName.ActualWidth <= 0 || header.X + ContactMessageHeader.ActualWidth >
                surface.ActualWidth - surface.Padding.Right - DestinationRailInset + 1 ||
            label.Y < send.Y + 1 || label.Y + ContactMessageChannel.ActualHeight > _root.ActualHeight + 1 ||
            Math.Abs(label.X - send.X) > 1 || ToolTipService.GetToolTip(ContactMessageHeader) is not null)
            throw new InvalidOperationException($"Contact indicators must stay beside the sidebar and place the channel directly below Send in each composer state. Header={header}; SidebarRight={sidebarRight}; Label={label}; Send={send}; Root={_root.ActualWidth}x{_root.ActualHeight}.");
    }

    private sealed class CheckedFacebookMessaging : IFacebookMessagingService
    {
        public int Calls;
        public string? Link, Text;
        public FacebookMessageResult Result = new("not-sent", "unsupported");
        public Task<FacebookMessageResult> SendAsync(string targetLink, string targetName, string text, CancellationToken token)
        { Calls++; Link = targetLink; Text = text; return Task.FromResult(Result); }
    }
}
#endif
