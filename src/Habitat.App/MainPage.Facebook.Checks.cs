#if HABITAT_UI_CHECKS
using Habitat.Features.Facebook;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckFacebookUiAsync(string log)
    {
        var service = _services.Facebook;
        var photo = _services.FacebookPhoto;
        var launch = _launchBrowser;
        var draft = Composer.Text;
        var sidebar = _sidebarManualExpanded;
        var theme = RequestedTheme;
        var destination = _destination;
        var fake = new CheckedFacebook();
        try
        {
            _services.Facebook = fake;
            _services.FacebookPhoto = _ => new Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap(1, 1);
            Composer.Text = "Synthetic draft preserved while viewing contacts";
            var expectedDraft = Composer.Text;
            Uri? opened = null;
            _launchBrowser = uri => { opened = uri; return Task.FromResult(true); };
            SetFacebookOpen(true);
            await WaitForUpdateUiAsync(() => !_facebookBusy);
            if (FacebookRail.Visibility != Visibility.Visible || FacebookAvatars.Children.Count != 2 ||
                AutomationProperties.GetName(FacebookButton) != "Hide Facebook contacts" || _destination != destination || Composer.Text != expectedDraft)
                throw new InvalidOperationException("Facebook must open an independent avatar rail while preserving the destination and draft.");
            foreach (var dark in new[] { false, true })
            foreach (var expanded in new[] { false, true, false })
            {
                RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
                PaintSurface();
                _sidebarManualExpanded = expanded;
                UpdateWritingLayout();
                await Task.Delay(40);
                var railPosition = FacebookRail.TransformToVisual(this).TransformPoint(new());
                var widgetPosition = _widgets.TransformToVisual(this).TransformPoint(new());
                var foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)FacebookIcon.Fill;
                var mailGlyph = (FontIcon)((StackPanel)GmailButton.Content).Children[0];
                var taskGlyph = (FontIcon)TasksSymbol.Children[0];
                var facebookCenter = FacebookIcon.TransformToVisual(this).TransformPoint(new(FacebookIcon.ActualWidth / 2, FacebookIcon.ActualHeight / 2));
                var mailCenter = mailGlyph.TransformToVisual(this).TransformPoint(new(mailGlyph.ActualWidth / 2, mailGlyph.ActualHeight / 2));
                var taskCenter = taskGlyph.TransformToVisual(this).TransformPoint(new(taskGlyph.ActualWidth / 2, taskGlyph.ActualHeight / 2));
                var facebookRow = FacebookButton.TransformToVisual(this).TransformPoint(new());
                var mailRow = GmailButton.TransformToVisual(this).TransformPoint(new());
                var taskRow = TasksButton.TransformToVisual(this).TransformPoint(new());
                if (FacebookButton.Width != _widgets.Width || FacebookRail.ActualWidth != 40 ||
                    FacebookSidebar.Visibility != Visibility.Visible || !ReferenceEquals(FacebookSidebar.Parent, Content) ||
                    Math.Abs(railPosition.X - (widgetPosition.X + _widgets.ActualWidth + 12)) > 0.5 ||
                    Math.Abs(facebookCenter.X - mailCenter.X) > 0.5 || Math.Abs(facebookCenter.X - taskCenter.X) > 0.5 ||
                    Math.Abs(facebookCenter.Y - facebookRow.Y - (taskCenter.Y - taskRow.Y)) > 0.5 ||
                    Math.Abs(facebookCenter.Y - facebookRow.Y - (mailCenter.Y - mailRow.Y)) > 0.5 ||
                    foreground.Color != ((Microsoft.UI.Xaml.Media.SolidColorBrush)TextBrush()).Color ||
                    ((Microsoft.UI.Xaml.Media.SolidColorBrush)FacebookButton.Background).Color.A != 0 ||
                    _root.Margin.Left < FacebookSidebar.Margin.Left + FacebookSidebar.Width ||
                    FacebookTitle.Visibility != (expanded ? Visibility.Visible : Visibility.Collapsed))
                    throw new InvalidOperationException($"Facebook must align with Gmail and Tasks and open an independent compact sidebar. Centers: Facebook={facebookCenter}, Gmail={mailCenter}, Tasks={taskCenter}; row offsets: {facebookCenter.Y - facebookRow.Y}, {mailCenter.Y - mailRow.Y}, {taskCenter.Y - taskRow.Y}.");
            }
            var contact = FacebookAvatars.Children[0] as Button;
            if (contact is null || AutomationProperties.GetName(contact) != "Amiga ☕" ||
                ToolTipService.GetToolTip(contact) as string != "Amiga ☕" || FacebookAvatars.Children[1] is Button)
                throw new InvalidOperationException("Names must appear in accessible labels/tooltips, and missing profile links must not invent actions.");
            ((IInvokeProvider)new ButtonAutomationPeer(contact).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(40);
            if (opened?.AbsoluteUri != "https://www.facebook.com/synthetic.friend")
                throw new InvalidOperationException("An avatar must open only the supplied Facebook link.");
            fake.Count = 30;
            await RefreshFacebookAsync();
            FacebookRail.UpdateLayout();
            if (FacebookRail.ScrollableHeight <= 0 || FacebookRail.ActualWidth != 40 || FacebookAvatars.Children.Count != 30)
                throw new InvalidOperationException("Contacts must scroll vertically in the compact rail rather than expanding it into a panel.");
            fake.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = RefreshFacebookAsync();
            SetFacebookOpen(false);
            await pending;
            if (FacebookRail.Visibility != Visibility.Collapsed || FacebookAvatars.Children.Count != 0 || Composer.Text != expectedDraft || !fake.Canceled)
                throw new InvalidOperationException("Closing the rail must cancel the read, discard contacts and preserve the draft.");
            fake.Pending = null;
            fake.Fail = true;
            SetFacebookOpen(true);
            await WaitForUpdateUiAsync(() => !_facebookBusy);
            if (FacebookAvatars.Children.Count != 0 ||
                FacebookSidebar.Visibility != Visibility.Visible || FacebookRail.Visibility != Visibility.Visible ||
                !FacebookRecovery.IsOpen || !FacebookRecoveryMessage.Text.Contains("Enable Facebook contacts") ||
                !_facebookError!.Contains("Enable Facebook contacts"))
                throw new InvalidOperationException("Unavailable contacts must show visible connection recovery, leaving the avatar rail free of fake contacts and error icons.");
            SetFacebookOpen(false);
            File.AppendAllText(log, "PASS: Facebook ink centered with Gmail/Tasks on both axes in both themes/sidebar layouts; independent 40px sidebar surface, native photo brushes, vertical avatar scrolling, original-language tooltips, safe links, preserved draft/destination, cancellation and recovery; no browser, network or user contacts\n");
        }
        finally
        {
            SetFacebookOpen(false);
            _services.Facebook = service;
            _services.FacebookPhoto = photo;
            _launchBrowser = launch;
            _sidebarManualExpanded = sidebar;
            RequestedTheme = theme;
            Composer.Text = draft;
            PaintSurface();
            UpdateWritingLayout();
        }
    }

    private sealed class CheckedFacebook : IFacebookContactsService
    {
        public bool IsConnected => true;
        public bool Fail, Canceled;
        public int Count = 2;
        public TaskCompletionSource<IReadOnlyList<FacebookContact>>? Pending;
        public async Task<IReadOnlyList<FacebookContact>> ReadAsync(CancellationToken token)
        {
            if (Fail) throw new InvalidOperationException("Enable Facebook contacts in the HABITAT extension, then retry.");
            if (Pending is not null)
                try { return await Pending.Task.WaitAsync(token); }
                catch (OperationCanceledException) { Canceled = true; throw; }
            return Enumerable.Range(0, Count).Select(index => new FacebookContact(index == 0 ? "Amiga ☕" : $"Friend 雪 {index}",
                $"https://scontent.fbcdn.net/synthetic{index}.jpg", index == 0 ? "https://www.facebook.com/synthetic.friend" : null)).ToArray();
        }
    }
}
#endif
