using Habitat.Features.Facebook;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Habitat;

public sealed partial class MainPage
{
    private bool FacebookCardsLarge => _preferences.FacebookCardSize == "large";
    private double FacebookSidebarWidth => FacebookCardsLarge ? 240 : 200;
    private const double FacebookRailGap = 12;
    private readonly FontIcon FacebookIcon = new()
    {
        Glyph = "\uE716", FontSize = 24, Width = 24, Height = 24,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Grid FacebookIconSlot = new() { Width = 24, Height = 24 };
    private readonly StackPanel FacebookSymbol = new()
    {
        Width = 24, Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Button FacebookButton = new()
    {
        Height = 78, Padding = new Thickness(8), BorderThickness = new Thickness(0),
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock FacebookTitle = new() { Text = "Facebook", FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel FacebookAvatars = new() { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox FacebookContactSearch = new()
    {
        PlaceholderText = "Search contacts", MinWidth = 0, Margin = new Thickness(8, 8, 8, 0)
    };
    private readonly TextBlock FacebookNoMatches = new()
    {
        Text = "No contacts found.", TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(8, 0, 8, 0), Visibility = Visibility.Collapsed
    };
    private readonly ScrollViewer FacebookRail = new()
    {
        Padding = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    private readonly Grid FacebookSidebar = new()
    {
        Width = 200, RowSpacing = 8, Visibility = Visibility.Collapsed, BorderThickness = new Thickness(0, 0, 1, 0),
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
    };
    private CancellationTokenSource? _facebookCancellation;
    private bool _facebookPanelOpen, _facebookBusy;
    private bool _facebookWasConnected;
    private bool _facebookShowRecovery;
    private readonly Flyout FacebookRecovery = new();
    private readonly TextBlock FacebookRecoveryMessage = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button FacebookOpenExtensions = new() { Content = "Open Chrome extensions" };
    private string? _facebookError;
    private DateTimeOffset _nextFacebookRefresh = DateTimeOffset.MinValue;

    private void BuildFacebookInterface(Grid surface)
    {
        var content = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        FacebookIconSlot.Children.Add(FacebookIcon);
        FacebookSymbol.Children.Add(FacebookIconSlot);
        FacebookSymbol.Children.Add(new Border { Height = 18 });
        content.Children.Add(FacebookSymbol);
        Grid.SetColumn(FacebookTitle, 1);
        content.Children.Add(FacebookTitle);
        FacebookButton.Content = content;
        UseStableWidgetButton(FacebookButton);
        AutomationProperties.SetName(FacebookButton, "Show Facebook contacts");
        ToolTipService.SetToolTip(FacebookButton, "Facebook contacts");
        FacebookButton.Click += (_, _) => SetFacebookOpen(!_facebookPanelOpen);
        var recovery = new StackPanel { Spacing = 12, Width = 280 };
        recovery.Children.Add(FacebookRecoveryMessage);
        recovery.Children.Add(FacebookOpenExtensions);
        FacebookOpenExtensions.Click += async (_, _) => await RunOperationAsync(async () =>
        {
            await _launchBrowser(new Uri("chrome://extensions/"));
        });
        var openFacebook = new Button { Content = "Open Facebook" };
        openFacebook.Click += async (_, _) => await RunOperationAsync(async () =>
        {
            await _launchBrowser(new Uri("https://www.facebook.com/friends/list/"));
        });
        recovery.Children.Add(openFacebook);
        var retry = new Button { Content = "Retry" };
        retry.Click += (_, _) =>
        {
            FacebookRecovery.Hide();
            _facebookShowRecovery = true;
            _nextFacebookRefresh = DateTimeOffset.MinValue;
            TickFacebook();
        };
        recovery.Children.Add(retry);
        FacebookRecovery.Content = recovery;
        _widgets.RowDefinitions.Add(new RowDefinition { Height = new GridLength(78) });
        Grid.SetRow(FacebookButton, _widgets.RowDefinitions.Count - 1);
        _widgets.Children.Add(FacebookButton);
        FacebookRail.Content = FacebookAvatars;
        AutomationProperties.SetName(FacebookRail, "Facebook contacts");
        AutomationProperties.SetName(FacebookContactSearch, "Search contacts by name");
        FacebookContactSearch.TextChanged += (_, _) =>
        {
            ApplyFacebookContactFilter();
            FacebookRail.ChangeView(null, 0, null, true);
        };
        AutomationProperties.SetLiveSetting(FacebookNoMatches, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        FacebookSidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        FacebookSidebar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        FacebookSidebar.Children.Add(FacebookContactSearch);
        Grid.SetRow(FacebookRail, 1);
        FacebookSidebar.Children.Add(FacebookRail);
        Grid.SetRow(FacebookNoMatches, 1);
        FacebookNoMatches.VerticalAlignment = VerticalAlignment.Top;
        FacebookSidebar.Children.Add(FacebookNoMatches);
        surface.Children.Add(FacebookSidebar);
        FacebookAvatars.Spacing = FacebookCardsLarge ? 4 : 2;
    }

    private void ApplyFacebookCardSize(bool wasLarge)
    {
        var rowOffset = FacebookRail.VerticalOffset / (wasLarge ? 56 : 38);
        FacebookAvatars.Spacing = FacebookCardsLarge ? 4 : 2;
        foreach (var item in FacebookAvatars.Children) SizeFacebookContact((Button)item);
        UpdateWritingLayout();
        FacebookRail.UpdateLayout();
        FacebookRail.ChangeView(null, rowOffset * (FacebookCardsLarge ? 56 : 38), null, true);
    }

    private void SetFacebookOpen(bool open)
    {
        if (HasAppearanceOverlay || _updateHandoff || _operations.IsStopping) return;
        _facebookPanelOpen = open;
        _facebookShowRecovery = open;
        FacebookRecovery.Hide();
        FacebookRail.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        FacebookSidebar.Visibility = FacebookRail.Visibility;
        AutomationProperties.SetName(FacebookButton, open ? "Hide Facebook contacts" : "Show Facebook contacts");
        AutomationProperties.SetItemStatus(FacebookButton, open ? "Expanded" : "Collapsed");
        _facebookCancellation?.Cancel();
        FacebookAvatars.Children.Clear();
        FacebookContactSearch.Text = "";
        ApplyFacebookContactFilter();
        _nextFacebookRefresh = DateTimeOffset.MinValue;
        UpdateWritingLayout();
        if (open) TickFacebook();
        else FocusComposer();
    }

    private void SizeFacebook(TopWidgetLayout layout, double width, double height)
    {
        FacebookButton.Width = layout.WidgetWidth;
        FacebookTitle.Visibility = layout.Expanded ? Visibility.Visible : Visibility.Collapsed;
        FacebookSidebar.Width = Math.Min(FacebookSidebarWidth, Math.Max(0, width - layout.WidgetWidth - FacebookRailGap - DestinationRailInset - 16));
        FacebookSidebar.Margin = new Thickness(layout.WidgetWidth + FacebookRailGap, 20, 0, 24);
        FacebookSidebar.Height = Math.Max(0, height - 44);
    }

    private void TickFacebook()
    {
        if (!_facebookPanelOpen || _operations.IsStopping || HasAppearanceOverlay || _facebookBusy) return;
        var connected = _services.Facebook.IsConnected;
        if (_facebookWasConnected && !connected)
        {
            ShowFacebookState("Chrome disconnected. Reconnect the HABITAT extension to load Facebook contacts.", true);
            _nextFacebookRefresh = DateTimeOffset.MinValue;
        }
        if (!_facebookWasConnected && connected) _nextFacebookRefresh = DateTimeOffset.MinValue;
        _facebookWasConnected = connected;
        if (DateTimeOffset.UtcNow < _nextFacebookRefresh) return;
        _ = RunOperationAsync(RefreshFacebookAsync);
    }

    private async Task RefreshFacebookAsync()
    {
        if (_facebookBusy || !_facebookPanelOpen || _operations.IsStopping) return;
        _facebookBusy = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_operations.Token);
        _facebookCancellation = cancellation;
        try
        {
            // Clear the previous snapshot before every read, including account changes.
            ShowFacebookState("Loading Facebook contacts…", false);
            var contacts = await _services.Facebook.ReadAsync(cancellation.Token);
            if (!_facebookPanelOpen || cancellation.IsCancellationRequested) return;
            _facebookError = null;
            _facebookShowRecovery = false;
            FacebookRecovery.Hide();
            ToolTipService.SetToolTip(FacebookButton, "Facebook contacts");
            AutomationProperties.SetHelpText(FacebookButton, "Facebook contacts");
            AutomationProperties.SetItemStatus(FacebookButton, $"{contacts.Count} contacts");
            FacebookAvatars.Children.Clear();
            foreach (var contact in contacts) AddFacebookAvatar(contact);
            ApplyFacebookContactFilter();
            PaintFacebook();
            if (contacts.Count == 0) ShowFacebookState("No contacts are loaded in Facebook. Open Friends → All friends in Chrome, wait for the profile photos, then retry.", true);
            _nextFacebookRefresh = DateTimeOffset.UtcNow.AddSeconds(30);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (_facebookPanelOpen && !cancellation.IsCancellationRequested)
                ShowFacebookState(exception is InvalidOperationException ? exception.Message : "Could not load Facebook contacts. Open Facebook in Chrome, then retry.", true);
            _nextFacebookRefresh = DateTimeOffset.UtcNow.AddSeconds(15);
        }
        finally
        {
            if (ReferenceEquals(_facebookCancellation, cancellation)) _facebookCancellation = null;
            _facebookBusy = false;
        }
    }

    private void ApplyFacebookContactFilter()
    {
        var query = FacebookContactSearch.Text.Trim();
        var visible = 0;
        foreach (var card in FacebookAvatars.Children)
        {
            var matches = query.Length == 0 || System.Globalization.CultureInfo.InvariantCulture.CompareInfo.IndexOf(
                AutomationProperties.GetName(card), query,
                System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0;
            card.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
            if (matches) visible++;
        }
        FacebookNoMatches.Visibility = FacebookAvatars.Children.Count > 0 && visible == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private Grid CreateFacebookAvatar(FacebookContact contact)
    {
        var avatar = new Grid { Width = 40, Height = 40 };
        var circle = new Ellipse { Fill = FacebookAvatarFallback() };
        avatar.Children.Add(circle);
        var initial = new TextBlock
        {
            Text = System.Globalization.StringInfo.GetNextTextElement(contact.Name), FontSize = 17,
            Foreground = TextBrush(),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        avatar.Children.Add(initial);
        if (_services.FacebookPhoto(new Uri(contact.PhotoUrl)) is { } photo)
        {
            circle.Fill = new ImageBrush { ImageSource = photo, Stretch = Stretch.UniformToFill };
            initial.Visibility = Visibility.Collapsed;
            if (photo is Microsoft.UI.Xaml.Media.Imaging.BitmapImage bitmap)
                bitmap.ImageFailed += (_, _) =>
                {
                    circle.Fill = FacebookAvatarFallback();
                    initial.Visibility = Visibility.Visible;
                };
        }
        var presence = new Ellipse
        {
            Width = 9, Height = 9, StrokeThickness = 1.5, Tag = contact.IsOnline,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false
        };
        PaintFacebookPresence(presence);
        avatar.Children.Add(presence);
        return avatar;
    }

    private void AddFacebookAvatar(FacebookContact contact)
    {
        var avatar = CreateFacebookAvatar(contact);
        var row = new Grid { Height = 52, Padding = new Thickness(8, 6, 8, 6), ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(avatar);
        var name = new TextBlock
        {
            Text = contact.Name, FontSize = 14, Foreground = TextBrush(),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        var card = new Button { Content = row, Tag = FacebookRecipientId(contact), MinWidth = 0, MinHeight = 0,
            Padding = new Thickness(0), BorderThickness = new Thickness(2, 0, 0, 0), CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        UseStableWidgetButton(card);
        AutomationProperties.SetName(card, contact.Name);
        AutomationProperties.SetItemStatus(card, contact.IsOnline switch { true => "Online", false => "Offline", null => "Online status unavailable" });
        AutomationProperties.SetHelpText(card, "Select as message destination");
        card.Click += (_, _) => SelectFacebookContact(contact);
        SizeFacebookContact(card);
        FacebookAvatars.Children.Add(card);
    }

    private void SizeFacebookContact(Button item)
    {
        var row = (Grid)item.Content;
        var avatarSize = FacebookCardsLarge ? 40 : 28;
        row.Height = FacebookCardsLarge ? 52 : 36;
        item.Height = row.Height;
        row.Padding = FacebookCardsLarge ? new Thickness(8, 6, 8, 6) : new Thickness(6, 4, 6, 4);
        row.ColumnSpacing = FacebookCardsLarge ? 10 : 8;
        row.ColumnDefinitions[0].Width = new GridLength(avatarSize);
        var avatar = (Grid)row.Children[0];
        avatar.Width = avatar.Height = avatarSize;
        ((TextBlock)avatar.Children[1]).FontSize = FacebookCardsLarge ? 17 : 13;
        ((TextBlock)row.Children[1]).FontSize = FacebookCardsLarge ? 14 : 12;
        ((Ellipse)avatar.Children[2]).Width = ((Ellipse)avatar.Children[2]).Height = FacebookCardsLarge ? 11 : 9;
    }

    private void PaintFacebookPresence(Ellipse presence)
    {
        presence.Fill = presence.Tag switch
        {
            true => new SolidColorBrush(Color(255, 0x21, 0xA3, 0x66)),
            false => new SolidColorBrush(Color(255, 0x8C, 0x94, 0xA3)),
            _ => FacebookAvatarFallback()
        };
        presence.Stroke = presence.Tag is null ? TextBrush() : new SolidColorBrush(RequestedTheme == ElementTheme.Dark
            ? Color(255, 0x1B, 0x24, 0x34) : Color(255, 0xF4, 0xF6, 0xFA));
    }

    private void ShowFacebookState(string message, bool actionable)
    {
        FacebookAvatars.Children.Clear();
        ApplyFacebookContactFilter();
        _facebookError = actionable ? message : null;
        AutomationProperties.SetHelpText(FacebookButton, message);
        AutomationProperties.SetItemStatus(FacebookButton, message);
        ToolTipService.SetToolTip(FacebookButton, message);
        FacebookRecoveryMessage.Text = message;
        FacebookOpenExtensions.Visibility = message.Contains("extension", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        if (actionable && _facebookShowRecovery)
        {
            _facebookShowRecovery = false;
            FacebookRecovery.ShowAt(FacebookButton);
        }
        PaintFacebook();
    }

    private Brush FacebookAvatarFallback() => new SolidColorBrush(RequestedTheme == ElementTheme.Dark
        ? Color(255, 0x35, 0x40, 0x55) : Color(255, 0xDF, 0xE4, 0xEC));

    private void PaintFacebook()
    {
        var text = TextBrush();
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        FacebookIcon.Foreground = text;
        FacebookTitle.Foreground = FacebookButton.Foreground = text;
        FacebookButton.Background = transparent;
        FacebookButton.CornerRadius = new CornerRadius(8);
        FacebookRecoveryMessage.Foreground = text;
        foreach (var key in new[] { "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed" })
            FacebookButton.Resources[key] = text;
        FacebookButton.Resources["ButtonBackground"] = transparent;
        var hover = new SolidColorBrush(RequestedTheme == ElementTheme.Dark ? Color(0x22, 255, 255, 255) : Color(0x12, 0, 0, 0));
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed" })
            FacebookButton.Resources[key] = hover;
        FacebookSidebar.RequestedTheme = FacebookRail.RequestedTheme = RequestedTheme;
        FacebookNoMatches.Foreground = text;
        PaintContactMessageHeader();
        FacebookSidebar.Background = new SolidColorBrush(RequestedTheme == ElementTheme.Dark ? Color(0x08, 255, 255, 255) : Color(0x06, 0, 0, 0));
        FacebookSidebar.BorderBrush = new SolidColorBrush(RequestedTheme == ElementTheme.Dark ? Color(0x24, 255, 255, 255) : Color(0x20, 0, 0, 0));
        if (FacebookRecovery.Content is FrameworkElement recovery) recovery.RequestedTheme = RequestedTheme;
        foreach (var item in FacebookAvatars.Children)
        {
            var card = (Button)item;
            var row = card.Content as Grid;
            if (row is null) continue;
            var selected = card.Tag as string == _destination;
            card.Background = transparent;
            card.BorderBrush = selected ? new SolidColorBrush(RequestedTheme == ElementTheme.Dark ? Color(255, 0x89, 0x95, 0xFF) : Color(255, 0x66, 0x74, 0xF4)) : transparent;
            card.Resources["ButtonBackground"] = transparent;
            card.Resources["ButtonBackgroundPointerOver"] = card.Resources["ButtonBackgroundPressed"] = hover;
            AutomationProperties.SetHelpText(card, selected ? "Selected Messenger recipient" : "Select as message destination");
            foreach (var name in row.Children.OfType<TextBlock>()) name.Foreground = text;
            var avatar = row.Children.OfType<Grid>().FirstOrDefault();
            if (avatar is null) continue;
            foreach (var child in avatar.Children)
            {
                if (child is Ellipse presence && ReferenceEquals(child, avatar.Children[2])) PaintFacebookPresence(presence);
                else if (child is Ellipse { Fill: SolidColorBrush } circle) circle.Fill = FacebookAvatarFallback();
                if (child is TextBlock initial) initial.Foreground = text;
            }
        }
    }
}
