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
    private readonly Border _conversationCard = new();
    private readonly Border _composerCard = new();
    private readonly Border _statusPill = new();
    private readonly TextBlock StatusText = new();
    private readonly Ellipse StatusDot = new();
    private readonly ScrollViewer ConversationScroll = new();
    private readonly StackPanel Conversation = new();
    private readonly TextBox Composer = new();
    private readonly Button SendButton = new();
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
        _root.Padding = new Thickness(28, 18, 28, 26);
        _root.RowSpacing = 14;
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition());
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "HABITAT",
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        StatusDot.Width = 7;
        StatusDot.Height = 7;
        StatusText.Text = "Iniciando puente…";
        StatusText.FontSize = 12;
        StatusText.VerticalAlignment = VerticalAlignment.Center;
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        statusRow.Children.Add(StatusDot);
        statusRow.Children.Add(StatusText);
        _statusPill.Padding = new Thickness(12, 7, 12, 7);
        _statusPill.CornerRadius = new CornerRadius(14);
        _statusPill.Child = statusRow;
        Grid.SetColumn(_statusPill, 1);
        header.Children.Add(_statusPill);

        var overflow = new Button
        {
            Content = "•••",
            Width = 38,
            Height = 38,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10)
        };
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
        overflow.Flyout = menu;
        Grid.SetColumn(overflow, 2);
        header.Children.Add(overflow);
        _root.Children.Add(header);

        Conversation.Spacing = 16;
        ConversationScroll.Content = Conversation;
        ConversationScroll.Padding = new Thickness(22, 20, 22, 20);
        ConversationScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        ConfigureCard(_conversationCard, ConversationScroll);
        Grid.SetRow(_conversationCard, 1);
        _root.Children.Add(_conversationCard);

        var composerGrid = new Grid { ColumnSpacing = 12 };
        composerGrid.ColumnDefinitions.Add(new ColumnDefinition());
        composerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Composer.MinHeight = 74;
        Composer.MaxHeight = 180;
        Composer.Padding = new Thickness(10, 8, 10, 8);
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
        SendButton.Padding = new Thickness(20, 10, 20, 10);
        SendButton.CornerRadius = new CornerRadius(10);
        SendButton.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        SendButton.VerticalAlignment = VerticalAlignment.Bottom;
        SendButton.Click += SendButton_Click;
        Grid.SetColumn(SendButton, 1);
        composerGrid.Children.Add(SendButton);
        ConfigureCard(_composerCard, composerGrid);
        _composerCard.Padding = new Thickness(14);
        Grid.SetRow(_composerCard, 2);
        _root.Children.Add(_composerCard);

        Content = _root;
        PaintSurface();
    }

    private static void ConfigureCard(Border card, UIElement child)
    {
        card.BorderThickness = new Thickness(1);
        card.CornerRadius = new CornerRadius(18);
        card.Child = child;
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
        var group = new StackPanel { Spacing = 5 };
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
        var opacity = new Slider { Minimum = 5, Maximum = 100, Value = _preferences.Opacity, StepFrequency = 1 };
        opacity.ValueChanged += (_, args) =>
        {
            opacityValue.Text = $"{args.NewValue:0}%";
            ((App)Application.Current).MainWindow?.SetWindowOpacity(args.NewValue);
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
        PaintSurface();
        ((App)Application.Current).MainWindow?.SetWindowOpacity(_preferences.Opacity);
    }

    private void PaintSurface()
    {
        var dark = _preferences.Theme == "night";
        var canvas = new SolidColorBrush(dark ? Color(0xFF, 0x10, 0x16, 0x23) : Color(0xFF, 0xF2, 0xF6, 0xFB));
        var surface = new SolidColorBrush(dark ? Color(0xFF, 0x19, 0x21, 0x31) : Color(0xFF, 0xF9, 0xFC, 0xFF));
        var surfaceAlt = new SolidColorBrush(dark ? Color(0xFF, 0x22, 0x2C, 0x3D) : Color(0xFF, 0xED, 0xF2, 0xF8));
        var border = new SolidColorBrush(dark ? Color(0xFF, 0x34, 0x41, 0x58) : Color(0xFF, 0xD7, 0xE0, 0xEA));
        var muted = new SolidColorBrush(dark ? Color(0xFF, 0xA1, 0xAE, 0xC1) : Color(0xFF, 0x6D, 0x7B, 0x91));
        var accent = new SolidColorBrush(dark ? Color(0xFF, 0x89, 0x95, 0xFF) : Color(0xFF, 0x66, 0x74, 0xF4));

        _root.Background = canvas;
        _conversationCard.Background = surface;
        _conversationCard.BorderBrush = border;
        _composerCard.Background = surface;
        _composerCard.BorderBrush = border;
        _statusPill.Background = surfaceAlt;
        StatusText.Foreground = muted;
        Composer.Background = new SolidColorBrush(Color(0, 0, 0, 0));
        SendButton.Background = accent;
        SendButton.Foreground = new SolidColorBrush(Color(0xFF, 0xFF, 0xFF, 0xFF));
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
                return saved with { Opacity = Math.Clamp(saved.Opacity, 5, 100) };
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        return new Preferences("day", 98);
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
