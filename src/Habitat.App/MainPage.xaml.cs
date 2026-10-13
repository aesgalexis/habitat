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
    private readonly ScrollViewer ConversationScroll = new();
    private readonly StackPanel Conversation = new();
    private readonly TextBox Composer = new();
    private readonly Button SendButton = new();
    private readonly Button OverflowButton = new();
    private readonly MenuFlyout OptionsMenu = new();
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
        _root.Padding = new Thickness(30, 20, 30, 24);
        _root.RowSpacing = 10;
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition());
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        OverflowButton.Content = "•••";
        OverflowButton.Width = 34;
        OverflowButton.Height = 34;
        OverflowButton.IsTabStop = false;
        OverflowButton.Padding = new Thickness(0);
        OverflowButton.BorderThickness = new Thickness(0);
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
        _root.Children.Add(header);

        Conversation.Spacing = 22;
        ConversationScroll.Content = Conversation;
        ConversationScroll.Padding = new Thickness(2, 18, 8, 18);
        ConversationScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Grid.SetRow(ConversationScroll, 1);
        _root.Children.Add(ConversationScroll);

        var composerGrid = new Grid { ColumnSpacing = 10 };
        composerGrid.ColumnDefinitions.Add(new ColumnDefinition());
        composerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Composer.MinHeight = 58;
        Composer.MaxHeight = 180;
        Composer.Padding = new Thickness(0, 9, 0, 9);
        Composer.FontFamily = new FontFamily("Cascadia Mono");
        Composer.AcceptsReturn = true;
        Composer.TextWrapping = TextWrapping.Wrap;
        Composer.PlaceholderText = string.Empty;
        Composer.BorderThickness = new Thickness(0);
        Composer.IsTabStop = true;
        // Handle Enter before TextBox processes it. Plain Enter sends without
        // adding a newline; Shift+Enter reaches TextBox and inserts one.
        Composer.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(Composer_KeyDown), true);
        composerGrid.Children.Add(Composer);
        SendButton.Content = "Send";
        SendButton.IsTabStop = false;
        SendButton.Padding = new Thickness(12, 9, 0, 9);
        SendButton.BorderThickness = new Thickness(0);
        SendButton.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        SendButton.VerticalAlignment = VerticalAlignment.Bottom;
        SendButton.Click += SendButton_Click;
        Grid.SetColumn(SendButton, 1);
        composerGrid.Children.Add(SendButton);
        Grid.SetRow(composerGrid, 2);
        _root.Children.Add(composerGrid);

        Content = _root;
        PaintSurface();
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

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Preferences",
            Content = panel,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        StyleDialog(dialog);
        _appearanceDialogOpen = true;
        await dialog.ShowAsync();
        _appearanceDialogOpen = false;

        _preferences = _preferences with
        {
            Theme = theme.SelectedIndex switch { 1 => "night", 2 => "hybrid", _ => "day" },
            Opacity = opacity.Value,
            TextOnly = textOnly.IsOn
        };
        _preferences.Save();
        ApplyPreferences();
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
