using Habitat.Models;
using Habitat.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Habitat.Features.Sidebar;

internal sealed class GmailSidebarPreview
{
    private static readonly TimeSpan RotationInterval = TimeSpan.FromSeconds(8);
    private readonly GmailSession _session;
    private readonly Func<Task> _restore, _refresh;
    private readonly SidebarDeadline _deadline;
    private readonly TextBlock _sender = new() { FontSize = 11, Opacity = 0.72, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _excerpt = new() { FontSize = 12, Opacity = 0.8, TextTrimming = TextTrimming.CharacterEllipsis };
    private bool _hovered, _focused, _panelOpen, _settingsOpen;
    private DateTimeOffset _nextRotation, _nextRestore, _nextRefreshAttempt;
    internal Grid Root { get; } = new() { ColumnSpacing = 4, Width = 300, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    internal Button Button { get; } = new();
    internal Button MessageButton { get; } = new()
    {
        Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center
    };
    internal TextBlock Badge { get; } = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    internal TextBlock Subject { get; } = new() { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    internal GmailMessage? Message { get; private set; }
    internal bool SettingsOpen { get => _settingsOpen; set { _settingsOpen = value; _deadline.Reschedule(); } }
    internal bool PanelOpen
    {
        get => _panelOpen;
        set { _panelOpen = value; if (value) _deadline.Reschedule(); else RestartRotation(); }
    }

    internal GmailSidebarPreview(DispatcherQueue dispatcher, GmailSession session, Func<Task> restore, Func<Task> refresh)
    {
        _session = session; _restore = restore; _refresh = refresh;
        _deadline = new(dispatcher, NextDeadline, Advance);
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var symbol = new StackPanel { Spacing = 3, Width = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        symbol.Children.Add(new FontIcon { Glyph = "\uE715", FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center });
        symbol.Children.Add(new Border { Height = 18, Child = Badge });
        Button.Content = symbol;
        Root.Children.Add(Button);
        var details = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(_sender); details.Children.Add(Subject); details.Children.Add(_excerpt);
        MessageButton.Content = details;
        Grid.SetColumn(MessageButton, 1);
        Root.Children.Add(MessageButton);
        MessageButton.GotFocus += (_, _) => { _focused = true; _deadline.Reschedule(); };
        MessageButton.LostFocus += (_, _) => { _focused = false; RestartRotation(); };
        Root.PointerEntered += (_, _) => { _hovered = true; _deadline.Reschedule(); };
        Root.PointerExited += (_, _) => { _hovered = false; RestartRotation(); };
    }

    internal void Start() => _deadline.Start();
    internal void Stop() => _deadline.Stop();

    private bool SignedIn => _session.Auth?.IsSignedIn == true;
    private DateTimeOffset RefreshDue => _session.NextRefresh > _nextRefreshAttempt ? _session.NextRefresh : _nextRefreshAttempt;
    private bool CanRotate => _session.Messages.Count > 1 && !_hovered && !_focused && !_panelOpen;
    private DateTimeOffset? NextDeadline()
    {
        if (_session.IsBusy || _settingsOpen) return null;
        if (!SignedIn) return _nextRestore;
        return CanRotate && _nextRotation < RefreshDue ? _nextRotation : RefreshDue;
    }

    private void Advance(DateTimeOffset now)
    {
        if (_session.IsBusy || _settingsOpen) return;
        if (!SignedIn)
        {
            if (now < _nextRestore) return;
            _nextRestore = now.AddSeconds(30);
            _ = _restore();
            return;
        }
        if (CanRotate && now >= _nextRotation)
        {
            _session.PreviewIndex = (_session.PreviewIndex + 1) % _session.Messages.Count;
            _nextRotation = now + RotationInterval;
            RenderMessage();
        }
        if (now >= RefreshDue)
        {
            _nextRefreshAttempt = now.AddSeconds(5);
            _ = _refresh();
        }
    }

    private void RestartRotation()
    {
        _nextRotation = DateTimeOffset.UtcNow + RotationInterval;
        _deadline.Reschedule();
    }

    internal void Render(bool expanded)
    {
        var inbox = _session.Inbox;
        Badge.Text = !SignedIn ? "" : inbox is null ? "…" : (inbox.CountIsEstimate ? "≈" : "") + inbox.UnreadCount;
        Badge.Visibility = SignedIn && expanded ? Visibility.Visible : Visibility.Collapsed;
        if (_session.ResetRotation)
        {
            _session.ResetRotation = false;
            _nextRotation = DateTimeOffset.UtcNow + RotationInterval;
        }
        RenderMessage();
        _deadline.Reschedule();
    }

    private void RenderMessage()
    {
        var messages = _session.Messages;
        _session.PreviewIndex = messages.Count == 0 ? 0 : Math.Clamp(_session.PreviewIndex, 0, messages.Count - 1);
        var message = SignedIn ? messages.ElementAtOrDefault(_session.PreviewIndex) : null;
        var error = _session.Error is not null;
        var storageUnavailable = _session.Auth is null && error;
        Message = message;
        _sender.Text = message is null ? "Gmail" : WidgetLine(message.Sender);
        Subject.Text = message is not null ? WidgetLine(message.Subject) :
            storageUnavailable ? UiText.Get("SavedAccountUnavailable") : !SignedIn ? UiText.Get("ConnectYourAccount") : error ? UiText.Get("RefreshUnavailable") :
            _session.Inbox is null ? UiText.Get("LoadingUnreadEmails") : UiText.Get("NoUnreadEmails");
        _excerpt.Text = message is not null ? WidgetLine(message.Preview) :
            storageUnavailable ? UiText.Get("ClickToRetry") : !SignedIn ? UiText.Get("ClickToSignIn") : error ? UiText.Get("ClickForDetails") :
            _session.Inbox is null ? UiText.Get("RestoringYourSavedSession") : UiText.Get("YourInboxIsUpToDate");
        UiLanguage.SetName(Button, () => UiText.Format("OpenGmailReaderInHABITATUnreadEmails", Badge.Text));
        UiLanguage.SetName(MessageButton, () => message is null ? UiText.Get("OpenGmailReaderInHABITAT") :
            UiText.Format("OpenInGmail", _sender.Text, Subject.Text));
    }

    internal void Paint(Brush text) => _sender.Foreground = Subject.Foreground = _excerpt.Foreground = text;

    private static string WidgetLine(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
