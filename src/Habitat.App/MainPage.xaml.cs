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

public sealed class MainPage : Page
{
    private const string TextOnlyDirective =
        "[HABITAT text mode: This message was sent from HABITAT. Reply in plain text only. " +
        "Do not use Markdown tables or create images, files, canvases, interactive content, or other rich output " +
        "unless the user's message explicitly requests it. Use simple paragraphs and plain-text lists when needed.]";

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private readonly Grid _root = new();
    private readonly Grid _header = new();
    private readonly ScrollViewer ConversationScroll = new();
    private readonly StackPanel Conversation = new();
    private readonly TextBox Composer = new();
    private readonly Button SendButton = new();
    private readonly Button OverflowButton = new();
    private readonly MenuFlyout OptionsMenu = new();
    private readonly WeatherService _weather = new();
    private readonly CancellationTokenSource _weatherCancellation = new();
    private readonly Grid WeatherBlock = new();
    private readonly Viewbox WeatherSymbol = new() { Width = 32, Height = 32 };
    private readonly TextBlock WeatherTemperature = new() { Text = "—", FontSize = 22 };
    private readonly TextBlock WeatherLocation = new() { FontSize = 11, Opacity = 0.72 };
    private readonly TextBlock WeatherOutlook = new() { Text = "Loading weather…", FontSize = 12, Opacity = 0.8 };
    private WeatherSnapshot? _weatherSnapshot;
    private bool _weatherRefreshing;
    private DateTimeOffset _nextWeatherRefresh = DateTimeOffset.MinValue;
    private BridgeService? _bridge;
    private Preferences _preferences = Preferences.Load();
    private string _activeModel = "OpenAI model";
    private bool _hybridDark;
    private bool _appearanceDialogOpen;
    private DateTimeOffset _nextHybridProbe = DateTimeOffset.MinValue;

    public MainPage(BridgeService bridge)
    {
        _bridge = bridge;
        _bridge.EventReceived += Bridge_EventReceived;
        _bridge.StartFailed += Bridge_StartFailed;
        BuildInterface();
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += Timer_Tick;
        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
    }

    private void BuildInterface()
    {
        var surface = new Grid { Padding = new Thickness(30, 0, 30, 0) };
        // Stretch fills smaller windows; MaxWidth keeps the chat centered in
        // the available layout slot when the window is wider or maximized.
        _root.MaxWidth = 867;
        _root.HorizontalAlignment = HorizontalAlignment.Stretch;
        _root.SizeChanged += (_, _) => UpdateWritingLayout();

        var header = _header;
        header.ColumnSpacing = 18;
        header.HorizontalAlignment = HorizontalAlignment.Right;
        header.VerticalAlignment = VerticalAlignment.Top;
        header.Margin = new Thickness(0, 20, 0, 0);
        header.SizeChanged += (_, _) => UpdateWritingLayout();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        WeatherBlock.ColumnSpacing = 12;
        WeatherBlock.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        WeatherBlock.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        WeatherSymbol.VerticalAlignment = VerticalAlignment.Center;
        WeatherBlock.Children.Add(WeatherSymbol);
        var weatherText = new StackPanel { Spacing = 2, MinWidth = 155, MaxWidth = 230 };
        WeatherLocation.Text = _preferences.WeatherCity;
        WeatherLocation.TextTrimming = TextTrimming.CharacterEllipsis;
        weatherText.Children.Add(WeatherLocation);
        weatherText.Children.Add(WeatherTemperature);
        weatherText.Children.Add(WeatherOutlook);
        Grid.SetColumn(weatherText, 1);
        WeatherBlock.Children.Add(weatherText);
        header.Children.Add(WeatherBlock);

        OverflowButton.Content = "•••";
        OverflowButton.Width = 34;
        OverflowButton.Height = 34;
        OverflowButton.IsTabStop = false;
        OverflowButton.Padding = new Thickness(0);
        OverflowButton.BorderThickness = new Thickness(0);
        OverflowButton.VerticalAlignment = VerticalAlignment.Top;
        OptionsMenu.Placement = FlyoutPlacementMode.BottomEdgeAlignedRight;
        var preferences = new MenuFlyoutItem { Text = "Preferences…" };
        preferences.Click += ShowPreferences_Click;
        OptionsMenu.Items.Add(preferences);
        OptionsMenu.Items.Add(new MenuFlyoutSeparator());
        var pairing = new MenuFlyoutItem { Text = "Pairing code…" };
        pairing.Click += ShowPairing_Click;
        OptionsMenu.Items.Add(pairing);
        var copy = new MenuFlyoutItem { Text = "Copy code" };
        copy.Click += CopyPairingCode_Click;
        OptionsMenu.Items.Add(copy);
        OptionsMenu.Closed += (_, _) => FocusComposer();
        OverflowButton.Flyout = OptionsMenu;
        Grid.SetColumn(OverflowButton, 1);
        header.Children.Add(OverflowButton);

        Conversation.Spacing = 22;
        ConversationScroll.Content = Conversation;
        ConversationScroll.Padding = new Thickness(2, 18, 8, 18);
        ConversationScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        ConversationScroll.VerticalAlignment = VerticalAlignment.Top;
        _root.Children.Add(ConversationScroll);

        Composer.MinHeight = 58;
        Composer.MaxHeight = 180;
        Composer.Padding = new Thickness(0, 9, 0, 9);
        Composer.FontFamily = new FontFamily("Cascadia Mono");
        Composer.TextAlignment = TextAlignment.Center;
        Composer.VerticalContentAlignment = VerticalAlignment.Center;
        Composer.VerticalAlignment = VerticalAlignment.Top;
        Composer.SizeChanged += (_, _) => UpdateWritingLayout();
        Composer.AcceptsReturn = true;
        Composer.TextWrapping = TextWrapping.Wrap;
        Composer.PlaceholderText = string.Empty;
        Composer.BorderThickness = new Thickness(0);
        Composer.IsTabStop = true;
        // Handle Enter before TextBox processes it. Plain Enter sends without
        // adding a newline; Shift+Enter reaches TextBox and inserts one.
        Composer.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(Composer_KeyDown), true);
        _root.Children.Add(Composer);
        SendButton.Content = "Send";
        SendButton.IsTabStop = false;
        SendButton.Padding = new Thickness(12, 9, 12, 9);
        SendButton.BorderThickness = new Thickness(0);
        SendButton.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        SendButton.HorizontalAlignment = HorizontalAlignment.Center;
        SendButton.VerticalAlignment = VerticalAlignment.Top;
        SendButton.SizeChanged += (_, _) => UpdateWritingLayout();
        SendButton.Click += SendButton_Click;
        _root.Children.Add(SendButton);

        surface.Children.Add(_root);
        surface.Children.Add(header);
        Content = surface;
        PaintSurface();
    }

    private void UpdateWritingLayout()
    {
        var height = _root.ActualHeight;
        if (height <= 0) return;

        var sendHeight = SendButton.ActualHeight > 0 ? SendButton.ActualHeight : 38;
        // Reserve space between the midpoint and two-thirds anchor even when
        // a multiline draft grows; longer drafts scroll inside the editor.
        Composer.MaxHeight = Math.Clamp(height / 3 - sendHeight - 24, Composer.MinHeight, 180);
        var composerHeight = Composer.ActualHeight > 0 ? Composer.ActualHeight : Composer.MinHeight;
        var composerTop = Math.Max(0, (height - composerHeight) / 2);
        var sendTop = Math.Max(height * 2 / 3 - sendHeight / 2, composerTop + composerHeight + 12);
        sendTop = Math.Min(sendTop, Math.Max(0, height - sendHeight));
        Composer.Margin = new Thickness(0, composerTop, 0, 0);
        SendButton.Margin = new Thickness(0, sendTop, 0, 0);

        var historyTop = _header.Margin.Top + _header.ActualHeight + 10;
        ConversationScroll.Margin = new Thickness(0, historyTop, 0, 0);
        ConversationScroll.Height = Math.Max(0, composerTop - historyTop - 16);
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyPreferences();
        SetStatus("Starting bridge…", StatusTone.Active);
        _timer.Start();
        FocusComposer();
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _weatherCancellation.Cancel();
        _weather.Dispose();
        if (_bridge is not null)
        {
            _bridge.EventReceived -= Bridge_EventReceived;
            _bridge.StartFailed -= Bridge_StartFailed;
        }
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

    private void SendCurrentMessage()
    {
        var text = Composer.Text.Trim();
        if (string.IsNullOrWhiteSpace(text) || _bridge is null)
        {
            FocusComposer();
            return;
        }

        var outboundText = _preferences.TextOnly
            ? $"{text}\n\n{TextOnlyDirective}"
            : text;

        if (!_bridge.TryQueueMessage(outboundText, out var reason))
        {
            SetStatus(reason, StatusTone.Warning);
            FocusComposer();
            return;
        }

        AddMessage("YOU", text, MessageTone.User);
        Composer.Text = string.Empty;
        SetStatus("Sending to Chrome…", StatusTone.Active);
        UpdateSendState();
        FocusComposer();
    }

    private void Timer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (!_weatherRefreshing && !_appearanceDialogOpen && DateTimeOffset.UtcNow >= _nextWeatherRefresh)
        {
            _ = RefreshWeatherAsync();
        }
        if (_preferences.Theme == "hybrid" && !_appearanceDialogOpen && DateTimeOffset.UtcNow >= _nextHybridProbe)
        {
            RefreshHybridAppearance();
            _nextHybridProbe = DateTimeOffset.UtcNow.AddMilliseconds(750);
        }

        if (_bridge is null)
        {
            return;
        }

        if (_bridge.TryExpireMessage(TimeSpan.FromMinutes(4)))
        {
            AddMessage("SYSTEM", "The request timed out. Check ChatGPT before sending it again.", MessageTone.Muted);
            SetStatus("Timed out — check Chrome", StatusTone.Danger);
        }
        else if (!_bridge.HasMessageInFlight)
        {
            SetStatus(_bridge.IsConnected ? "Extension connected" : "Reconnecting to ChatGPT…",
                _bridge.IsConnected ? StatusTone.Success : StatusTone.Warning);
        }

        UpdateSendState();
    }

    private async Task RefreshWeatherAsync()
    {
        _weatherRefreshing = true;
        _nextWeatherRefresh = DateTimeOffset.UtcNow.AddMinutes(15);
        var city = _preferences.WeatherCity;
        try
        {
            var snapshot = await _weather.GetAsync(city, _weatherCancellation.Token);
            if (_weatherCancellation.IsCancellationRequested || city != _preferences.WeatherCity) return;
            _weatherSnapshot = snapshot;
            WeatherLocation.Text = snapshot.Location;
            WeatherTemperature.Text = $"{snapshot.Temperature:0}°C";
            WeatherOutlook.Text = $"{snapshot.Outlook} · {snapshot.PrecipitationChance}% · 6h";
            ToolTipService.SetToolTip(WeatherBlock,
                $"{snapshot.Location}\n{snapshot.Condition} · next 6 hours\n" +
                $"Peak hourly precipitation chance: {snapshot.PrecipitationChance}%\n" +
                $"Updated {snapshot.UpdatedAt.ToLocalTime():HH:mm}\nWeather data: Open-Meteo");
            PaintWeather();
        }
        catch (OperationCanceledException) when (_weatherCancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or
            JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
        {
            if (_weatherCancellation.IsCancellationRequested || city != _preferences.WeatherCity) return;
            _nextWeatherRefresh = DateTimeOffset.UtcNow.AddMinutes(5);
            WeatherOutlook.Text = _weatherSnapshot is null ? "Weather unavailable" :
                $"Offline · updated {_weatherSnapshot.UpdatedAt.ToLocalTime():HH:mm}";
            ToolTipService.SetToolTip(WeatherBlock,
                "Could not update the weather. It will retry automatically.\n" +
                "Check the location in Preferences.\nWeather data: Open-Meteo");
        }
        finally
        {
            _weatherRefreshing = false;
        }
    }

    private void PaintWeather()
    {
        var text = TextBrush();
        WeatherLocation.Foreground = text;
        WeatherTemperature.Foreground = text;
        WeatherOutlook.Foreground = text;
        WeatherSymbol.Child = WeatherIcon.Create(_weatherSnapshot?.Condition ?? WeatherCondition.Cloudy,
            _weatherSnapshot?.IsDay ?? true, text);
    }

    private void Bridge_EventReceived(object? sender, BridgeEvent bridgeEvent)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!string.IsNullOrWhiteSpace(bridgeEvent.Model))
            {
                _activeModel = NormalizeModelName(bridgeEvent.Model);
            }
            switch (bridgeEvent.Type)
            {
                case "received":
                    SetStatus("Chrome received the message…", StatusTone.Active);
                    break;
                case "started":
                    SetStatus($"{_activeModel} is responding…", StatusTone.Active);
                    break;
                case "response":
                    AddMessage(_activeModel, bridgeEvent.Text ?? string.Empty, MessageTone.Assistant);
                    SetStatus("Response received", StatusTone.Success);
                    break;
                case "error":
                    AddMessage("SYSTEM", bridgeEvent.Text ?? "Unknown error", MessageTone.Muted);
                    SetStatus("Bridge error", StatusTone.Danger);
                    break;
                case "status":
                    var text = bridgeEvent.Text ?? string.Empty;
                    SetStatus(text[..Math.Min(100, text.Length)], StatusTone.Active);
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
            AddMessage("SYSTEM", error, MessageTone.Muted);
            SetStatus("Could not start the bridge", StatusTone.Danger);
        });
    }

    private void AddMessage(string author, string body, MessageTone tone)
    {
        var group = new StackPanel { Spacing = 5, Tag = tone };
        group.Children.Add(new TextBlock
        {
            Text = author,
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ToneBrush(tone)
        });
        group.Children.Add(new TextBlock
        {
            Text = body,
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = TextBrush()
        });
        Conversation.Children.Add(group);
        ConversationScroll.UpdateLayout();
        ConversationScroll.ChangeView(null, ConversationScroll.ScrollableHeight, null);
    }

    private void SetStatus(string text, StatusTone tone)
    {
        ((App)Application.Current).MainWindow?.SetStatusPresentation(text, tone.ToString());
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

        return "OpenAI model";
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
        SendButton.IsEnabled = _bridge is { IsConnected: true, HasMessageInFlight: false };
    }

    private async void ShowPreferences_Click(object sender, RoutedEventArgs e)
    {
        var opacityValue = new TextBlock { Text = $"{_preferences.Opacity:0}%", VerticalAlignment = VerticalAlignment.Center };
        var opacity = new Slider { Minimum = 0, Maximum = 100, Value = _preferences.Opacity, StepFrequency = 1 };
        opacity.ValueChanged += (_, args) =>
        {
            opacityValue.Text = $"{args.NewValue:0}%";
            ((App)Application.Current).MainWindow?.SetBackgroundAppearance(
                args.NewValue, RequestedTheme == ElementTheme.Dark);
        };
        var theme = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "Day", "Night", "Hybrid" },
            SelectedIndex = _preferences.Theme switch { "night" => 1, "hybrid" => 2, _ => 0 }
        };
        theme.SelectionChanged += (_, _) =>
        {
            var dark = theme.SelectedIndex == 1 ||
                (theme.SelectedIndex == 2 && DetectHybridTheme());
            RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
            ((App)Application.Current).MainWindow?.SetBackgroundAppearance(
                opacity.Value, dark);
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 360 };
        panel.Children.Add(new TextBlock { Text = "Appearance", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(theme);
        var opacityHeader = new Grid();
        opacityHeader.ColumnDefinitions.Add(new ColumnDefinition());
        opacityHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        opacityHeader.Children.Add(new TextBlock { Text = "Background opacity", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        Grid.SetColumn(opacityValue, 1);
        opacityHeader.Children.Add(opacityValue);
        panel.Children.Add(opacityHeader);
        panel.Children.Add(opacity);
        var textOnly = new ToggleSwitch
        {
            Header = "Text-only replies",
            IsOn = _preferences.TextOnly,
            Margin = new Thickness(0, 8, 0, 0)
        };
        panel.Children.Add(textOnly);
        panel.Children.Add(new TextBlock
        {
            Text = "Tell ChatGPT that messages come from HABITAT and avoid tables, images, files and other rich output unless you explicitly request them.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        });
        var weatherCity = new TextBox
        {
            Header = "Weather location",
            Text = _preferences.WeatherCity,
            PlaceholderText = "City, country code (for example Palma, ES)",
            MaxLength = 100,
            Margin = new Thickness(0, 8, 0, 0)
        };
        panel.Children.Add(weatherCity);
        panel.Children.Add(new TextBlock
        {
            Text = "Weather data: Open-Meteo · refreshes every 15 minutes.",
            FontSize = 11,
            Opacity = 0.72
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Preferences",
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        StyleDialog(dialog);
        _appearanceDialogOpen = true;
        await dialog.ShowAsync();
        _appearanceDialogOpen = false;

        var locationChanged = _preferences.WeatherCity != weatherCity.Text.Trim();
        _preferences = _preferences with
        {
            Theme = theme.SelectedIndex switch { 1 => "night", 2 => "hybrid", _ => "day" },
            Opacity = opacity.Value,
            TextOnly = textOnly.IsOn,
            WeatherCity = weatherCity.Text.Trim()
        };
        _preferences.Save();
        ApplyPreferences();
        if (locationChanged)
        {
            _weatherSnapshot = null;
            WeatherLocation.Text = _preferences.WeatherCity;
            WeatherTemperature.Text = "—";
            WeatherOutlook.Text = "Loading weather…";
            ToolTipService.SetToolTip(WeatherBlock, "Weather data: Open-Meteo");
            _nextWeatherRefresh = DateTimeOffset.MinValue;
            PaintWeather();
        }
        FocusComposer();
    }

    private async void ShowPairing_Click(object sender, RoutedEventArgs e)
    {
        if (_bridge is null) return;
        var code = new TextBox { Text = _bridge.PairKey, IsReadOnly = true, MinWidth = 400 };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "Only needed for manual recovery. The extension reconnects automatically when HABITAT opens.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 440
        });
        panel.Children.Add(code);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Pairing code",
            Content = panel,
            PrimaryButtonText = "Copy code",
            CloseButtonText = "Close"
        };
        StyleDialog(dialog);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
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
        SetStatus("Code copied", StatusTone.Active);
    }

    private void ApplyPreferences()
    {
        if (_preferences.Theme == "hybrid")
        {
            RefreshHybridAppearance(force: true);
            return;
        }

        RequestedTheme = _preferences.Theme == "night" ? ElementTheme.Dark : ElementTheme.Light;
        ((App)Application.Current).MainWindow?.SetBackgroundAppearance(
            _preferences.Opacity, RequestedTheme == ElementTheme.Dark);
    }

    private bool DetectHybridTheme()
    {
        var window = ((App)Application.Current).MainWindow;
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
        ((App)Application.Current).MainWindow?.SetBackgroundAppearance(_preferences.Opacity, dark);
    }

    public void SetBackgroundOpacity(double percent)
    {
        _preferences = _preferences with { Opacity = Math.Clamp(percent, 0, 100) };
        PaintSurface();
    }

    public void FocusComposer()
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
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
        _preferences.Save();
    }

    private void PaintSurface()
    {
        var dark = RequestedTheme == ElementTheme.Dark;
        var canvas = new SolidColorBrush(Color(0, 0, 0, 0));
        var transparent = new SolidColorBrush(Color(0, 0, 0, 0));
        var muted = new SolidColorBrush(dark ? Color(0xFF, 0xA1, 0xAE, 0xC1) : Color(0xFF, 0x6D, 0x7B, 0x91));
        var accent = new SolidColorBrush(dark ? Color(0xFF, 0x89, 0x95, 0xFF) : Color(0xFF, 0x66, 0x74, 0xF4));
        var text = TextBrush();
        var popup = new SolidColorBrush(dark ? Color(0xF5, 0x16, 0x1D, 0x29) : Color(0xF7, 0xF7, 0xFA, 0xFE));
        var popupBorder = new SolidColorBrush(dark ? Color(0xFF, 0x35, 0x40, 0x55) : Color(0xFF, 0xD6, 0xDF, 0xEA));
        var hover = new SolidColorBrush(dark ? Color(0x33, 0xFF, 0xFF, 0xFF) : Color(0x12, 0, 0, 0));

        _root.Background = canvas;
        Composer.Background = transparent;
        Composer.BorderBrush = transparent;
        Composer.Foreground = text;
        SendButton.Background = transparent;
        SendButton.Foreground = accent;
        OverflowButton.Background = transparent;
        OverflowButton.Foreground = muted;
        PaintWeather();

        var menuStyle = new Style(typeof(MenuFlyoutPresenter));
        menuStyle.Setters.Add(new Setter(Control.BackgroundProperty, popup));
        menuStyle.Setters.Add(new Setter(Control.ForegroundProperty, text));
        menuStyle.Setters.Add(new Setter(Control.BorderBrushProperty, popupBorder));
        menuStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        menuStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(12)));
        menuStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6)));
        OptionsMenu.MenuFlyoutPresenterStyle = menuStyle;
        foreach(var item in OptionsMenu.Items.OfType<MenuFlyoutItem>())
        {
            item.Foreground = text;
            item.Background = transparent;
            item.Resources["MenuFlyoutItemBackgroundPointerOver"] = hover;
            item.Resources["MenuFlyoutItemBackgroundPressed"] = hover;
        }

        // Keep the controls visually absent in all pointer and focus states;
        // typography and color provide the interaction affordance.
        foreach(var key in new[] { "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBackgroundDisabled" })
        {
            _root.Resources[key] = transparent;
            OverflowButton.Resources[key] = transparent;
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

        foreach(var child in Conversation.Children)
        {
            if(child is not StackPanel group || group.Tag is not MessageTone tone) continue;
            if(group.Children.Count > 0 && group.Children[0] is TextBlock author)
            {
                author.Foreground = ToneBrush(tone);
            }
            if(group.Children.Count > 1 && group.Children[1] is TextBlock body)
            {
                body.Foreground = text;
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
    private enum MessageTone { User, Assistant, Muted }
}

internal sealed record Preferences(string Theme, double Opacity)
{
    public bool TextOnly { get; init; } = true;
    public string WeatherCity { get; init; } = "Palma, ES";
    public int? WindowX { get; init; }
    public int? WindowY { get; init; }
    public int? WindowWidth { get; init; }
    public int? WindowHeight { get; init; }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HABITAT", "preferences.json");

    public static Preferences Load()
    {
        try
        {
            var saved = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath));
            if (saved is not null && saved.Theme is "day" or "night" or "hybrid")
            {
                return saved with { Opacity = Math.Clamp(saved.Opacity, 0, 100) };
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        return new Preferences("day", 85);
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
