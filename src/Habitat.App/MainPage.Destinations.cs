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
    private readonly Dictionary<string, Button> _destinationButtons = new();
    private readonly Dictionary<string, Button> _externalDestinationButtons = new();
    private string _destination = "";
    private DestinationCatalog _destinationCatalog = new();
    private readonly ScrollViewer _destinationRailScroll = new()
    {
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
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

    private static FontIcon DestinationIcon(string destination) => new()
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
        _destination = _destinationCatalog.Select(_preferences.DefaultDestination ?? _preferences.Destination);
        RebuildDestinationRail();
        _destinationRailScroll.Content = DestinationRail;
        _destinationRailScroll.Width = DestinationRailWidth + 12;
        surface.Children.Add(_destinationRailScroll);
        surface.SizeChanged += (_, _) => _destinationRailScroll.MaxHeight = Math.Max(36, surface.ActualHeight - 40);
        RegisterDestinationWheel(surface);
        UpdateDestinationPresentation();
    }

    private void ApplyDestinationPreferences()
    {
        var previous = _destination;
        _destinationCatalog = new(_preferences.EnabledDestinations, _preferences.WindowsApps);
        _destination = IsContactDestination(previous) ? previous : _destinationCatalog.Select(previous);
        if (previous == ComposerDestination.Codex && _destination != previous) CancelPendingCodexSend();
        ConfigureComposerRoutes();
        RebuildDestinationRail();
        RestoreComposerDraft();
        if (!IsContactDestination(_destination)) _preferences = _preferences with { Destination = _destination };
        UpdateDestinationPresentation();
    }

    private void RebuildDestinationRail()
    {
        DestinationRail.Children.Clear();
        _destinationButtons.Clear();
        _externalDestinationButtons.Clear();
        foreach (var destination in _destinationCatalog.Enabled)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(_destinationCatalog.WindowsApp(destination) is null ? DestinationIcon(destination) :
                new FontIcon { Glyph = "\uE737", FontSize = 16 });
            content.Children.Add(new TextBlock { Text = _destinationCatalog.Label(destination), FontSize = 12,
                MaxWidth = DestinationLabelWidth - 44, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button
            {
                Content = content, Width = DestinationLabelWidth, Height = 36,
                Padding = new Thickness(8, 4, 4, 4), BorderThickness = new Thickness(2, 0, 0, 0),
                CornerRadius = new CornerRadius(0), HorizontalContentAlignment = HorizontalAlignment.Left
            };
            UseStableWidgetButton(button);
            button.Click += (_, _) => { SelectDestination(destination); FocusComposer(); };
            AutomationProperties.SetName(button, _destinationCatalog.Label(destination));
            ToolTipService.SetToolTip(button, _destinationCatalog.Label(destination));
            UiLanguage.SetHelpText(button, () => UiText.Get("SelectDestinationMouseWheelOverTheSelectorCycles"));
            if (destination == ComposerDestination.Codex)
            {
                var help = UiText.Get("ContinueACodexChatStartedFromHABITATOr");
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
    }

    internal void RegisterDestinationWheel(UIElement element) => element.AddHandler(
        UIElement.PointerWheelChangedEvent, new PointerEventHandler(Destination_PointerWheelChanged), true);

    private void Destination_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_updateDialogOpen || _updateHandoff) return;
        if (ReferenceEquals(e, _lastDestinationWheelEvent) ||
            (_windowHost is { IsActive: false })) return;
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        var position = e.GetCurrentPoint(_destinationRailScroll).Position;
        var overSelector = DestinationRail.Visibility == Visibility.Visible &&
            position.X >= 0 && position.X < _destinationRailScroll.ActualWidth &&
            position.Y >= 0 && position.Y < _destinationRailScroll.ActualHeight;
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
        if (steps != 0) SelectDestination(_destinationCatalog.Cycle(_destination, -steps));
    }

    private void SelectDestination(string destination)
    {
        if (_updateDialogOpen || _updateHandoff) return;
        if (_destination == ComposerDestination.Codex && destination != _destination) CancelPendingCodexSend();
        _destination = IsContactDestination(destination) ? destination : _destinationCatalog.Select(destination);
        RestoreComposerDraft();
        if (!IsContactDestination(_destination)) _preferences = _preferences with { Destination = _destination };
        SavePreferences();
        UpdateDestinationPresentation();
        if (_destinationButtons.TryGetValue(_destination, out var button)) button.StartBringIntoView();
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
            UiLanguage.SetItemStatus(button, () => active ? UiText.Get("Selected") : UiText.Get("NotSelected"));
            if (!_externalDestinationButtons.TryGetValue(destination, out var external)) continue;
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
        UpdateContactMessagePresentation();
        PaintDestinations();
        PaintFacebook();
        PresentDestinationSection();
        Composer.PlaceholderText = string.Empty;
        ComposerHint.Text = _facebookRecipients.TryGetValue(_destination, out var recipient)
            ? "Message " + recipient.Name + "…" : _emailRecipients.TryGetValue(_destination, out var emailRecipient)
            ? "Email " + emailRecipient.Name + "…" : _destinationCatalog.Placeholder(_destination);
        AutomationProperties.SetName(Composer, ComposerHint.Text);
        UpdateWritingLayout();
        SendButton.Content = UiLanguage.Value(SendButton, "Content", () => IsFacebookDestination(_destination) ? "Send message" :
            IsEmailContactDestination(_destination) ? UiText.Get("DraftEmail") : _destination switch
        {
            ComposerDestination.Notepad => UiText.Get("OpenInNotepad"),
            ComposerDestination.Translate => UiText.Get("Translate"),
            ComposerDestination.Gmail => UiText.Get("DraftEmail"),
            ComposerDestination.Terminal => UiText.Get("RunCommandMessage"),
            ComposerDestination.Run => UiText.Get("Run"),
            ComposerDestination.VSCode => UiText.Get("RunInVSCode"),
            ComposerDestination.Codex => UiText.Get("SendToCodex"),
            ComposerDestination.OpenAI => UiText.Get("Send"),
            _ => _destinationCatalog.WindowsApp(_destination) is not null || _destination.Length == 0
                ? _destinationCatalog.ActionLabel(_destination) : UiText.Get("Search")
        });
        UpdateDestinationStatus();
        UpdateSendState();
    }

    private Task LaunchDestinationAsync(string destination, string draft, bool openExternally = false) =>
        RunOperationAsync(() => _composer.SubmitAsync(destination, draft, openExternally));

    private async Task<bool> ConfirmTerminalAsync(string command)
    {
        if (HasAppearanceOverlay) return false;
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(UiLanguage.Watch(new TextBlock { Text = UiText.Get("RunThisCommandInANewPowerShellWindow"), TextWrapping = TextWrapping.Wrap }, ("Text", () => (object?)(UiText.Get("RunThisCommandInANewPowerShellWindow")))));
        var preview = new TextBox
        {
            Text = command, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            MaxHeight = 240, FontFamily = new FontFamily("Cascadia Mono")
        };
        UiLanguage.SetName(preview, () => UiText.Get("PowerShellCommandToRun"));
        content.Children.Add(preview);
        content.Children.Add(UiLanguage.Watch(new TextBlock { Text = UiText.Format("WorkingDirectoryMessage", _powerShell.WorkingDirectory), TextWrapping = TextWrapping.Wrap }, ("Text", () => (object?)(UiText.Format("WorkingDirectoryMessage", _powerShell.WorkingDirectory)))));
        var dialog = UiLanguage.Watch(new ContentDialog
        {
            XamlRoot = XamlRoot, Title = UiText.Get("RunPowerShellCommand"), Content = content,
            PrimaryButtonText = UiText.Get("RunCommand"), CloseButtonText = UiText.Get("Cancel"), DefaultButton = ContentDialogButton.Close
        }, ("Title", () => (object?)(UiText.Get("RunPowerShellCommand"))), ("PrimaryButtonText", () => (object?)(UiText.Get("RunCommand"))), ("CloseButtonText", () => (object?)(UiText.Get("Cancel"))));
        StyleDialog(dialog);
        _terminalConfirmationOpen = true;
        try { return await ShowDialogAsync(dialog) == ContentDialogResult.Primary; }
        finally { _terminalConfirmationOpen = false; }
    }
}
