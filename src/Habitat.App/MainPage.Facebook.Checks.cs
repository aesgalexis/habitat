#if HABITAT_UI_CHECKS
using Habitat.Features.Facebook;
using Habitat.Features.Preferences;
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
        var cardSize = _preferences.FacebookCardSize;
        var surface = (Grid)Content;
        var surfaceWidth = surface.Width;
        var fake = new CheckedFacebook();
        try
        {
            _services.Facebook = fake;
            if (_preferences.FacebookCardSize != "small" || FacebookIcon.Glyph != "\uE716" ||
                FacebookSidebar.Children.Count != 3 || FacebookSidebar.Children[0] != FacebookContactSearch ||
                Grid.GetRow(FacebookContactSearch) != 0 || Grid.GetRow(FacebookRail) != 1 ||
                FacebookContactSearch.PlaceholderText != "Search contacts" ||
                AutomationProperties.GetName(FacebookContactSearch) != "Search contacts by name")
                throw new InvalidOperationException("Contacts must start small with an accessible English search field above the list and size settings outside it.");
            _services.FacebookPhoto = _ => new Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap(1, 1);
            Composer.Text = "Synthetic draft preserved while viewing contacts";
            var expectedDraft = Composer.Text;
            Uri? opened = null;
            _launchBrowser = uri => { opened = uri; return Task.FromResult(true); };
            ((IInvokeProvider)new ButtonAutomationPeer(FacebookButton).GetPattern(PatternInterface.Invoke)).Invoke();
            await WaitForUpdateUiAsync(() => !_facebookBusy);
            if (FacebookRail.Visibility != Visibility.Visible || FacebookAvatars.Children.Count != 3 ||
                AutomationProperties.GetName(FacebookButton) != "Hide Facebook contacts" || _destination != destination || Composer.Text != expectedDraft)
                throw new InvalidOperationException("Clicking Facebook must open an independent contacts sidebar while preserving the destination and draft.");
            var originalCards = FacebookAvatars.Children.ToArray();
            foreach (var (query, expected) in new[] { ("miGA", 1), (" 雪 ", 2), ("JOSE", 1), ("no matching contact", 0), ("   ", 3), ("", 3) })
            {
                FacebookContactSearch.Text = query;
                await WaitForUpdateUiAsync(() => FacebookAvatars.Children.Count(card => card.Visibility == Visibility.Visible) == expected);
                if (!FacebookAvatars.Children.SequenceEqual(originalCards) || fake.Reads != 1 ||
                    FacebookNoMatches.Visibility != (expected == 0 ? Visibility.Visible : Visibility.Collapsed) ||
                    FacebookNoMatches.Text != "No contacts found." || Composer.Text != expectedDraft || _destination != destination)
                    throw new InvalidOperationException("Name search must filter locally by substring, ignore case/accents and surrounding whitespace, preserve cards/drafts, and restore all rows when cleared.");
            }
            foreach (var large in new[] { false, true, false })
            {
                var originalContact = FacebookAvatars.Children[0];
                var reads = fake.Reads;
                await ChangeFacebookCardsInPreferencesAsync(large);
                if (!ReferenceEquals(FacebookAvatars.Children[0], originalContact) || fake.Reads != reads ||
                    Preferences.Load(_services.Paths.PreferencesPath).FacebookCardSize != (large ? "large" : "small") ||
                    Composer.Text != expectedDraft || _destination != destination)
                    throw new InvalidOperationException("Changing contact size must persist immediately without reloading contacts or disturbing the draft/destination.");
                foreach (var dark in new[] { false, true })
                foreach (var expanded in new[] { false, true, false })
                {
                    RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
                    PaintSurface();
                    _sidebarManualExpanded = expanded;
                    UpdateWritingLayout();
                    await Task.Delay(40);
                    var railPosition = FacebookRail.TransformToVisual(this).TransformPoint(new());
                    var searchBottom = FacebookContactSearch.TransformToVisual(this).TransformPoint(new(0, FacebookContactSearch.ActualHeight)).Y;
                    var widgetPosition = _widgets.TransformToVisual(this).TransformPoint(new());
                    var foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)FacebookIcon.Foreground;
                    var mailGlyph = (FontIcon)((StackPanel)GmailButton.Content).Children[0];
                    var taskGlyph = (FontIcon)TasksSymbol.Children[0];
                    var facebookCenter = FacebookIcon.TransformToVisual(this).TransformPoint(new(FacebookIcon.ActualWidth / 2, FacebookIcon.ActualHeight / 2));
                    var mailCenter = mailGlyph.TransformToVisual(this).TransformPoint(new(mailGlyph.ActualWidth / 2, mailGlyph.ActualHeight / 2));
                    var taskCenter = taskGlyph.TransformToVisual(this).TransformPoint(new(taskGlyph.ActualWidth / 2, taskGlyph.ActualHeight / 2));
                    var facebookRow = FacebookButton.TransformToVisual(this).TransformPoint(new());
                    var mailRow = GmailButton.TransformToVisual(this).TransformPoint(new());
                    var taskRow = TasksButton.TransformToVisual(this).TransformPoint(new());
                    if (FacebookButton.Width != _widgets.Width || FacebookSidebar.ActualWidth != (large ? 240 : 200) ||
                        FacebookSidebar.Visibility != Visibility.Visible || !ReferenceEquals(FacebookSidebar.Parent, Content) ||
                        Math.Abs(railPosition.X - (widgetPosition.X + _widgets.ActualWidth + 12)) > 0.5 ||
                        searchBottom + 7 > railPosition.Y || FacebookContactSearch.ActualWidth <= 0 ||
                        Math.Abs(facebookCenter.X - mailCenter.X) > 0.5 || Math.Abs(facebookCenter.X - taskCenter.X) > 0.5 ||
                        Math.Abs(facebookCenter.Y - facebookRow.Y - (taskCenter.Y - taskRow.Y)) > 0.5 ||
                        Math.Abs(facebookCenter.Y - facebookRow.Y - (mailCenter.Y - mailRow.Y)) > 0.5 ||
                        foreground.Color != ((Microsoft.UI.Xaml.Media.SolidColorBrush)TextBrush()).Color ||
                        ((Microsoft.UI.Xaml.Media.SolidColorBrush)FacebookButton.Background).Color.A != 0 ||
                        _root.Margin.Left < FacebookSidebar.Margin.Left + FacebookSidebar.Width ||
                        _sidebarDivider.Margin.Left <= _widgets.Width || _sidebarDivider.Margin.Left >= FacebookSidebar.Margin.Left ||
                        FacebookTitle.Visibility != (expanded ? Visibility.Visible : Visibility.Collapsed))
                        throw new InvalidOperationException($"Facebook must align with Gmail and Tasks and open an independent contacts sidebar. Centers: Facebook={facebookCenter}, Gmail={mailCenter}, Tasks={taskCenter}; row offsets: {facebookCenter.Y - facebookRow.Y}, {mailCenter.Y - mailRow.Y}, {taskCenter.Y - taskRow.Y}.");
                    CheckFacebookContactRows();
                }
            }
            foreach (var item in FacebookAvatars.Children)
                if (item is not Button || ToolTipService.GetToolTip(item) is not null ||
                    Descendants(item).Any(child => child is Button || ToolTipService.GetToolTip(child) is not null))
                    throw new InvalidOperationException("Recipient cards must have no tooltips or nested browser actions.");
            if (opened is not null || AutomationProperties.GetName(FacebookAvatars.Children[0]) != "Amiga ☕" ||
                AutomationProperties.GetItemStatus(FacebookAvatars.Children[0]) != "Online" ||
                AutomationProperties.GetItemStatus(FacebookAvatars.Children[1]) != "Offline" ||
                AutomationProperties.GetItemStatus(FacebookAvatars.Children[2]) != "Online status unavailable")
                throw new InvalidOperationException("Presence must distinguish explicit online/offline from unknown without opening a browser.");
            _sidebarManualExpanded = true;
            surface.Width = 500;
            UpdateWritingLayout();
            surface.UpdateLayout();
            if (_sidebarExpanded || FacebookSidebar.ActualWidth < 160 ||
                FacebookSidebar.Margin.Left + FacebookSidebar.ActualWidth + DestinationRailInset >
                surface.ActualWidth - surface.Padding.Left - surface.Padding.Right ||
                _root.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("A narrow window must keep names beside photos without overlapping the destination rail.");
            CheckFacebookContactRows();
            _sidebarManualExpanded = false;
            surface.Width = surfaceWidth;
            UpdateWritingLayout();
            surface.UpdateLayout();
            fake.Count = 30;
            FacebookContactSearch.Text = "Amiga";
            await RefreshFacebookAsync();
            if (FacebookContactSearch.Text != "Amiga" || FacebookAvatars.Children.Count(card => card.Visibility == Visibility.Visible) != 1)
                throw new InvalidOperationException("Refreshing contacts must retain the query and apply it to the new snapshot.");
            FacebookContactSearch.Text = "";
            await WaitForUpdateUiAsync(() => FacebookAvatars.Children.All(card => card.Visibility == Visibility.Visible));
            FacebookRail.UpdateLayout();
            if (FacebookRail.ScrollableHeight <= 0 || FacebookSidebar.ActualWidth != 200 || FacebookAvatars.Children.Count != 30 ||
                FacebookRail.ScrollableWidth > 0)
                throw new InvalidOperationException("Contact rows must scroll vertically within the second sidebar without horizontal scrolling.");
            FacebookRail.ChangeView(null, 5 * 38, null, true);
            await Task.Delay(80);
            var searchTop = FacebookContactSearch.TransformToVisual(this).TransformPoint(new()).Y;
            if (Math.Abs(searchTop - FacebookSidebar.TransformToVisual(this).TransformPoint(new()).Y - 8) > 0.5)
                throw new InvalidOperationException("The contact search field must stay fixed above the scrolling list.");
            await ChangeFacebookCardsInPreferencesAsync(true);
            await Task.Delay(80);
            if (Math.Abs(FacebookRail.VerticalOffset - 5 * 56) > 1 || fake.Reads != 2)
                throw new InvalidOperationException("Resizing a scrolled list must retain the current contact without another read.");
            await ChangeFacebookCardsInPreferencesAsync(false);
            FacebookContactSearch.Text = "no matching contact";
            await WaitForUpdateUiAsync(() => FacebookNoMatches.Visibility == Visibility.Visible);
            fake.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = RefreshFacebookAsync();
            if (FacebookNoMatches.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Loading must clear the previous no-match state.");
            ((IInvokeProvider)new ButtonAutomationPeer(FacebookButton).GetPattern(PatternInterface.Invoke)).Invoke();
            await pending;
            if (FacebookRail.Visibility != Visibility.Collapsed || FacebookSidebar.Visibility != Visibility.Collapsed ||
                FacebookAvatars.Children.Count != 0 || FacebookContactSearch.Text != "" || FacebookNoMatches.Visibility != Visibility.Collapsed ||
                Composer.Text != expectedDraft || !fake.Canceled ||
                AutomationProperties.GetName(FacebookButton) != "Show Facebook contacts")
                throw new InvalidOperationException("Clicking Facebook again must close the sidebar, cancel the read, discard contacts and preserve the draft.");
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
            await CheckFacebookMessagingUiAsync(log);
            File.AppendAllText(log, "PASS: fixed accessible contact search, local substring/case/accent filtering, empty results, clearing and refresh; two-person icon; size settings in Preferences > Sidebar with small default and persistent large cards; list has no size selector, tooltips or profile actions; online/offline/unknown indicators in both sizes/themes, stable contacts/scroll position, preserved draft, cancellation and recovery; no browser, network or user contacts\n");
        }
        finally
        {
            SetFacebookOpen(false);
            var wasLarge = FacebookCardsLarge;
            _preferences = _preferences with { FacebookCardSize = cardSize };
            ApplyFacebookCardSize(wasLarge);
            SavePreferences();
            _services.Facebook = service;
            _services.FacebookPhoto = photo;
            _launchBrowser = launch;
            _sidebarManualExpanded = sidebar;
            surface.Width = surfaceWidth;
            RequestedTheme = theme;
            Composer.Text = draft;
            PaintSurface();
            UpdateWritingLayout();
        }
    }

    private void CheckFacebookContactRows()
    {
        foreach (var item in FacebookAvatars.Children)
        {
            var row = (Grid)((Button)item).Content;
            var avatar = (Grid)row.Children[0];
            var name = (TextBlock)row.Children[1];
            var avatarRight = avatar.TransformToVisual(row).TransformPoint(new(avatar.ActualWidth, 0)).X;
            var nameLeft = name.TransformToVisual(row).TransformPoint(new()).X;
            var expectedName = ReferenceEquals(item, FacebookAvatars.Children[0]) ? "Amiga ☕" :
                ReferenceEquals(item, FacebookAvatars.Children[1]) ? CheckedFacebook.LongName : "Friend 雪 2";
            var photoSize = FacebookCardsLarge ? 40 : 28;
            var presence = (Microsoft.UI.Xaml.Shapes.Ellipse)avatar.Children[2];
            if (name.Text != expectedName || name.Visibility != Visibility.Visible || name.ActualWidth <= 0 ||
                row.ColumnDefinitions[1].ActualWidth < 60 || nameLeft + name.ActualWidth > row.ActualWidth - row.Padding.Right + 0.5 ||
                nameLeft < avatarRight + (FacebookCardsLarge ? 9 : 7) || avatar.ActualWidth != photoSize || avatar.ActualHeight != photoSize ||
                row.ActualHeight != (FacebookCardsLarge ? 52 : 36) || name.FontSize != (FacebookCardsLarge ? 14 : 12) ||
                name.TextTrimming != TextTrimming.CharacterEllipsis ||
                presence.Visibility != Visibility.Visible || presence.ActualWidth != (FacebookCardsLarge ? 11 : 9) ||
                (presence.Tag is true) != (AutomationProperties.GetItemStatus(item) == "Online") ||
                ((Microsoft.UI.Xaml.Media.SolidColorBrush)name.Foreground).Color !=
                ((Microsoft.UI.Xaml.Media.SolidColorBrush)TextBrush()).Color)
                throw new InvalidOperationException($"Each size must show the original name beside its photo, with readable theme colors and bounded long names. Large={FacebookCardsLarge}; photo={avatar.ActualWidth}x{avatar.ActualHeight}; row={row.ActualHeight}; text={name.ActualWidth}, font={name.FontSize}, X={nameLeft}, photoRight={avatarRight}; textColor={((Microsoft.UI.Xaml.Media.SolidColorBrush)name.Foreground).Color}, themeColor={((Microsoft.UI.Xaml.Media.SolidColorBrush)TextBrush()).Color}.");
        }
    }

    private async Task ChangeFacebookCardsInPreferencesAsync(bool large)
    {
        var showing = ShowPreferencesAsync("Sidebar");
        await Task.Delay(120);
        try
        {
            if (_activeDialog?.Content is not PreferencesView view || view.Category != "Sidebar")
                throw new InvalidOperationException("Contact size must be available from Sidebar preferences.");
            var choice = view.Sidebar.ContactCardSize;
            if (choice.Header as string != "Contact card size" || AutomationProperties.GetName(choice) != "Contact card size" ||
                !choice.Items.Cast<string>().SequenceEqual(new[] { "Small cards (default)", "Large cards" }) ||
                choice.SelectedIndex != (FacebookCardsLarge ? 1 : 0))
                throw new InvalidOperationException("Preferences must restore the saved card size and use English labels.");
            choice.SelectedIndex = large ? 1 : 0;
        }
        finally
        {
            _activeDialog?.Hide();
            await showing;
        }
    }

    private sealed class CheckedFacebook : IFacebookContactsService
    {
        public const string LongName = "Friend 雪 José with a very long original name that must fit beside the profile photo";
        public bool IsConnected => true;
        public bool Fail, Canceled;
        public int Count = 3, Reads;
        public TaskCompletionSource<IReadOnlyList<FacebookContact>>? Pending;
        public async Task<IReadOnlyList<FacebookContact>> ReadAsync(CancellationToken token)
        {
            Reads++;
            if (Fail) throw new InvalidOperationException("Enable Facebook contacts in the HABITAT extension, then retry.");
            if (Pending is not null)
                try { return await Pending.Task.WaitAsync(token); }
                catch (OperationCanceledException) { Canceled = true; throw; }
            return Enumerable.Range(0, Count).Select(index => new FacebookContact(index == 0 ? "Amiga ☕" : index == 1 ? LongName : $"Friend 雪 {index}",
                $"https://scontent.fbcdn.net/synthetic{index}.jpg", index < 2 ? $"https://www.facebook.com/synthetic.friend{index}" : null,
                index == 0 ? true : index == 1 ? false : null)).ToArray();
        }
    }
}
#endif
