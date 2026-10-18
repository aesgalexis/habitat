using Habitat.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace Habitat;

public sealed partial class MainPage
{
    private const double DestinationLabelWidth = 128;
    private const double DestinationRailWidth = DestinationLabelWidth + 40;
    private const double DestinationRailInset = DestinationRailWidth + 18;
    private readonly StackPanel DestinationRail = new()
    {
        Width = DestinationRailWidth, Spacing = 16,
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Dictionary<ComposerDestination, Button> _destinationButtons = new();
    private readonly Dictionary<ComposerDestination, Button> _externalDestinationButtons = new();
    private ComposerDestination _destination;
    private int _destinationWheelDelta;
    private PointerRoutedEventArgs? _lastDestinationWheelEvent;
    private Func<Uri, Task<bool>> _launchBrowser;
    private Func<string, Task<bool>> _launchNotepad;
    private Func<string, CancellationToken, Task<CodexDeliveryResult>> _launchCodex;
    private readonly PowerShellLauncher _powerShell;
    private Func<string, bool>? _launchTerminal;
    private Func<string, bool> _launchRun;
    private Func<string, Task<bool>>? _confirmTerminal;
    private bool _terminalConfirmationOpen;

    private static FontIcon DestinationIcon(ComposerDestination destination) => new()
    {
        Glyph = destination switch
        {
            ComposerDestination.Google => "\uE721",
            ComposerDestination.YouTube => "\uE714",
            ComposerDestination.Notepad => "\uE70B",
            ComposerDestination.Translate => "\uE8C1",
            ComposerDestination.Gmail => "\uE715",
            ComposerDestination.Terminal => "\uE756",
            ComposerDestination.GoogleMaps => "\uE707",
            ComposerDestination.Codex => "\uE943",
            ComposerDestination.VSCode => "\uE756",
            ComposerDestination.Run => "\uE768",
            _ => "\uE8F2"
        },
        FontSize = 16
    };

    private void BuildDestinationInterface(Grid surface)
    {
        _launchTerminal = _powerShell.Run;
        _confirmTerminal = ConfirmTerminalAsync;
        _destination = ComposerDestinations.Normalize(_preferences.Destination);
        foreach (var destination in ComposerDestinations.Ordered)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(DestinationIcon(destination));
            content.Children.Add(new TextBlock { Text = ComposerDestinations.Label(destination), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button
            {
                Content = content, Width = DestinationLabelWidth, Height = 36,
                Padding = new Thickness(8, 4, 4, 4), BorderThickness = new Thickness(2, 0, 0, 0),
                CornerRadius = new CornerRadius(0), HorizontalContentAlignment = HorizontalAlignment.Left
            };
            UseStableWidgetButton(button);
            button.Click += (_, _) => { SelectDestination(destination); FocusComposer(); };
            AutomationProperties.SetName(button, ComposerDestinations.Label(destination));
            AutomationProperties.SetHelpText(button, "Select destination. Mouse wheel over the selector cycles destinations. Ctrl + mouse wheel works anywhere in the window.");
            if (destination == ComposerDestination.Codex)
            {
                const string help = "Continue a Codex chat started from HABITAT or start a new chat and show its reply here.";
                ToolTipService.SetToolTip(button, help);
                AutomationProperties.SetHelpText(button, help);
            }
            _destinationButtons.Add(destination, button);
            var external = new Button
            {
                Content = new FontIcon { Glyph = "\uE8A7", FontSize = 14 },
                Width = 32, Height = 36, Padding = new Thickness(0), BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
            };
            external.Click += async (_, _) => await OpenSelectedDestinationAsync();
            _externalDestinationButtons.Add(destination, external);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DestinationLabelWidth) });
            row.Children.Add(external);
            Grid.SetColumn(button, 1);
            row.Children.Add(button);
            DestinationRail.Children.Add(row);
        }
        surface.Children.Add(DestinationRail);
        RegisterDestinationWheel(surface);
        UpdateDestinationPresentation();
    }

    internal void RegisterDestinationWheel(UIElement element) => element.AddHandler(
        UIElement.PointerWheelChangedEvent, new PointerEventHandler(Destination_PointerWheelChanged), true);

    private void Destination_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (ReferenceEquals(e, _lastDestinationWheelEvent) ||
            (_windowHost is { IsActive: false })) return;
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        var position = e.GetCurrentPoint(DestinationRail).Position;
        var overSelector = DestinationRail.Visibility == Visibility.Visible &&
            position.X >= 0 && position.X < DestinationRail.ActualWidth &&
            position.Y >= 0 && position.Y < DestinationRail.ActualHeight;
        if (!overSelector && (control & CoreVirtualKeyStates.Down) == 0)
        {
            _destinationWheelDelta = 0;
            return;
        }
        var properties = e.GetCurrentPoint(null).Properties;
        if (properties.IsHorizontalMouseWheel) return;
        e.Handled = true;
        _lastDestinationWheelEvent = e;
        _destinationWheelDelta += properties.MouseWheelDelta;
        var steps = _destinationWheelDelta / 120;
        _destinationWheelDelta %= 120;
        if (steps != 0) SelectDestination(ComposerDestinations.CycleFromWheel(_destination, steps));
    }

    private void SelectDestination(ComposerDestination destination)
    {
        if (_destination == ComposerDestination.Codex && destination != _destination) CancelPendingCodexSend();
        _destination = ComposerDestinations.Normalize(destination);
        RestoreComposerDraft();
        _preferences = _preferences with { Destination = _destination };
        SavePreferences();
        UpdateDestinationPresentation();
    }

    private void PaintDestinations()
    {
        var dark = RequestedTheme == ElementTheme.Dark;
        var text = TextBrush();
        var accent = new SolidColorBrush(dark ? Color(0xFF, 0x89, 0x95, 0xFF) : Color(0xFF, 0x66, 0x74, 0xF4));
        var transparent = new SolidColorBrush(Color(0, 0, 0, 0));
        foreach (var (destination, button) in _destinationButtons)
        {
            var active = destination == _destination;
            button.Foreground = active ? accent : text;
            button.Background = transparent;
            button.BorderBrush = active ? accent : transparent;
            button.FontWeight = active ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed" })
                button.Resources[key] = transparent;
            AutomationProperties.SetItemStatus(button, active ? "Selected" : "Not selected");
            var external = _externalDestinationButtons[destination];
            external.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            external.Foreground = accent;
            external.Resources["ButtonForegroundPointerOver"] = accent;
            external.Resources["ButtonForegroundPressed"] = accent;
            external.Background = transparent;
            external.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(dark ? Color(0x24, 255, 255, 255) : Color(0x12, 0, 0, 0));
        }
    }

    private void UpdateDestinationPresentation()
    {
        PaintDestinations();
        PresentDestinationSection();
        Composer.PlaceholderText = string.Empty;
        ComposerHint.Text = ComposerDestinations.Placeholder(_destination);
        AutomationProperties.SetName(Composer, ComposerHint.Text);
        UpdateWritingLayout();
        SendButton.Content = _destination switch
        {
            ComposerDestination.Notepad => "Open in Notepad",
            ComposerDestination.Translate => "Translate",
            ComposerDestination.Gmail => "Draft email",
            ComposerDestination.Terminal => "Run command…",
            ComposerDestination.Run => "Run",
            ComposerDestination.VSCode => "Run in VS Code",
            ComposerDestination.Codex => "Send to Codex",
            ComposerDestination.OpenAI => "Send",
            _ => "Search"
        };
        UpdateSendState();
    }

    private Task LaunchDestinationAsync(ComposerDestination destination, string draft, bool openExternally = false) =>
        RunOperationAsync(() => _composer.SubmitAsync(destination, draft, openExternally));

    private async Task<bool> ConfirmTerminalAsync(string command)
    {
        if (HasAppearanceOverlay) return false;
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "Run this command in a new PowerShell window?", TextWrapping = TextWrapping.Wrap });
        var preview = new TextBox
        {
            Text = command, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            MaxHeight = 240, FontFamily = new FontFamily("Cascadia Mono")
        };
        AutomationProperties.SetName(preview, "PowerShell command to run");
        content.Children.Add(preview);
        content.Children.Add(new TextBlock { Text = $"Working directory: {_powerShell.WorkingDirectory}", TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "Run PowerShell command", Content = content,
            PrimaryButtonText = "Run command", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
        };
        StyleDialog(dialog);
        _terminalConfirmationOpen = true;
        try { return await ShowDialogAsync(dialog) == ContentDialogResult.Primary; }
        finally { _terminalConfirmationOpen = false; }
    }
}
