using Habitat.Models;
using Habitat.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.System;
using Windows.UI.Core;

namespace Habitat;

public sealed partial class MainPage : Page
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private readonly ConversationSession _conversation = new();
    private readonly Grid _root = new();
    private readonly Features.Conversation.ConversationView _conversationView;
    private ScrollViewer ConversationScroll => _conversationView.Scroll;
    private StackPanel Conversation => _conversationView.Messages;
    private readonly TextBox Composer = new();
    private readonly TextBlock ComposerHint = new()
    {
        FontSize = 12, MinHeight = 18, Opacity = 0.8, TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false
    };
    private Microsoft.UI.Xaml.Shapes.Shape? _composerCaret;
    private readonly Button SendButton = new();
    private readonly Button OverflowButton = new();
    private readonly MenuFlyout OptionsMenu = new();
    internal Button WindowMenuButton => OverflowButton;
    private readonly AppServices _services;
    private WeatherService _weather => _services.Weather;
    private readonly CancellationTokenSource _weatherCancellation = new();
    private readonly Grid WeatherBlock = new()
    {
        Height = 78, HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Viewbox WeatherSymbol = new() { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock WeatherTemperature = new() { Text = "—", FontSize = 22 };
    private readonly TextBlock WeatherLocation = new() { FontSize = 11, Opacity = 0.72 };
    private readonly TextBlock WeatherOutlook = UiLanguage.Watch(new TextBlock() { Text = UiText.Get("LoadingWeather"), FontSize = 12, Opacity = 0.8 }, ("Text", () => (object?)(UiText.Get("LoadingWeather"))));
    private WeatherSnapshot? _weatherSnapshot;
    private bool _locationSettingsOpened;
    private readonly WeatherSession _weatherSession;
    private bool _weatherRefreshing => _weatherSession.IsRefreshing;
    private DateTimeOffset _nextWeatherRefresh { get => _weatherSession.NextRefresh; set => _weatherSession.NextRefresh = value; }
    private BridgeService? _bridge;
    private Preferences _preferences;
    private readonly Features.Preferences.WallpaperSurface _wallpaperSurface;
    private Services.WallpaperSettings? _previewWallpaper;
    private string? _unavailableWallpaperId;
    private string _activeModel = UiText.Get("OpenAIModel");
    private bool _hybridDark;
    private bool _appearanceDialogOpen;
    private DateTimeOffset _nextHybridProbe = DateTimeOffset.MinValue;

    internal bool HasAppearanceOverlay => _appearanceDialogOpen || _gmailSettingsOpen || _terminalConfirmationOpen || _setupDialogOpen || _marketsPanelOpen;

    internal MainPage(AppServices services, IWindowHost? windowHost = null)
    {
        _services = services;
        _wallpaperSurface = new Features.Preferences.WallpaperSurface(services.Wallpapers)
            { Margin = new Thickness(-12, 0, -30, 0) };
        _composer = new(_composerDrafts, RestoreComposerDraft);
        _composer.StateChanged += UpdateSendState;
        _composer.IsEnabled = id => _destinationCatalog.Enabled.Contains(id);
        _conversationView = new(_conversation, ToneBrush, TextBrush);
        _windowHost = windowHost;
        _preferences = Preferences.Load(services.Paths.PreferencesPath);
        UiText.SetLanguage(_preferences.InterfaceLanguage);
        _destinationCatalog = new(_preferences.EnabledDestinations, _preferences.WindowsApps);
        _taskStore = services.Tasks;
        _taskSession = services.TaskSession;
        _weatherSession = new WeatherSession(services.Weather, services.GetLocationAsync);
        _marketsSession = new MarketsSession(services.Markets);
        _marketsSession.Configure(_preferences.Markets);
        _marketsSession.Changed += () => { if (!_operations.IsStopping) RenderMarkets(); };
        _gmailSession = new GmailSession(services.CreateGmailAuth, services.CreateGmail, GmailError);
        _gmailSession.Changed += () => { if (!_operations.IsStopping) RenderGmail(); };
        _bridge = services.Bridge;
        _launchBrowser = services.LaunchBrowser;
        _launchNotepad = services.LaunchNotepad;
        _launchCodex = services.LaunchCodex;
        _openRun = services.Run.Open;
        _openChat = () => services.Bridge.HasBrowserConnection ? services.Bridge.OpenChatAsync() :
            _launchBrowser(new Uri("https://chatgpt.com/"));
        _powerShell = services.PowerShell;
        _launchRun = services.Run.Run;
        _bridge.EventReceived += Bridge_EventReceived;
        _bridge.StartFailed += Bridge_StartFailed;
        BuildInterface();
        ApplyInterfaceLanguage();
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += Timer_Tick;
#if !HABITAT_UI_CHECKS
        Loaded += MainPage_Loaded;
#endif
        Unloaded += MainPage_Unloaded;
    }

    private void BuildInterface()
    {
        var surface = new Grid
        {
            Padding = new Thickness(12, 0, 30, 0),
            // A transparent brush makes the full client area clickable,
            // including the space outside the centered chat column.
            Background = new SolidColorBrush(Color(0, 0, 0, 0))
        };
        surface.AddHandler(UIElement.TappedEvent, new TappedEventHandler(Surface_Tapped), true);
        surface.Children.Add(_wallpaperSurface);
        // Stretch fills smaller windows; MaxWidth keeps the chat centered in
        // the available layout slot when the window is wider or maximized.
        _root.MaxWidth = 867;
        _root.HorizontalAlignment = HorizontalAlignment.Stretch;
        _root.SizeChanged += (_, _) => UpdateWritingLayout();

        OverflowButton.Content = new FontIcon { Glyph = "\uE700", FontSize = 14 };
        OverflowButton.Width = 40;
        OverflowButton.Height = 32;
        OverflowButton.IsTabStop = true;
        UiLanguage.SetToolTip(OverflowButton, () => UiText.Get("HABITATMenu"));
        UiLanguage.SetName(OverflowButton, () => UiText.Get("HABITATMenu"));
        OverflowButton.Padding = new Thickness(0);
        OverflowButton.BorderThickness = new Thickness(0);
        OverflowButton.VerticalAlignment = VerticalAlignment.Center;
        OptionsMenu.Placement = FlyoutPlacementMode.BottomEdgeAlignedRight;
        var preferences = UiLanguage.Watch(new MenuFlyoutItem { Text = UiText.Get("PreferencesMessage") }, ("Text", () => (object?)(UiText.Get("PreferencesMessage"))));
        preferences.Click += ShowPreferences_Click;
        OptionsMenu.Items.Add(preferences);
        var setup = UiLanguage.Watch(new MenuFlyoutItem { Text = UiText.Get("SetUpHABITATMessage") }, ("Text", () => (object?)(UiText.Get("SetUpHABITATMessage"))));
        setup.Click += async (_, _) => await RunOperationAsync(ShowSetupAsync);
        OptionsMenu.Items.Add(setup);
        var gmail = new MenuFlyoutItem { Text = "Gmail…" };
        gmail.Click += (_, _) => OpenGmail();
        OptionsMenu.Items.Add(gmail);
        var tasks = UiLanguage.Watch(new MenuFlyoutItem { Text = UiText.Get("TasksMessage") }, ("Text", () => (object?)(UiText.Get("TasksMessage"))));
        tasks.Click += (_, _) => OpenTasks();
        OptionsMenu.Items.Add(tasks);
        OptionsMenu.Items.Add(new MenuFlyoutSeparator());
        var pairing = UiLanguage.Watch(new MenuFlyoutItem { Text = UiText.Get("PairingCodeMessage") }, ("Text", () => (object?)(UiText.Get("PairingCodeMessage"))));
        pairing.Click += ShowPairing_Click;
        OptionsMenu.Items.Add(pairing);
        var copy = UiLanguage.Watch(new MenuFlyoutItem { Text = UiText.Get("CopyCode") }, ("Text", () => (object?)(UiText.Get("CopyCode"))));
        copy.Click += CopyPairingCode_Click;
        OptionsMenu.Items.Add(copy);
        OptionsMenu.Items.Add(new MenuFlyoutSeparator());
        var about = UiLanguage.Watch(new MenuFlyoutItem { Text = UiText.Get("AboutMenu") }, ("Text", () => (object?)(UiText.Get("AboutMenu"))));
        about.Click += async (_, _) => await RunOperationAsync(ShowAboutAsync);
        OptionsMenu.Items.Add(about);
        OptionsMenu.Closed += (_, _) => FocusComposer();
        OverflowButton.Flyout = OptionsMenu;

        Conversation.Spacing = 22;
        ConversationScroll.Content = Conversation;
        ConversationScroll.Padding = new Thickness(2, 18, 8, 18);
        ConversationScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        ConversationScroll.VerticalAlignment = VerticalAlignment.Top;
        _root.Children.Add(ConversationScroll);
        BuildDestinationSections();

        Composer.MinHeight = 58;
        Composer.MaxHeight = 180;
        Composer.Padding = new Thickness(0, 9, 0, 9);
        Composer.FontFamily = new FontFamily("Cascadia Mono");
        ComposerHint.FontFamily = Composer.FontFamily;
        ComposerHint.SizeChanged += (_, _) => UpdateWritingLayout();
        Composer.TextChanging += (_, _) =>
        {
            if (!_restoringComposerDraft)
            {
                _composerDrafts.Edit(_destination, Composer.Text);
                if (_destination == ComposerDestination.Codex) CancelPendingCodexSend();
                ResetEmptySearch();
            }
        };
        Composer.TextChanged += (_, _) => { UpdateWritingLayout(); UpdateExternalDestinationState(); };
        Composer.TextAlignment = TextAlignment.Center;
        Composer.VerticalContentAlignment = VerticalAlignment.Center;
        Composer.VerticalAlignment = VerticalAlignment.Top;
        Composer.SizeChanged += (_, _) => UpdateWritingLayout();
        // WinUI creates/replaces its caret lazily (focus, layout and RTL input).
        Composer.LayoutUpdated += (_, _) => PaintComposerCaret();
        Composer.AcceptsReturn = true;
        Composer.TextWrapping = TextWrapping.Wrap;
        Composer.PlaceholderText = string.Empty;
        Composer.BorderThickness = new Thickness(0);
        Composer.IsTabStop = true;
        // Handle Enter before TextBox processes it. Plain Enter sends without
        // adding a newline; Shift+Enter reaches TextBox and inserts one.
        Composer.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(Composer_KeyDown), true);
        _root.Children.Add(Composer);
        _root.Children.Add(ComposerHint);
        SendButton.Content = UiLanguage.Value(SendButton, "Content", () => UiText.Get("Send"));
        SendButton.MinWidth = 64;
        SendButton.MinHeight = 38;
        SendButton.IsTabStop = false;
        SendButton.Padding = new Thickness(12, 9, 12, 9);
        SendButton.BorderThickness = new Thickness(0);
        SendButton.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        SendButton.HorizontalAlignment = HorizontalAlignment.Center;
        SendButton.VerticalAlignment = VerticalAlignment.Top;
        SendButton.SizeChanged += (_, _) => UpdateWritingLayout();
        SendButton.Click += SendButton_Click;
        _root.Children.Add(SendButton);
        BuildCodexScheduling();

        surface.Children.Add(_root);
        BuildTaskInterface(surface);
        BuildGmailInterface(_widgets);
        surface.Children.Add(_widgets);
        BuildWeatherInterface(_widgets);
        BuildMarketsInterface();
        BuildDestinationInterface(surface);
        BuildUpdateInterface(surface);
        surface.SizeChanged += (_, _) => SizeTopWidgets(surface);
        Content = surface;
        PaintSurface();
    }

    private void Surface_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (HasAppearanceOverlay || _gmailPanelOpen) return;

        // Keep native editing, text selection and interactive controls intact.
        // Blank areas, message spacing and the weather display focus writing.
        for (var element = e.OriginalSource as DependencyObject;
            element is not null && !ReferenceEquals(element, sender);
            element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase or TextBox or RichEditBox or PasswordBox or Selector or
                Slider or ScrollBar or Thumb or MenuFlyoutPresenter or MenuFlyoutItemBase or ContentDialog ||
                element is TextBlock { IsTextSelectionEnabled: true })
            {
                return;
            }
        }

        FocusComposer();
    }

    private void UpdateWritingLayout()
    {
        if (Content is Grid surface) SizeTopWidgets(surface);
        var height = _root.ActualHeight;
        if (height <= 0) return;
        ComposerHint.Visibility = Composer.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Preserve the space above the content that the former weather header occupied.
        const double historyTop = 100;
        ConversationScroll.Margin = new Thickness(0, historyTop, 0, 0);
        if (_destination == ComposerDestination.OpenAI && Conversation.Children.Count > 0 || HasDestinationSection)
        {
            const double bottomPadding = 24;
            var sendWidth = SendButton.ActualWidth > 0 ? SendButton.ActualWidth : 60;
            // Match the single-line editor to Send; longer drafts keep their
            // final line at the same bottom anchor as the button label.
            Composer.MinHeight = SendButton.ActualHeight > 0 ? SendButton.ActualHeight : 38;
            Composer.MaxHeight = 180;
            Composer.TextAlignment = TextAlignment.Left;
            Composer.VerticalContentAlignment = VerticalAlignment.Bottom;
            Composer.VerticalAlignment = VerticalAlignment.Bottom;
            var scheduleSpace = _destination == ComposerDestination.Codex ? 44 : 0;
            Composer.Margin = new Thickness(0, 0, sendWidth + 10 + scheduleSpace, bottomPadding);
            SendButton.HorizontalAlignment = HorizontalAlignment.Right;
            SendButton.VerticalAlignment = VerticalAlignment.Bottom;
            SendButton.Padding = new Thickness(12, 9, 0, 9);
            SendButton.Margin = new Thickness(0, 0, scheduleSpace, bottomPadding);
            PositionCodexScheduleButton(conversation: true);

            var editorHeight = Composer.ActualHeight > 0 ? Composer.ActualHeight : Composer.MinHeight;
            UpdateComposerHintLayout(height - bottomPadding - editorHeight);
            var hintSpace = ComposerHint.Visibility == Visibility.Visible ? ComposerHintHeight + 16 : 0;
            ConversationScroll.Visibility = IsDestinationSectionVisible ? Visibility.Collapsed : Visibility.Visible;
            ConversationScroll.Height = Math.Max(0, height - bottomPadding - editorHeight - hintSpace - 10 - historyTop);
            _destinationContent.Margin = new Thickness(0, historyTop, 0, 0);
            _destinationContent.Height = ConversationScroll.Height;
            return;
        }

        // The centered writing layout is only the welcome state. The first
        // message restores the bottom composer and gives the history space.
        Composer.MinHeight = 58;
        Composer.TextAlignment = TextAlignment.Center;
        Composer.VerticalContentAlignment = VerticalAlignment.Center;
        Composer.VerticalAlignment = VerticalAlignment.Top;
        SendButton.HorizontalAlignment = HorizontalAlignment.Center;
        SendButton.VerticalAlignment = VerticalAlignment.Top;
        SendButton.Padding = new Thickness(12, 9, 12, 9);
        ConversationScroll.Visibility = Visibility.Collapsed;
        var sendHeight = SendButton.ActualHeight > 0 ? SendButton.ActualHeight : 38;
        // Reserve space between the midpoint and two-thirds anchor even when
        // a multiline draft grows; longer drafts scroll inside the editor.
        Composer.MaxHeight = Math.Clamp(height / 3 - sendHeight - 24, Composer.MinHeight, 180);
        var composerHeight = Composer.ActualHeight > 0 ? Composer.ActualHeight : Composer.MinHeight;
        var composerTop = Math.Max(0, (height - composerHeight) / 2);
        var sendTop = Math.Max(height * 2 / 3 - sendHeight / 2, composerTop + composerHeight + 12);
        sendTop = Math.Min(sendTop, Math.Max(0, height - sendHeight));
        Composer.Margin = new Thickness(0, composerTop, 0, 0);
        UpdateComposerHintLayout(composerTop);
        SendButton.Margin = new Thickness(0, sendTop, 0, 0);
        PositionCodexScheduleButton(conversation: false);
        // Project context stays above the welcome editor without moving it.
        _destinationContent.Margin = new Thickness(0, historyTop, 0, 0);
        _destinationContent.Height = Math.Max(0, ComposerHint.Margin.Top - 16 - historyTop);

    }

    private double ComposerHintHeight => ComposerHint.ActualHeight > 0 ? ComposerHint.ActualHeight : ComposerHint.MinHeight;

    private void UpdateComposerHintLayout(double editorTop)
    {
        ComposerHint.TextAlignment = Composer.TextAlignment;
        ComposerHint.Margin = new Thickness(Composer.Margin.Left,
            Math.Max(0, editorTop - ComposerHintHeight - 16), Composer.Margin.Right, 0);
    }

    private bool HasActiveConversation => _destination switch
    {
        ComposerDestination.OpenAI => _conversation.HasActiveConversation,
        ComposerDestination.Codex => CodexChat.HasMessages,
        _ => false
    };

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Freeze the migrated selection so future catalog additions remain opt-in.
        SavePreferences();
        _ = RunOperationAsync(ApplyGlobalHotkeyAsync);
        ApplyPreferences();
        UpdateDestinationStatus();
        _timer.Start();
        TickUpdates(DateTimeOffset.UtcNow);
        TickCodexSchedules(DateTimeOffset.UtcNow);
        RestoreUpdateRestartState();
        FocusComposer();
        if (!_preferences.SetupGuideSeen) _ = RunOperationAsync(ShowSetupAsync);
    }

    private async void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        try { await StopAsync(); }
        catch (Exception exception) { _services.ReportError("Unload page", exception); }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) => SendCurrentMessage();

    private void Composer_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if ((shift & CoreVirtualKeyStates.Down) != 0)
        {
            return;
        }

        e.Handled = true;
        SendCurrentMessage();
    }

    private void SendCurrentMessage(bool openExternally = false)
    {
        if (_operations.IsStopping || _composer.IsExclusiveBusy) return;
        if (_destination.Length == 0)
        { _ = RunOperationAsync(() => ShowPreferencesAsync("Destinations")); return; }
        if (!_composer.CanSend(_destination, Composer.Text, openExternally) ||
            string.IsNullOrWhiteSpace(Composer.Text) && _destinationCatalog.WindowsApp(_destination) is null)
        { FocusComposer(); return; }
        _ = LaunchDestinationAsync(_destination, Composer.Text, openExternally);
    }

    private void Timer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        TickGmail();
        TickTasks(DateTimeOffset.UtcNow);
        TickMarkets();
        TickUpdates(DateTimeOffset.UtcNow);
        TickCodexSchedules(DateTimeOffset.UtcNow);
        if (!_weatherRefreshing && !HasAppearanceOverlay && DateTimeOffset.UtcNow >= _nextWeatherRefresh)
        {
            _ = RefreshWeatherAsync();
        }
        if (_preferences.Theme == "hybrid" && !HasAppearanceOverlay && DateTimeOffset.UtcNow >= _nextHybridProbe)
        {
            RefreshHybridAppearance();
            _nextHybridProbe = DateTimeOffset.UtcNow.AddMilliseconds(750);
        }

        if (_bridge is null)
        {
            return;
        }

        if (_bridge.TryExpireMessage(BridgeProtocol.RequestTimeout))
        {
            AddMessage(UiText.Get("SYSTEM"), UiText.Get("TheRequestTimedOutCheckChatGPTBeforeSending"), MessageTone.Muted);
            SetOpenAIStatus(UiText.Get("TimedOutCheckChrome"), StatusTone.Danger);
        }
        else if (!_bridge.HasMessageInFlight && _destination == ComposerDestination.OpenAI)
        {
            UpdateDestinationStatus();
        }

        UpdateSendState();
    }

    private Task RefreshWeatherAsync() => RunOperationAsync(RefreshWeatherCoreAsync);

    private async Task RefreshWeatherCoreAsync()
    {
        var city = _preferences.WeatherCity;
        var useDeviceLocation = _preferences.UseDeviceLocation;
        try
        {
            var snapshot = await _weatherSession.RefreshAsync(city, useDeviceLocation, _windowHost?.IsActive == true,
                () => city == _preferences.WeatherCity && useDeviceLocation == _preferences.UseDeviceLocation,
                _weatherCancellation.Token);
            if (snapshot is null || _weatherCancellation.IsCancellationRequested || city != _preferences.WeatherCity ||
                useDeviceLocation != _preferences.UseDeviceLocation) return;
            _weatherSnapshot = snapshot;
            WeatherLocation.Text = snapshot.DisplayLocation;
            WeatherTemperature.Text = snapshot.Temperature.ToString("0", UiText.Culture) + "°C";
            WeatherOutlook.Text = $"{UiText.LocalizeMessage(snapshot.Outlook)} · {snapshot.PrecipitationChance.ToString(UiText.Culture)}% · 6h";
            PaintWeather();
        }
        catch (OperationCanceledException) when (_weatherCancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or
            JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
        {
            if (_weatherCancellation.IsCancellationRequested || city != _preferences.WeatherCity ||
                useDeviceLocation != _preferences.UseDeviceLocation) return;
            _nextWeatherRefresh = DateTimeOffset.UtcNow.AddMinutes(5);
            WeatherOutlook.Text = UiLanguage.Value(WeatherOutlook, "Text", () => _weatherSnapshot is null ? UiText.Get("WeatherUnavailable") :
                UiText.Format("OfflineUpdated", _weatherSnapshot.UpdatedAt.ToLocalTime()));
            UpdateWeatherSummary();
        }
    }

    public void OnWindowActivated()
    {
        if (_locationSettingsOpened)
        {
            _locationSettingsOpened = false;
            _services.ResetLocationAccess();
            if (_preferences.UseDeviceLocation) _nextWeatherRefresh = DateTimeOffset.MinValue;
        }
        else if (_preferences.UseDeviceLocation && _services.NeedsLocationPermission)
        {
            _nextWeatherRefresh = DateTimeOffset.MinValue;
        }
    }

    private void PaintWeather()
    {
        var text = TextBrush();
        WeatherLocation.Foreground = text;
        WeatherTemperature.Foreground = text;
        WeatherCompactTemperature.Foreground = text;
        WeatherOutlook.Foreground = text;
        UpdateWeatherSummary();
        WeatherSymbol.Child = WeatherIcon.Create(_weatherSnapshot?.Condition ?? WeatherCondition.Cloudy,
            _weatherSnapshot?.IsDay ?? true, text);
    }

    private void Bridge_EventReceived(object? sender, BridgeEvent bridgeEvent)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_operations.IsStopping) return;
            if (!string.IsNullOrWhiteSpace(bridgeEvent.Model))
            {
                _activeModel = NormalizeModelName(bridgeEvent.Model);
            }
            switch (bridgeEvent.Type)
            {
                case "received":
                    SetOpenAIStatus(UiText.Get("TheBrowserReceivedTheMessage"), StatusTone.Active);
                    break;
                case "started":
                    SetOpenAIStatus(UiText.Format("IsResponding", _activeModel), StatusTone.Active);
                    break;
                case "response":
                    AddMessage(_activeModel, bridgeEvent.Text ?? string.Empty, MessageTone.Assistant);
                    SetOpenAIStatus(UiText.Get("ResponseReceived"), StatusTone.Success);
                    break;
                case "error":
                    AddMessage(UiText.Get("SYSTEM"), UiText.BridgeMessage(bridgeEvent.Text ?? UiText.Get("UnknownError")), MessageTone.Muted);
                    SetOpenAIStatus(UiText.Get("BridgeError"), StatusTone.Danger);
                    break;
                case "status":
                    var text = UiText.BridgeMessage(bridgeEvent.Text ?? string.Empty);
                    SetOpenAIStatus(text[..Math.Min(100, text.Length)], StatusTone.Active);
                    break;
                case "model":
                    break;
            }
            UpdateSendState();
        });
    }

    private void Bridge_StartFailed(object? sender, string error)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            AddMessage(UiText.Get("SYSTEM"), error, MessageTone.Muted);
            _bridgeStartupFailed = true;
            SetOpenAIStatus(UiText.Get("CouldNotStartTheBridge"), StatusTone.Danger);
        });
    }

    private void AddMessage(string author, string body, MessageTone tone)
    {
        _conversationView.Add(author, body, tone);
        UpdateWritingLayout();
        _conversationView.ScrollToEnd();
    }

    private void SetStatus(string text, StatusTone tone)
    {
        _windowHost?.SetStatusPresentation(text, tone.ToString());
    }

    private (string Text, StatusTone Tone) _openAIStatus = (UiText.Get("SendingToBrowser"), StatusTone.Active);
    private bool _bridgeStartupFailed;

    private void SetOpenAIStatus(string text, StatusTone tone)
    {
        _openAIStatus = (text, tone);
        if (_destination == ComposerDestination.OpenAI) SetStatus(text, tone);
    }

    private void UpdateDestinationStatus()
    {
        if (_destination == ComposerDestination.OpenAI)
        {
            if (_bridge?.HasMessageInFlight == true || _bridgeStartupFailed)
                SetStatus(UiText.LocalizeMessage(_openAIStatus.Text), _openAIStatus.Tone);
            else
                SetStatus(_bridge?.IsConnected == true ? UiText.Get("ExtensionConnected") : _bridge?.ConnectionIssue ?? UiText.Get("ReconnectingToChatGPT"),
                    _bridge?.IsConnected == true ? StatusTone.Success : StatusTone.Warning);
        }
        else if (_destination == ComposerDestination.Codex && !string.IsNullOrWhiteSpace(CodexChat.StatusText))
            SetStatus(CodexChat.StatusText, StatusTone.Active);
        else SetStatus(UiText.Get("Ready"), StatusTone.Success);
    }

    private static string NormalizeModelName(string value)
    {
        var label = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        var family = Regex.Match(label, @"\b(Sol|Astra|Luna|Terra)\b", RegexOptions.IgnoreCase);
        var version = Regex.Match(label, @"\b\d+(?:\.\d+)+\b");
        var effort = Regex.Match(label, @"\b(minimal|low|medium|high|xhigh|max|ultra)\b", RegexOptions.IgnoreCase);
        if (family.Success)
        {
            var familyName = char.ToUpperInvariant(family.Value[0]) + family.Value[1..].ToLowerInvariant();
            return string.Join(" ", new[]
            {
                "OpenAI",
                familyName,
                version.Success ? version.Value : string.Empty,
                effort.Success ? effort.Value.ToLowerInvariant() : string.Empty
            }.Where(part => part.Length > 0));
        }

        var technical = Regex.Match(label, @"\b(?:GPT[- ]?\d+(?:\.\d+)*(?:[- ]?[a-z]+)?|o\d+(?:[- ]?[a-z]+)?)\b", RegexOptions.IgnoreCase);
        if (technical.Success)
        {
            var name = Regex.Replace(technical.Value, @"\s+", "-");
            if (name.StartsWith("gpt", StringComparison.OrdinalIgnoreCase))
            {
                name = "GPT" + name[3..];
            }
            return $"OpenAI {name}";
        }

        return UiText.Get("OpenAIModel");
    }

    private Brush ToneBrush(MessageTone tone)
    {
        var dark = RequestedTheme == ElementTheme.Dark;
        return new SolidColorBrush(tone switch
        {
            MessageTone.User => Color(0xFF, dark ? 0x89 : 0x66, dark ? 0x95 : 0x74, dark ? 0xFF : 0xF4),
            MessageTone.Assistant => Color(0xFF, dark ? 0x55 : 0x24, dark ? 0xC7 : 0x84, dark ? 0x9C : 0x5F),
            _ => Color(0xFF, dark ? 0xA1 : 0x6D, dark ? 0xAE : 0x7B, dark ? 0xC1 : 0x91)
        });
    }

    private Brush TextBrush()
    {
        var dark = RequestedTheme == ElementTheme.Dark;
        return new SolidColorBrush(dark ? Color(0xFF, 0xEE, 0xF3, 0xFB) : Color(0xFF, 0x1B, 0x24, 0x34));
    }

    private static Windows.UI.Color Color(int a, int r, int g, int b) =>
        Windows.UI.Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b);

    private void UpdateSendState()
    {
        CodexScheduleButton.IsEnabled = !_operations.IsStopping && !_services.CodexSchedules.IsSending;
        UpdateExternalDestinationState();
        if (_operations.IsStopping || _composer.IsExclusiveBusy) { SendButton.IsEnabled = false; return; }
        if (_destination.Length == 0) { SendButton.IsEnabled = true; return; }
        if (_destination == ComposerDestination.Codex)
        {
            SendButton.Content = UiLanguage.Value(SendButton, "Content", () => _codexSendCancellation is not null || CodexChat.IsSubmitting ? UiText.Get("SendingToCodex") :
                CodexChat.IsBusy ? UiText.Get("WaitingForCodex") : UiText.Get("SendToCodex"));
            SendButton.IsEnabled = !_services.CodexSchedules.IsSending && _composer.CanSend(_destination, Composer.Text);
            return;
        }
        // Keep Send visible and clickable while reconnecting. The bridge
        // reports why it cannot send without dropping the user's draft.
        SendButton.IsEnabled = _composer.CanSend(_destination, Composer.Text);
    }

    private async void ShowPairing_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(ShowPairingAsync);

    private async Task ShowPairingAsync()
    {
        if (_bridge is null) return;
        var code = new TextBox { Text = _bridge.PairKey, IsReadOnly = true, MinWidth = 400 };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(UiLanguage.Watch(new TextBlock
        {
            Text = UiText.Get("OnlyNeededForManualRecoveryTheExtensionReconnects"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 440
        }, ("Text", () => (object?)(UiText.Get("OnlyNeededForManualRecoveryTheExtensionReconnects")))));
        panel.Children.Add(code);
        var dialog = UiLanguage.Watch(new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = UiText.Get("PairingCode"),
            Content = panel,
            PrimaryButtonText = UiText.Get("CopyCode"),
            CloseButtonText = UiText.Get("Close")
        }, ("Title", () => (object?)(UiText.Get("PairingCode"))), ("PrimaryButtonText", () => (object?)(UiText.Get("CopyCode"))), ("CloseButtonText", () => (object?)(UiText.Get("Close"))));
        StyleDialog(dialog);
        if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary)
        {
            CopyPairingCode();
        }
        FocusComposer();
    }

    private void CopyPairingCode_Click(object sender, RoutedEventArgs e)
    {
        CopyPairingCode();
        FocusComposer();
    }

    private void CopyPairingCode()
    {
        if (_bridge is null) return;
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(_bridge.PairKey);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        SetStatus(UiText.Get("CodeCopied"), StatusTone.Active);
    }

    private void ApplyPreferences()
    {
        _previewWallpaper = null;
        var dark = _preferences.Theme == "night" || _preferences.Theme == "hybrid" && DetectHybridTheme();
        _hybridDark = dark;
        RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        _windowHost?.SetBackgroundAppearance(
            _preferences.Opacity, RequestedTheme == ElementTheme.Dark);
        PaintSurface();
        _ = RunOperationAsync(() => RenderWallpaperAsync(_preferences.Wallpaper));
    }

    private bool DetectHybridTheme()
    {
        var wallpaper = _previewWallpaper ?? _preferences.Wallpaper;
        if (wallpaper.Luminance is not null && wallpaper.SelectedId != _unavailableWallpaperId &&
            _services.Wallpapers.Exists(wallpaper.SelectedId))
            return wallpaper.IsDark(_hybridDark);
        var window = _windowHost;
        return window?.DetectBackdropIsDark(_hybridDark) ?? _hybridDark;
    }

    public void RefreshHybridAppearance(bool force = false)
    {
        if (_preferences.Theme != "hybrid")
        {
            return;
        }

        var dark = DetectHybridTheme();
        if (!force && dark == _hybridDark)
        {
            return;
        }

        _hybridDark = dark;
        RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        _windowHost?.SetBackgroundAppearance(_preferences.Opacity, dark);
        PaintSurface();
        _ = RunOperationAsync(() => RenderWallpaperAsync(_previewWallpaper ?? _preferences.Wallpaper));
    }

    public void SetBackgroundOpacity(double percent)
    {
        PaintSurface();
    }

    private async Task RenderWallpaperAsync(Services.WallpaperSettings settings)
    {
        if (await _wallpaperSurface.ApplyAsync(settings, RequestedTheme == ElementTheme.Dark)) return;
        if ((_previewWallpaper ?? _preferences.Wallpaper).SelectedId != settings.SelectedId ||
            _unavailableWallpaperId == settings.SelectedId) return;
        _unavailableWallpaperId = settings.SelectedId;
        SetStatus(UiText.Get("WallpaperUnavailable"), StatusTone.Warning);
        if (!HasAppearanceOverlay) RefreshHybridAppearance(force: true);
    }

    public void FocusComposer()
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (OptionsMenu.IsOpen || HasAppearanceOverlay || _tasksPanelOpen || _gmailPanelOpen || _gmailCancellation.IsCancellationRequested) return;
            Composer.Focus(FocusState.Keyboard);
            Composer.Select(Composer.Text.Length, 0);
        });
    }

    public void RestoreWindowPlacement(Microsoft.UI.Windowing.AppWindow appWindow)
    {
        if (_preferences.WindowX is not int savedX ||
            _preferences.WindowY is not int savedY ||
            _preferences.WindowWidth is not int savedWidth ||
            _preferences.WindowHeight is not int savedHeight)
        {
            return;
        }

        var requested = new Windows.Graphics.RectInt32(
            savedX,
            savedY,
            Math.Max(480, savedWidth),
            Math.Max(420, savedHeight));
        var display = Microsoft.UI.Windowing.DisplayArea.GetFromRect(
            requested,
            Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
        var workArea = display.WorkArea;
        var width = Math.Min(requested.Width, workArea.Width);
        var height = Math.Min(requested.Height, workArea.Height);
        var x = Math.Clamp(requested.X, workArea.X, workArea.X + workArea.Width - width);
        var y = Math.Clamp(requested.Y, workArea.Y, workArea.Y + workArea.Height - height);
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
    }

    public void SaveWindowPlacement(Microsoft.UI.Windowing.AppWindow appWindow)
    {
        var position = appWindow.Position;
        var size = appWindow.Size;
        _preferences = _preferences with
        {
            WindowX = position.X,
            WindowY = position.Y,
            WindowWidth = size.Width,
            WindowHeight = size.Height
        };
        SavePreferences();
    }

    private void PaintSurface()
    {
        var dark = RequestedTheme == ElementTheme.Dark;
        var canvas = new SolidColorBrush(Color(0, 0, 0, 0));
        var transparent = new SolidColorBrush(Color(0, 0, 0, 0));
        var muted = new SolidColorBrush(dark ? Color(0xFF, 0xA1, 0xAE, 0xC1) : Color(0xFF, 0x6D, 0x7B, 0x91));
        var accent = new SolidColorBrush(dark ? Color(0xFF, 0x89, 0x95, 0xFF) : Color(0xFF, 0x66, 0x74, 0xF4));
        var text = TextBrush();
        // Match the opaque, neutral title bar rather than the acrylic content.
        var menuBackground = new SolidColorBrush(dark ? Color(0xFF, 0, 0, 0) : Color(0xFF, 0xFF, 0xFF, 0xFF));
        var menuText = new SolidColorBrush(dark ? Color(0xFF, 0xED, 0xED, 0xED) : Color(0xFF, 0x20, 0x20, 0x20));
        var menuBorder = new SolidColorBrush(dark ? Color(0xFF, 0x28, 0x28, 0x28) : Color(0xFF, 0xD5, 0xD5, 0xD5));
        var menuHover = new SolidColorBrush(dark ? Color(0xFF, 0x28, 0x28, 0x28) : Color(0xFF, 0xE8, 0xE8, 0xE8));
        var menuPressed = new SolidColorBrush(dark ? Color(0xFF, 0x3A, 0x3A, 0x3A) : Color(0xFF, 0xD5, 0xD5, 0xD5));
        var hover = new SolidColorBrush(dark ? Color(0x33, 0xFF, 0xFF, 0xFF) : Color(0x12, 0, 0, 0));

        _root.Background = canvas;
        Composer.Background = transparent;
        Composer.BorderBrush = transparent;
        Composer.Foreground = text;
        ComposerHint.Foreground = muted;
        PaintComposerCaret();
        SendButton.Background = transparent;
        SendButton.Foreground = accent;
        CodexScheduleButton.Background = transparent;
        CodexScheduleButton.Foreground = accent;
        PaintCodexScheduleNotice();
        foreach (var key in new[] { "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed", "ButtonForegroundDisabled" })
            SendButton.Resources[key] = accent;
        OverflowButton.Background = transparent;
        OverflowButton.Foreground = muted;
        PaintDestinations();
        PaintWeather();
        PaintGmail();
        PaintTasks();
        PaintMarkets();
        PaintUpdateButton();

        var menuStyle = new Style(typeof(MenuFlyoutPresenter));
        menuStyle.Setters.Add(new Setter(Control.BackgroundProperty, menuBackground));
        menuStyle.Setters.Add(new Setter(Control.ForegroundProperty, menuText));
        menuStyle.Setters.Add(new Setter(Control.BorderBrushProperty, menuBorder));
        menuStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1, 0, 1, 1)));
        menuStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(0)));
        menuStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0, 2, 0, 2)));
        // Reduce the standard flyout gap so it reads as a title-bar dropdown.
        menuStyle.Setters.Add(new Setter(UIElement.RenderTransformProperty, new TranslateTransform { Y = -6 }));
        OptionsMenu.MenuFlyoutPresenterStyle = menuStyle;
        foreach(var item in OptionsMenu.Items.OfType<MenuFlyoutItem>())
        {
            item.Foreground = menuText;
            item.Background = transparent;
            item.FontSize = 13;
            item.MinHeight = 30;
            item.Padding = new Thickness(10, 6, 10, 6);
            item.CornerRadius = new CornerRadius(0);
            item.Resources["MenuFlyoutItemThemePaddingNarrow"] = item.Padding;
            item.Resources["MenuFlyoutItemBackgroundPointerOver"] = menuHover;
            item.Resources["MenuFlyoutItemBackgroundPressed"] = menuPressed;
            foreach (var key in new[] { "MenuFlyoutItemForeground", "MenuFlyoutItemForegroundPointerOver", "MenuFlyoutItemForegroundPressed" })
                item.Resources[key] = menuText;
        }
        foreach (var separator in OptionsMenu.Items.OfType<MenuFlyoutSeparator>())
            separator.Resources["MenuFlyoutSeparatorBackground"] = menuBorder;

        // Keep the controls visually absent in all pointer and focus states;
        // typography and color provide the interaction affordance.
        foreach(var key in new[] { "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBackgroundDisabled" })
        {
            _root.Resources[key] = transparent;
            OverflowButton.Resources[key] = key is "ButtonBackgroundPointerOver" or "ButtonBackgroundPressed" ? hover : transparent;
        }
        foreach(var key in new[]
        {
            "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
            "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused"
        })
        {
            Composer.Resources[key] = transparent;
        }
        foreach(var key in new[]
        {
            "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver",
            "TextControlPlaceholderForegroundFocused"
        })
        {
            Composer.Resources[key] = text;
        }
        foreach(var key in new[]
        {
            "TextControlForeground", "TextControlForegroundPointerOver",
            "TextControlForegroundFocused"
        })
        {
            Composer.Resources[key] = text;
        }
        Composer.Resources["TextControlForegroundDisabled"] = muted;

        _conversationView.RefreshAppearance();
    }

    private void PaintComposerCaret()
    {
        PaintCaret(Composer);

        void PaintCaret(DependencyObject parent)
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is Microsoft.UI.Xaml.Shapes.Shape shape &&
                    (ReferenceEquals(shape, _composerCaret) ||
                     (shape.CompositeMode != ElementCompositeMode.Inherit &&
                      shape.CompositeMode != ElementCompositeMode.SourceOver &&
                      shape.CompositeMode != ElementCompositeMode.MinBlend)))
                {
                    // The native caret uses the internal DestInvert mode with a
                    // white fill. Inversion cannot see through our transparent
                    // surface to the desktop acrylic. Draw it in the text color
                    // instead, preserving WinUI's positioning and blink animation.
                    _composerCaret = shape;
                    if (shape.CompositeMode != ElementCompositeMode.SourceOver)
                        shape.CompositeMode = ElementCompositeMode.SourceOver;
                    if (!ReferenceEquals(shape.Fill, Composer.Foreground))
                        shape.Fill = Composer.Foreground;
                }
                PaintCaret(child);
            }
        }
    }

    private void StyleDialog(ContentDialog dialog)
    {
        var dark = RequestedTheme == ElementTheme.Dark;
        var text = TextBrush();
        var popup = new SolidColorBrush(dark ? Color(0xFA, 0x16, 0x1D, 0x29) : Color(0xFA, 0xF7, 0xFA, 0xFE));
        var border = new SolidColorBrush(dark ? Color(0xFF, 0x35, 0x40, 0x55) : Color(0xFF, 0xD6, 0xDF, 0xEA));
        var accent = new SolidColorBrush(dark ? Color(0xFF, 0x89, 0x95, 0xFF) : Color(0xFF, 0x66, 0x74, 0xF4));

        dialog.RequestedTheme = RequestedTheme;
        dialog.Background = popup;
        dialog.Foreground = text;
        dialog.BorderBrush = border;
        dialog.BorderThickness = new Thickness(1);
        dialog.CornerRadius = new CornerRadius(16);
        dialog.Resources["ContentDialogBackground"] = popup;
        dialog.Resources["ContentDialogForeground"] = text;
        dialog.Resources["ContentDialogBorderBrush"] = border;
        dialog.Resources["AccentButtonBackground"] = accent;
        dialog.Resources["AccentButtonBackgroundPointerOver"] = accent;
        dialog.Resources["AccentButtonBackgroundPressed"] = accent;
    }

    private enum StatusTone { Active, Success, Warning, Danger }
}
