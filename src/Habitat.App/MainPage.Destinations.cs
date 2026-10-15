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
    private const double DestinationRailWidth = 128;
    private const double DestinationRailInset = DestinationRailWidth + 18;
    private readonly StackPanel DestinationRail = new()
    {
        Width = DestinationRailWidth, Spacing = 16,
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Dictionary<ComposerDestination, Button> _destinationButtons = new();
    private ComposerDestination _destination;
    private int _destinationWheelDelta;
    private bool _destinationLaunching;
    private PointerRoutedEventArgs? _lastDestinationWheelEvent;
    private Func<Uri, Task<bool>> _launchBrowser;
    private Func<string, Task<bool>> _launchNotepad;
    private Func<string, Task<bool>> _launchCodex;
    private readonly PowerShellLauncher _powerShell;
    private Func<string, bool>? _launchTerminal;
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
            _ => "\uE8F2"
        },
        FontSize = 16
    };

    private void BuildDestinationInterface(Grid surface)
    {
        _launchTerminal = _powerShell.Run;
        _confirmTerminal = ConfirmTerminalAsync;
        _destination = ComposerDestinations.Normalize(_preferences.Destination);
        foreach (var destination in Enum.GetValues<ComposerDestination>())
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(DestinationIcon(destination));
            content.Children.Add(new TextBlock { Text = ComposerDestinations.Label(destination), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button
            {
                Content = content, Width = DestinationRailWidth, Height = 36,
                Padding = new Thickness(8, 4, 4, 4), BorderThickness = new Thickness(2, 0, 0, 0),
                CornerRadius = new CornerRadius(0), HorizontalContentAlignment = HorizontalAlignment.Left
            };
            UseStableWidgetButton(button);
            button.Click += (_, _) => { SelectDestination(destination); FocusComposer(); };
            AutomationProperties.SetName(button, ComposerDestinations.Label(destination));
            AutomationProperties.SetHelpText(button, "Select destination. Ctrl + mouse wheel cycles destinations.");
            if (destination == ComposerDestination.Codex)
                ToolTipService.SetToolTip(button, "Copy the instruction and open Codex. Paste it into your active chat.");
            _destinationButtons.Add(destination, button);
            DestinationRail.Children.Add(button);
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
        if ((control & CoreVirtualKeyStates.Down) == 0)
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
        _destination = ComposerDestinations.Normalize(destination);
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
        }
    }

    private void UpdateDestinationPresentation()
    {
        PaintDestinations();
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
            ComposerDestination.Codex => "Copy to Codex",
            ComposerDestination.OpenAI => "Send",
            _ => "Search"
        };
        UpdateSendState();
    }

    private Task LaunchDestinationAsync(ComposerDestination destination, string draft) =>
        RunOperationAsync(() => LaunchDestinationCoreAsync(destination, draft));

    private async Task LaunchDestinationCoreAsync(ComposerDestination destination, string draft)
    {
        _destinationLaunching = true;
        UpdateSendState();
        try
        {
            if (destination == ComposerDestination.Terminal &&
                !await (_confirmTerminal ?? ConfirmTerminalAsync)(draft)) return;
            var opened = destination == ComposerDestination.Codex
                ? await _launchCodex(draft)
                : destination == ComposerDestination.Terminal
                ? (_launchTerminal ?? _powerShell.Run)(draft)
                : destination == ComposerDestination.Notepad
                ? await _launchNotepad(draft)
                : ComposerDestinations.SearchUri(destination, draft, _gmailAuth?.Email) is { } uri && await _launchBrowser(uri);
            if (opened)
            {
                if (Composer.Text == draft) Composer.Text = string.Empty;
            }
            else SetStatus($"Could not open {ComposerDestinations.Label(destination)}. Try again.", StatusTone.Warning);
        }
        catch (Exception)
        {
            SetStatus($"Could not open {ComposerDestinations.Label(destination)}. Try again.", StatusTone.Warning);
        }
        finally
        {
            _destinationLaunching = false;
            UpdateSendState();
            FocusComposer();
        }
    }

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
