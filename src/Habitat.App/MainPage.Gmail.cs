using Habitat.Models;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using System.Text.Json;
using Windows.Storage.Pickers;

namespace Habitat;

public sealed partial class MainPage
{
    private readonly CancellationTokenSource _gmailCancellation = new();
    private readonly GmailSession _gmailSession;
    private CancellationTokenSource? _gmailSignInCancellation => _gmailSession.SignInCancellation;
    private GmailAuthService? _gmailAuth => _gmailSession.Auth;
    private GmailInbox? _gmailInbox { get => _gmailSession.Inbox; set => _gmailSession.Inbox = value; }
    private List<GmailMessage> _gmailMessages => _gmailSession.Messages;
    private GmailMessage? _gmailSelected { get => _gmailSession.Selected; set => _gmailSession.Selected = value; }
    private readonly Features.Sidebar.GmailSidebarPreview _gmailPreview;
    private Button GmailButton => _gmailPreview.Button;
    private Button GmailMessageButton => _gmailPreview.MessageButton;
    private Grid GmailWidget => _gmailPreview.Root;
    private TextBlock GmailBadge => _gmailPreview.Badge;
    private readonly Flyout GmailFlyout = new() { Placement = FlyoutPlacementMode.RightEdgeAlignedTop };
    private readonly StackPanel GmailContent = new() { Spacing = 14 };
    private readonly TextBlock GmailStatus = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly Button GmailRefresh = UiLanguage.Watch(new Button() { Content = UiText.Get("Refresh") }, ("Content", () => (object?)(UiText.Get("Refresh"))));
    private readonly Button GmailMore = UiLanguage.Watch(new Button() { Content = UiText.Get("LoadMore"), Visibility = Visibility.Collapsed }, ("Content", () => (object?)(UiText.Get("LoadMore"))));
    private readonly Button GmailSignIn = UiLanguage.Watch(new Button() { Content = UiText.Get("SignInWithGoogle") }, ("Content", () => (object?)(UiText.Get("SignInWithGoogle"))));
    private readonly Button GmailDisconnect = UiLanguage.Watch(new Button() { Content = UiText.Get("Disconnect") }, ("Content", () => (object?)(UiText.Get("Disconnect"))));
    private readonly Button GmailCancel = UiLanguage.Watch(new Button() { Content = UiText.Get("CancelSignin"), Visibility = Visibility.Collapsed }, ("Content", () => (object?)(UiText.Get("CancelSignin"))));
    private readonly ComboBox GmailFilter = new() { ItemsSource = new[] { UiText.Get("InboxUnread"), UiText.Get("AllUnread") } };
    private bool _gmailBusy => _gmailSession.IsBusy;
    private bool _gmailPanelOpen => _gmailPreview.PanelOpen;
    private bool _gmailSettingsOpen { get => _gmailPreview.SettingsOpen; set => _gmailPreview.SettingsOpen = value; }
    private DateTimeOffset _nextGmailRefresh => _gmailSession.NextRefresh;
    private string? _gmailError { get => _gmailSession.Error is { } error ? UiText.LocalizeMessage(error) : null; set => _gmailSession.Error = value; }
    private string? _gmailFailureKind;
    private string? _lastGmailRecordedState;

    private void BuildGmailInterface(Grid surface)
    {
        try
        {
            _gmailSession.Initialize();
            RecordGmailStartup();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or Win32Exception)
        {
            _gmailError = GmailError(exception);
        }

        var indicator = GmailWidget;
        GmailMessageButton.Click += async (_, _) =>
        {
            var message = _gmailPreview.Message;
            if (message is null || _gmailAuth?.Email is not string email) { OpenGmail(); return; }
            try
            {
                if (!await _launchBrowser(GmailWebLink.ForMessage(email, message)))
                    throw new InvalidOperationException(UiText.Get("WindowsCouldNotOpenGmailInYourBrowser"));
            }
            catch (Exception exception) { _gmailError = GmailError(exception); RenderGmail(); }
        };
        GmailButton.HorizontalAlignment = HorizontalAlignment.Left;
        GmailButton.VerticalAlignment = VerticalAlignment.Center;
        GmailButton.Width = 40;
        GmailButton.Height = 78;
        GmailButton.Padding = new Thickness(8);
        GmailButton.MinWidth = 36;
        GmailButton.MinHeight = 36;
        GmailButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        GmailButton.VerticalContentAlignment = VerticalAlignment.Center;
        GmailButton.BorderThickness = new Thickness(0);
        UiLanguage.SetName(GmailButton, () => UiText.Get("GmailUnreadEmails"));
        GmailButton.Flyout = GmailFlyout;
        surface.Children.Add(indicator);
        indicator.SizeChanged += (_, _) => UpdateWritingLayout();

        var title = new TextBlock { Text = "Gmail", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var setup = UiLanguage.Watch(new Button { Content = UiText.Get("Setup") }, ("Content", () => (object?)(UiText.Get("Setup"))));
        setup.Click += async (_, _) => await ShowGmailSetupAsync();
        GmailRefresh.Click += async (_, _) => await RefreshGmailAsync();
        GmailSignIn.Click += async (_, _) =>
        {
            await RestoreGmailSessionAsync();
            if (_gmailAuth is null) return;
            if (_gmailAuth?.IsSignedIn == true) return;
            if (_gmailAuth?.IsConfigured != true) await ShowGmailSetupAsync();
            else await SignInGmailAsync();
        };
        GmailDisconnect.Click += async (_, _) => await DisconnectGmailAsync();
        GmailCancel.Click += (_, _) => _gmailSignInCancellation?.Cancel();
        GmailMore.Click += async (_, _) => await RefreshGmailAsync(loadMore: true);
        actions.Children.Add(GmailRefresh);
        actions.Children.Add(setup);
        actions.Children.Add(GmailDisconnect);

        var panel = new StackPanel { Spacing = 14, Width = 380 };
        panel.Children.Add(title);
        panel.Children.Add(GmailStatus);
        panel.Children.Add(GmailFilter);
        panel.Children.Add(GmailSignIn);
        panel.Children.Add(GmailCancel);
        panel.Children.Add(actions);
        var scroll = new ScrollViewer { Content = GmailContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 420 };
        ScrollEdgeFade.Enable(scroll);
        panel.Children.Add(scroll);
        panel.Children.Add(GmailMore);
        panel.Children.Add(UiLanguage.Watch(new TextBlock
        {
            Text = UiText.Get("ReadonlyOpeningAMessageDoesNotMarkIt"),
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Opacity = 0.7
        }, ("Text", () => (object?)(UiText.Get("ReadonlyOpeningAMessageDoesNotMarkIt")))));
        GmailFlyout.Content = panel;
        GmailFilter.SelectedIndex = _preferences.GmailInboxOnly ? 0 : 1;
        GmailFilter.SelectionChanged += async (_, _) =>
        {
            var inboxOnly = GmailFilter.SelectedIndex == 0;
            if (_preferences.GmailInboxOnly == inboxOnly) return;
            _preferences = _preferences with { GmailInboxOnly = inboxOnly };
            SavePreferences();
            _gmailInbox = null;
            _gmailMessages.Clear();
            _gmailSelected = null;
            RenderGmail();
            await RefreshGmailAsync();
        };
        GmailFlyout.Opening += (_, _) =>
        {
            _gmailPreview.PanelOpen = true;
            panel.Width = Math.Clamp(XamlRoot.Size.Width - 100, 260, 380);
            scroll.MaxHeight = Math.Clamp(XamlRoot.Size.Height - 330, 120, 420);
            RenderGmail();
            if (_gmailAuth?.IsSignedIn == true && DateTimeOffset.UtcNow >= _nextGmailRefresh) _ = RefreshGmailAsync();
        };
        GmailFlyout.Closed += (_, _) =>
        {
            _gmailPreview.PanelOpen = false;
            FocusComposer();
        };
        RenderGmail();
    }

    private void RecordGmailStartup()
    {
        var state = $"Client configured: {_gmailAuth?.IsConfigured == true}; saved session loaded: {_gmailAuth?.IsSignedIn == true}; inbox loaded: {_gmailInbox is not null}; last sync: {_gmailInbox?.UpdatedAt:O}; error: {(_gmailError is null ? "none" : _gmailFailureKind ?? "local storage")}";
        if (state == _lastGmailRecordedState) return;
        var entry = $"{DateTimeOffset.Now:O} PID: {Environment.ProcessId}; build: {typeof(MainPage).Module.ModuleVersionId}; {state}; authorization store: Windows Credential Manager{Environment.NewLine}";
        try
        {
            var directory = _services.Paths.DesktopDirectory;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "gmail-startup.log"), entry);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        _lastGmailRecordedState = state;
    }

    private void OpenGmail()
    {
        _gmailPreview.PanelOpen = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => GmailFlyout.ShowAt(GmailButton));
    }

    private Task RestoreGmailSessionAsync() => RunOperationAsync(() =>
        _gmailSession.RestoreAsync(() => _preferences.GmailInboxOnly, _gmailCancellation.Token));

    private Task RefreshGmailAsync(bool loadMore = false) => RunOperationAsync(() =>
        _gmailSession.RefreshAsync(() => _preferences.GmailInboxOnly, loadMore, _gmailCancellation.Token));

    private Task ReadGmailMessageAsync(GmailMessage message) => RunOperationAsync(() =>
        _gmailSession.ReadAsync(message, _gmailCancellation.Token));

    private Task SignInGmailAsync() => RunOperationAsync(() =>
        _gmailSession.SignInAsync(() => _preferences.GmailInboxOnly, _gmailCancellation.Token));

    private Task DisconnectGmailAsync() => RunOperationAsync(() => _gmailSession.DisconnectAsync(_gmailCancellation.Token));

    private void RenderGmail()
    {
        RecordGmailStartup();
        var signedIn = _gmailAuth?.IsSignedIn == true;
        _gmailPreview.Render(_sidebarExpanded);
        GmailStatus.Text = UiLanguage.Value(GmailStatus, "Text", () => _gmailError ?? (_gmailSignInCancellation is not null ? UiText.Get("FinishSigninInYourBrowserHABITATNeverAsks") :
            _gmailBusy ? UiText.Get("Loading") : signedIn ? $"{_gmailAuth!.Email}\n" +
                (_gmailInbox is null ? UiText.Get("ReadyToRefresh") : UiText.Format(_gmailInbox.CountIsEstimate ? "ApproximateUnreadUpdated" : "UnreadUpdated", _gmailInbox.UnreadCount, _gmailInbox.UpdatedAt.ToLocalTime())) :
            _gmailAuth?.IsConfigured == true ? UiText.Get("NotConnectedSignInWithYourGoogleAccount") :
            UiText.Get("GoogleSetupPendingTheGmailReaderIsReady")));
        GmailRefresh.IsEnabled = signedIn && !_gmailBusy;
        GmailDisconnect.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        GmailDisconnect.IsEnabled = !_gmailBusy;
        GmailFilter.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        GmailFilter.IsEnabled = !_gmailBusy;
        GmailSignIn.Visibility = !signedIn && _gmailSignInCancellation is null ? Visibility.Visible : Visibility.Collapsed;
        GmailSignIn.Content = UiLanguage.Value(GmailSignIn, "Content", () => _gmailAuth is null ? UiText.Get("RetrySavedAccount") : UiText.Get("SignInWithGoogle"));
        GmailSignIn.IsEnabled = !_gmailBusy;
        GmailCancel.Visibility = _gmailSignInCancellation is null ? Visibility.Collapsed : Visibility.Visible;
        GmailMore.Visibility = signedIn && _gmailSelected is null && _gmailInbox?.NextPageToken is not null ? Visibility.Visible : Visibility.Collapsed;
        GmailMore.IsEnabled = !_gmailBusy;
        GmailContent.Children.Clear();
        if (!signedIn) { PaintGmail(); return; }
        if (_gmailSelected is GmailMessage selected)
        {
            var back = UiLanguage.Watch(new Button { Content = UiText.Get("UnreadEmails"), IsEnabled = !_gmailBusy }, ("Content", () => (object?)(UiText.Get("UnreadEmails"))));
            back.Click += (_, _) => { _gmailSelected = null; RenderGmail(); };
            GmailContent.Children.Add(back);
            GmailContent.Children.Add(MailText(selected.Subject, 17, bold: true));
            GmailContent.Children.Add(MailText(selected.Sender + "\n" + selected.Date.ToLocalTime().ToString("g", UiText.Culture), 12));
            GmailContent.Children.Add(MailText(selected.Body ?? (_gmailBusy ? UiText.Get("LoadingMessage") : UiText.Get("MessageUnavailableUseRefreshAndTryAgain")), 14));
        }
        else
        {
            if (_gmailMessages.Count == 0 && !_gmailBusy && _gmailInbox is not null)
                GmailContent.Children.Add(MailText(UiText.Get("NoUnreadEmailsMessage"), 14));
            foreach (var message in _gmailMessages)
            {
                var content = new StackPanel { Spacing = 4 };
                content.Children.Add(MailText(message.Sender, 12, selectable: false));
                content.Children.Add(MailText(message.Subject, 14, bold: true, selectable: false));
                content.Children.Add(new TextBlock { Text = message.Preview, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                    MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.7 });
                content.Children.Add(new TextBlock { Text = message.Date.ToLocalTime().ToString("g", UiText.Culture), FontSize = 11, Opacity = 0.7 });
                var row = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 8, 6, 8), IsEnabled = !_gmailBusy };
                row.Click += async (_, _) => await ReadGmailMessageAsync(message);
                GmailContent.Children.Add(row);
            }
        }
        PaintGmail();
    }

    private static TextBlock MailText(string text, double size, bool bold = false, bool selectable = true) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = selectable,
        FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
    };

    private Task ShowGmailSetupAsync() => RunOperationAsync(ShowGmailSetupCoreAsync);

    private async Task ShowGmailSetupCoreAsync()
    {
        if (_gmailSettingsOpen || _gmailBusy || _appearanceDialogOpen || _setupDialogOpen) return;
        await RestoreGmailSessionAsync();
        if (_gmailCancellation.IsCancellationRequested) return;
        if (_gmailAuth is null) { OpenGmail(); return; }
        _gmailSettingsOpen = true;
        GmailFlyout.Hide();
        if (_gmailAuth?.IsSignedIn == true)
        {
            try
            {
                var connectedDialog = UiLanguage.Watch(new ContentDialog
                {
                    XamlRoot = XamlRoot, Title = UiText.Get("GmailConnected"), CloseButtonText = UiText.Get("Close"),
                    Content = MailText(UiText.Format("GoogleSessionRestored", _gmailAuth.Email), 14)
                }, ("Title", () => (object?)(UiText.Get("GmailConnected"))), ("CloseButtonText", () => (object?)(UiText.Get("Close"))));
                StyleDialog(connectedDialog);
                await ShowDialogAsync(connectedDialog);
            }
            finally { _gmailSettingsOpen = false; FocusComposer(); }
            return;
        }
        var state = MailText(_gmailAuth?.IsConfigured == true ? UiText.Get("DesktopOAuthClientConfiguredYouCanSignInMessage") : UiText.Get("ImportYourGoogleDesktopAppOAuthClientTo"), 14);
        var panel = new StackPanel { Spacing = 14, MaxWidth = 400 };
        panel.Children.Add(state);
        panel.Children.Add(MailText(UiText.Get("WhenReadyEnableGmailAPIInGoogleCloud"), 14));
        panel.Children.Add(MailText(UiText.Get("TheReaderRequestsReadonlyGmailAccessSavingDrafts"), 12));
        panel.Children.Add(MailText(UiText.Get("StandardGmailAPIUseIsFreeWithinGoogles"), 12));
        var import = UiLanguage.Watch(new Button { Content = UiText.Get("ImportClientJSON"), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = _gmailAuth is not null }, ("Content", () => (object?)(UiText.Get("ImportClientJSON"))));
        panel.Children.Add(import);
        var link = UiLanguage.Watch(new HyperlinkButton { Content = UiText.Get("GoogleCloudConsole"),
            HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0) }, ("Content", () => (object?)(UiText.Get("GoogleCloudConsole"))));
        link.Click += async (_, _) => await RunOperationAsync(async () =>
        {
            if (!await _launchBrowser(new Uri("https://console.cloud.google.com/apis/credentials")))
                SetStatus(UiText.Get("CouldNotOpenChromeCheckThatGoogleChromeMessage"), StatusTone.Warning);
        });
        panel.Children.Add(link);
        var dialog = UiLanguage.Watch(new ContentDialog { XamlRoot = XamlRoot, Title = UiText.Get("GmailSetup"), Content = panel,
            CloseButtonText = UiText.Get("NotNow"), PrimaryButtonText = UiText.Get("SignInWithGoogle"), IsPrimaryButtonEnabled = _gmailAuth?.IsConfigured == true }, ("Title", () => (object?)(UiText.Get("GmailSetup"))), ("CloseButtonText", () => (object?)(UiText.Get("NotNow"))), ("PrimaryButtonText", () => (object?)(UiText.Get("SignInWithGoogle"))));
        StyleDialog(dialog);
        import.Click += async (_, _) =>
        {
            import.IsEnabled = false;
            try
            {
                var picker = new FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHost?.Handle ?? 0);
                picker.FileTypeFilter.Add(".json");
                var file = await picker.PickSingleFileAsync();
                if (file is null) return;
                if ((await file.GetBasicPropertiesAsync()).Size > 65536) throw new InvalidDataException(UiText.Get("TheGoogleClientFileIsTooLarge"));
                var json = await Windows.Storage.FileIO.ReadTextAsync(file);
                _gmailAuth!.Configure(GmailClientConfiguration.FromJson(json));
                _gmailInbox = null;
                _gmailMessages.Clear();
                _gmailSelected = null;
                _gmailError = null;
                state.Text = UiLanguage.Value(state, "Text", () => UiText.Get("DesktopOAuthClientConfiguredYouCanSignIn"));
                dialog.IsPrimaryButtonEnabled = true;
                RenderGmail();
            }
            catch (Exception exception) { state.Text = GmailError(exception); }
            finally { import.IsEnabled = true; }
        };
        try
        {
            var result = await ShowDialogAsync(dialog);
            _gmailSettingsOpen = false;
            if (result == ContentDialogResult.Primary)
            {
                OpenGmail();
                await SignInGmailAsync();
            }
        }
        finally { _gmailSettingsOpen = false; FocusComposer(); }
    }

    private void PaintGmail()
    {
        var dark = RequestedTheme == ElementTheme.Dark;
        var transparent = new SolidColorBrush(Color(0, 0, 0, 0));
        var text = TextBrush();
        var muted = ToneBrush(MessageTone.Muted);
        var hover = new SolidColorBrush(dark ? Color(0x22, 0xFF, 0xFF, 0xFF) : Color(0x12, 0, 0, 0));
        GmailButton.Foreground = text;
        GmailButton.Background = transparent;
        GmailBadge.Foreground = text;
        GmailBadge.Opacity = WeatherOutlook.Opacity;
        _gmailPreview.Paint(text);
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(dark ? Color(0xFA, 0x16, 0x1D, 0x29) : Color(0xFA, 0xF7, 0xFA, 0xFE))));
        style.Setters.Add(new Setter(Control.ForegroundProperty, text));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(dark ? Color(0xFF, 0x35, 0x40, 0x55) : Color(0xFF, 0xD6, 0xDF, 0xEA))));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(12)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(18)));
        GmailFlyout.FlyoutPresenterStyle = style;
        if (GmailFlyout.Content is StackPanel panel)
        {
            panel.RequestedTheme = RequestedTheme;
            PaintMailChildren(panel);
        }
        StyleMailButton(GmailButton);
        StyleMailButton(GmailMessageButton);

        void PaintMailChildren(DependencyObject parent)
        {
            if (parent is TextBlock block) block.Foreground = text;
            if (parent is Button button) StyleMailButton(button);
            if (parent is Panel children) foreach (var child in children.Children) PaintMailChildren(child);
            if (parent is ContentControl control && control.Content is DependencyObject content) PaintMailChildren(content);
        }
        void StyleMailButton(Button button)
        {
            UseStableWidgetButton(button);
            button.Foreground = text;
            button.Background = transparent;
            button.BorderThickness = new Thickness(0);
            button.CornerRadius = new CornerRadius(8);
            button.Resources["ButtonBackground"] = transparent;
            button.Resources["ButtonBackgroundPointerOver"] = hover;
            button.Resources["ButtonBackgroundPressed"] = hover;
            button.Resources["ButtonForeground"] = text;
            button.Resources["ButtonForegroundPointerOver"] = text;
            button.Resources["ButtonForegroundPressed"] = text;
            button.Resources["ButtonForegroundDisabled"] = muted;
        }
    }

    private string GmailError(Exception exception)
    {
        _gmailFailureKind = exception.GetType().Name + $" HRESULT {exception.HResult:X8}" +
            (exception is Win32Exception native ? $" native {native.NativeErrorCode}" : "") +
            (exception is HttpRequestException http && http.StatusCode is not null ? $" ({(int)http.StatusCode})" : "");
        return exception switch
        {
        GmailSignInRequiredException => exception.Message,
        InvalidDataException => exception.Message,
        InvalidOperationException => exception.Message,
        JsonException => UiText.Get("CouldNotReadTheGoogleClientOrGmail"),
        HttpRequestException => UiText.Get("GmailIsUnavailableCheckYourConnectionUnreadEmails"),
        OperationCanceledException => UiText.Get("GmailDidNotRespondInTimePleaseTry"),
        IOException or UnauthorizedAccessException or Win32Exception => UiText.Get("WindowsCouldNotSecurelySaveOrReadThe"),
        _ => UiText.Get("CouldNotConnectGmailCheckTheSetupAnd")
        };
    }

}
