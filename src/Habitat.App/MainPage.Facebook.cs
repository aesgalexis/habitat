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
    private const double FacebookRailWidth = 40;
    private const double FacebookRailGap = 12;
    private const double FacebookRailInset = FacebookRailWidth + FacebookRailGap;
    private readonly Microsoft.UI.Xaml.Shapes.Path FacebookIcon = new()
    {
        Width = 13, Height = 24, Stretch = Stretch.Uniform,
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
    private readonly StackPanel FacebookAvatars = new() { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ScrollViewer FacebookRail = new()
    {
        Width = FacebookRailWidth, Padding = new Thickness(0), Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    private readonly Grid FacebookSidebar = new()
    {
        Width = FacebookRailWidth, Visibility = Visibility.Collapsed,
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
        // Normalize the mark to its ink bounds and center it in the same 24px
        // symbol slot and reserved badge line as Gmail and Tasks.
        var mark = new PathFigure { StartPoint = new(4, 24), IsClosed = true };
        foreach (var point in new Windows.Foundation.Point[] { new(4, 13), new(0, 13), new(0, 9), new(4, 9), new(4, 6) })
            mark.Segments.Add(new LineSegment { Point = point });
        mark.Segments.Add(new BezierSegment { Point1 = new(4, 2), Point2 = new(6, 0), Point3 = new(10, 0) });
        foreach (var point in new Windows.Foundation.Point[] { new(13, 0), new(13, 4), new(11, 4) })
            mark.Segments.Add(new LineSegment { Point = point });
        mark.Segments.Add(new BezierSegment { Point1 = new(9, 4), Point2 = new(9, 5), Point3 = new(9, 6) });
        foreach (var point in new Windows.Foundation.Point[] { new(9, 9), new(13, 9), new(12, 13), new(9, 13), new(9, 24) })
            mark.Segments.Add(new LineSegment { Point = point });
        var geometry = new PathGeometry();
        geometry.Figures.Add(mark);
        FacebookIcon.Data = geometry;
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
        FacebookSidebar.Children.Add(FacebookRail);
        surface.Children.Add(FacebookSidebar);
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
        _nextFacebookRefresh = DateTimeOffset.MinValue;
        UpdateWritingLayout();
        if (open) TickFacebook();
        else FocusComposer();
    }

    private void SizeFacebook(TopWidgetLayout layout, double height)
    {
        FacebookButton.Width = layout.WidgetWidth;
        FacebookTitle.Visibility = layout.Expanded ? Visibility.Visible : Visibility.Collapsed;
        FacebookSidebar.Margin = new Thickness(layout.WidgetWidth + FacebookRailGap, 20, 0, 24);
        FacebookSidebar.Height = Math.Max(0, height - 44);
        FacebookRail.Height = FacebookSidebar.Height;
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

    private void AddFacebookAvatar(FacebookContact contact)
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
        FrameworkElement item = avatar;
        if (contact.Link is { } link)
        {
            var button = new Button
            {
                Content = avatar, Width = 40, Height = 40, MinWidth = 0, MinHeight = 0,
                Padding = new Thickness(0), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(20)
            };
            UseStableWidgetButton(button);
            button.Click += async (_, _) =>
            {
                if (!HasAppearanceOverlay && !_operations.IsStopping) await RunOperationAsync(async () =>
                {
                    if (!await _launchBrowser(new Uri(link))) ShowFacebookState("Could not open Facebook in Chrome. Try again.", true);
                });
            };
            item = button;
        }
        AutomationProperties.SetName(item, contact.Name);
        ToolTipService.SetToolTip(item, contact.Name);
        FacebookAvatars.Children.Add(item);
    }

    private void ShowFacebookState(string message, bool actionable)
    {
        FacebookAvatars.Children.Clear();
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
        FacebookIcon.Fill = text;
        FacebookTitle.Foreground = FacebookButton.Foreground = text;
        FacebookButton.Background = transparent;
        FacebookRecoveryMessage.Foreground = text;
        foreach (var key in new[] { "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed" })
            FacebookButton.Resources[key] = text;
        foreach (var key in new[] { "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed" })
            FacebookButton.Resources[key] = transparent;
        FacebookRail.RequestedTheme = RequestedTheme;
        FacebookSidebar.Background = new SolidColorBrush(RequestedTheme == ElementTheme.Dark ? Color(0x08, 255, 255, 255) : Color(0x06, 0, 0, 0));
        if (FacebookRecovery.Content is FrameworkElement recovery) recovery.RequestedTheme = RequestedTheme;
        foreach (var item in FacebookAvatars.Children)
        {
            var avatar = item is Button button ? button.Content as Grid : item as Grid;
            if (avatar is null) continue;
            foreach (var child in avatar.Children)
            {
                if (child is Ellipse { Fill: SolidColorBrush } circle) circle.Fill = FacebookAvatarFallback();
                if (child is TextBlock initial) initial.Foreground = text;
            }
        }
    }
}
