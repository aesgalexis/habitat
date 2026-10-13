using Habitat.Models;
using Habitat.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Ellipse = Microsoft.UI.Xaml.Shapes.Ellipse;
using System.Text.Json;
using Windows.System;
using Windows.UI.Core;

namespace Habitat;

public sealed class MainPage : Page
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private readonly Grid _root = new();
    private readonly TextBlock StatusText = new();
    private readonly Ellipse StatusDot = new();
    private readonly TextBlock TitleText = new();
    private readonly ScrollViewer ConversationScroll = new();
    private readonly StackPanel Conversation = new();
    private readonly TextBox Composer = new();
    private readonly Button SendButton = new();
    private readonly Button OverflowButton = new();
    private BridgeService? _bridge;
    private Preferences _preferences = Preferences.Load();

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
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TitleText.Text = "HABITAT";
        TitleText.VerticalAlignment = VerticalAlignment.Center;
        TitleText.FontFamily = new FontFamily("Segoe UI Variable Display");
        TitleText.FontSize = 20;
        TitleText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        header.Children.Add(TitleText);

        StatusDot.Width = 6;
        StatusDot.Height = 6;
        StatusText.Text = "Iniciando puente…";
        StatusText.FontSize = 12;
        StatusText.VerticalAlignment = VerticalAlignment.Center;
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        statusRow.Children.Add(StatusDot);
        statusRow.Children.Add(StatusText);
        Grid.SetColumn(statusRow, 1);
        header.Children.Add(statusRow);

        OverflowButton.Content = "•••";
        OverflowButton.Width = 34;
        OverflowButton.Height = 34;
        OverflowButton.Padding = new Thickness(0);
        OverflowButton.BorderThickness = new Thickness(0);
        var menu = new MenuFlyout();
        var preferences = new MenuFlyoutItem { Text = "Preferencias…" };
        preferences.Click += ShowPreferences_Click;
        menu.Items.Add(preferences);
        menu.Items.Add(new MenuFlyoutSeparator());
        var pairing = new MenuFlyoutItem { Text = "Código de vinculación…" };
        pairing.Click += ShowPairing_Click;
        menu.Items.Add(pairing);
        var copy = new MenuFlyoutItem { Text = "Copiar código" };
        copy.Click += CopyPairingCode_Click;
        menu.Items.Add(copy);
        OverflowButton.Flyout = menu;
        Grid.SetColumn(OverflowButton, 2);
        header.Children.Add(OverflowButton);
        _root.Children.Add(header);

        Conversation.Spacing = 22;
        ConversationScroll.Content = Conversation;
        ConversationScroll.Padding = new Thickness(2, 18, 8, 18);
        ConversationScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Grid.SetRow(ConversationScroll, 1);
        _root.Children.Add(ConversationScroll);

        var composerGrid = new Grid { ColumnSpacing = 12 };
        composerGrid.ColumnDefinitions.Add(new ColumnDefinition());
        composerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Composer.MinHeight = 58;
        Composer.MaxHeight = 180;
        Composer.Padding = new Thickness(0, 9, 0, 9);
        Composer.AcceptsReturn = true;
        Composer.TextWrapping = TextWrapping.Wrap;
        Composer.PlaceholderText = "Escribe a ChatGPT…";
        Composer.BorderThickness = new Thickness(0);
        // TextBox handles Enter internally when AcceptsReturn is enabled.
        // Listen to handled routed events as well so plain Enter always sends,
        // while Shift+Enter is left to the TextBox to insert a line break.
        Composer.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Composer_KeyDown), true);
        composerGrid.Children.Add(Composer);
        SendButton.Content = "Enviar";
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
        AddMessage("HABITAT", "Conexión automática habilitada. Mantén abierta en Chrome la conversación deseada y espera a que aparezca ‘Extensión conectada’.", MessageTone.Muted);
        _timer.Start();
        Composer.Focus(FocusState.Programmatic);
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
            return;
        }

        if (!_bridge.TryQueueMessage(text, out var reason))
        {
            SetStatus(reason, StatusTone.Warning);
            return;
        }

        AddMessage("TÚ", text, MessageTone.User);
        Composer.Text = string.Empty;
        SetStatus("Enviando a Chrome…", StatusTone.Active);
        UpdateSendState();
    }

    private void Timer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (_bridge is null)
        {
            return;
        }

        if (_bridge.TryExpireMessage(TimeSpan.FromMinutes(4)))
        {
            AddMessage("HABITAT", "Se agotó el tiempo de espera. Revisa ChatGPT antes de volver a enviar.", MessageTone.Muted);
            SetStatus("Tiempo agotado; revisa Chrome", StatusTone.Danger);
        }
        else if (!_bridge.HasMessageInFlight)
        {
            SetStatus(_bridge.IsConnected ? "Extensión conectada" : "Reconectando con ChatGPT…",
                _bridge.IsConnected ? StatusTone.Success : StatusTone.Warning);
        }

        UpdateSendState();
    }

    private void Bridge_EventReceived(object? sender, BridgeEvent bridgeEvent)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (bridgeEvent.Type)
            {
                case "received":
                    SetStatus("Chrome recibió el mensaje…", StatusTone.Active);
                    break;
                case "started":
                    SetStatus("ChatGPT está respondiendo…", StatusTone.Active);
                    break;
                case "response":
                    AddMessage("CHATGPT", bridgeEvent.Text ?? string.Empty, MessageTone.Assistant);
                    SetStatus("Respuesta recibida", StatusTone.Success);
                    break;
                case "error":
                    AddMessage("HABITAT", bridgeEvent.Text ?? "Error desconocido", MessageTone.Muted);
                    SetStatus("Error en el puente", StatusTone.Danger);
                    break;
                case "status":
                    var text = bridgeEvent.Text ?? string.Empty;
                    SetStatus(text[..Math.Min(100, text.Length)], StatusTone.Active);
                    break;
            }
            UpdateSendState();
        });
    }

    private void Bridge_StartFailed(object? sender, string error)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            AddMessage("HABITAT", error, MessageTone.Muted);
            SetStatus("No se pudo iniciar el puente", StatusTone.Danger);
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
        StatusText.Text = text;
        var dark = RequestedTheme == ElementTheme.Dark;
        StatusDot.Fill = new SolidColorBrush(tone switch
        {
            StatusTone.Success => Color(0xFF, dark ? 0x55 : 0x24, dark ? 0xC7 : 0x84, dark ? 0x9C : 0x5F),
            StatusTone.Danger => Color(0xFF, dark ? 0xED : 0xB8, dark ? 0x71 : 0x42, dark ? 0x81 : 0x58),
            StatusTone.Warning => Color(0xFF, dark ? 0xE2 : 0xA9, dark ? 0xA0 : 0x63, dark ? 0x6B : 0x31),
            _ => Color(0xFF, dark ? 0x89 : 0x66, dark ? 0x95 : 0x74, dark ? 0xFF : 0xF4)
        });
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
            ItemsSource = new[] { "Día", "Noche" },
            SelectedIndex = _preferences.Theme == "night" ? 1 : 0
        };
        theme.SelectionChanged += (_, _) =>
        {
            RequestedTheme = theme.SelectedIndex == 1 ? ElementTheme.Dark : ElementTheme.Light;
            ((App)Application.Current).MainWindow?.SetBackgroundAppearance(
                opacity.Value, RequestedTheme == ElementTheme.Dark);
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 360 };
        panel.Children.Add(new TextBlock { Text = "Apariencia", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(theme);
        var opacityHeader = new Grid();
        opacityHeader.ColumnDefinitions.Add(new ColumnDefinition());
        opacityHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        opacityHeader.Children.Add(new TextBlock { Text = "Opacidad", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        Grid.SetColumn(opacityValue, 1);
        opacityHeader.Children.Add(opacityValue);
        panel.Children.Add(opacityHeader);
        panel.Children.Add(opacity);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Preferencias",
            Content = panel,
            CloseButtonText = "Cerrar",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();

        _preferences = new Preferences(theme.SelectedIndex == 1 ? "night" : "day", opacity.Value);
        _preferences.Save();
        ApplyPreferences();
    }

    private async void ShowPairing_Click(object sender, RoutedEventArgs e)
    {
        if (_bridge is null) return;
        var code = new TextBox { Text = _bridge.PairKey, IsReadOnly = true, MinWidth = 400 };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "Sólo se necesita como recuperación manual. La extensión se reconecta automáticamente al abrir HABITAT.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 440
        });
        panel.Children.Add(code);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Código de vinculación",
            Content = panel,
            PrimaryButtonText = "Copiar código",
            CloseButtonText = "Cerrar"
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            CopyPairingCode();
        }
    }

    private void CopyPairingCode_Click(object sender, RoutedEventArgs e) => CopyPairingCode();

    private void CopyPairingCode()
    {
        if (_bridge is null) return;
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(_bridge.PairKey);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        SetStatus("Código copiado", StatusTone.Active);
    }

    private void ApplyPreferences()
    {
        RequestedTheme = _preferences.Theme == "night" ? ElementTheme.Dark : ElementTheme.Light;
        ((App)Application.Current).MainWindow?.SetBackgroundAppearance(
            _preferences.Opacity, RequestedTheme == ElementTheme.Dark);
    }

    public void SetBackgroundOpacity(double percent)
    {
        _preferences = _preferences with { Opacity = Math.Clamp(percent, 0, 100) };
        PaintSurface();
    }

    private void PaintSurface()
    {
        var dark = RequestedTheme == ElementTheme.Dark;
        var canvas = new SolidColorBrush(Color(0, 0, 0, 0));
        var transparent = new SolidColorBrush(Color(0, 0, 0, 0));
        var muted = new SolidColorBrush(dark ? Color(0xFF, 0xA1, 0xAE, 0xC1) : Color(0xFF, 0x6D, 0x7B, 0x91));
        var accent = new SolidColorBrush(dark ? Color(0xFF, 0x89, 0x95, 0xFF) : Color(0xFF, 0x66, 0x74, 0xF4));

        _root.Background = canvas;
        TitleText.Foreground = TextBrush();
        StatusText.Foreground = muted;
        Composer.Background = transparent;
        Composer.BorderBrush = transparent;
        Composer.Foreground = TextBrush();
        SendButton.Background = transparent;
        SendButton.Foreground = accent;
        OverflowButton.Background = transparent;
        OverflowButton.Foreground = muted;

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
            Composer.Resources[key] = muted;
        }

        foreach(var child in Conversation.Children)
        {
            if(child is not StackPanel group || group.Tag is not MessageTone tone) continue;
            if(group.Children.Count > 0 && group.Children[0] is TextBlock author)
            {
                author.Foreground = ToneBrush(tone);
            }
            if(group.Children.Count > 1 && group.Children[1] is TextBlock body)
            {
                body.Foreground = TextBrush();
            }
        }
    }

    private enum StatusTone { Active, Success, Warning, Danger }
    private enum MessageTone { User, Assistant, Muted }
}

internal sealed record Preferences(string Theme, double Opacity)
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HABITAT", "preferences.json");

    public static Preferences Load()
    {
        try
        {
            var saved = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath));
            if (saved is not null && saved.Theme is "day" or "night")
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
