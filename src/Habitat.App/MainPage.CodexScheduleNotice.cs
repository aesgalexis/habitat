using Habitat.Features.Codex;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Habitat;

public sealed partial class MainPage
{
    private IReadOnlyList<CodexScheduledMessage> _codexScheduleSnapshot = [];
    private readonly TextBlock _codexScheduleCount = new() { FontSize = 9, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _codexScheduleBadge = new() { MinWidth = 14, Height = 14, Padding = new Thickness(3, 0, 3, 0),
        CornerRadius = new CornerRadius(7), HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Border _codexScheduleWarning = new() { Width = 14, Height = 14, CornerRadius = new CornerRadius(7),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
        Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly TextBlock _codexScheduleWarningText = new() { FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Flyout _codexScheduleConfirmation = new() { Placement = FlyoutPlacementMode.Top };
    private readonly TextBlock _codexScheduleConfirmationText = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 280 };
    private DateTimeOffset? _codexScheduleConfirmedAt;
    private DateTimeOffset _codexScheduleConfirmationUntil;

    private void BuildCodexScheduleNotice()
    {
        var content = new Grid { Width = 36, Height = 38, IsHitTestVisible = false };
        content.Children.Add(new FontIcon { Glyph = "\uE787", FontSize = 17, Margin = new Thickness(-4, 4, 4, 0) });
        _codexScheduleBadge.Child = _codexScheduleCount;
        _codexScheduleWarning.Child = _codexScheduleWarningText;
        AutomationProperties.SetAccessibilityView(content, AccessibilityView.Raw);
        content.Children.Add(_codexScheduleBadge); content.Children.Add(_codexScheduleWarning);
        CodexScheduleButton.Content = content;
        AutomationProperties.SetLiveSetting(CodexScheduleButton, AutomationLiveSetting.Polite);
        UiLanguage.SetName(CodexScheduleButton, CodexScheduleNoticeText);
        UiLanguage.SetToolTip(CodexScheduleButton, CodexScheduleNoticeText);
        _codexScheduleConfirmation.Content = _codexScheduleConfirmationText;
        AutomationProperties.SetLiveSetting(_codexScheduleConfirmationText, AutomationLiveSetting.Polite);
        _codexScheduleConfirmation.Closed += (_, _) => _codexScheduleConfirmedAt = null;
        UiLanguage.Watch(_codexScheduleConfirmationText, ("Text", () => _codexScheduleConfirmedAt is { } at
            ? UiText.Format("CodexScheduleSaved", CodexScheduleLocalTime(at)) : ""));
        PaintCodexScheduleNotice();
    }

    private static string CodexScheduleLocalTime(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        return local.Date == DateTimeOffset.Now.Date ? UiText.Format("CodexScheduleTodayAt", local) : local.ToString("g", UiText.Culture);
    }

    private string CodexScheduleNoticeText()
    {
        var parts = new List<string> { UiText.Get("CodexScheduleOpen") };
        var pending = _codexScheduleSnapshot.Where(item => item.State == CodexScheduleState.Pending).ToArray();
        if (pending.Length > 0)
        {
            parts.Add(UiText.Format(pending.Length == 1 ? "CodexSchedulePendingOne" : "CodexSchedulePendingMany", pending.Length));
            parts.Add(UiText.Format("CodexScheduleNext", CodexScheduleLocalTime(pending.Min(item => item.RetryAt ?? item.SendAt))));
        }
        if (_codexScheduleSnapshot.Any(item => item.State == CodexScheduleState.Sending)) parts.Add(UiText.Get("CodexScheduleStateSending"));
        var review = _codexScheduleSnapshot.Count(item => item.State == CodexScheduleState.NeedsAttention);
        if (review > 0) parts.Add(UiText.Format(review == 1 ? "CodexScheduleReviewOne" : "CodexScheduleReviewMany", review));
        if (_codexSchedulesFailed) parts.Add(UiText.Get("CodexScheduleStorageFailed"));
        return string.Join(" · ", parts);
    }

    private void RenderCodexScheduleNotice()
    {
        var pending = _codexScheduleSnapshot.Count(item => item.State == CodexScheduleState.Pending);
        _codexScheduleCount.Text = pending > 99 ? UiText.Get("CodexScheduleManyBadge") : pending.ToString(UiText.Culture);
        _codexScheduleBadge.Visibility = pending > 0 ? Visibility.Visible : Visibility.Collapsed;
        _codexScheduleWarning.Visibility = _codexSchedulesFailed || _codexScheduleSnapshot.Any(item => item.State == CodexScheduleState.NeedsAttention)
            ? Visibility.Visible : Visibility.Collapsed;
        _codexScheduleWarningText.Text = UiText.Get("CodexScheduleWarningBadge");
        UiLanguage.SetName(CodexScheduleButton, CodexScheduleNoticeText);
        UiLanguage.SetToolTip(CodexScheduleButton, CodexScheduleNoticeText);
        UiLanguage.SetHelpText(CodexScheduleButton, () => _codexSchedulesFailed ? UiText.Get("CodexScheduleStorageFailed") : "");
    }

    private void PaintCodexScheduleNotice()
    {
        var dark = RequestedTheme == ElementTheme.Dark;
        _codexScheduleBadge.Background = new SolidColorBrush(dark ? Color(255, 0x89, 0x95, 0xFF) : Color(255, 0x66, 0x74, 0xF4));
        _codexScheduleCount.Foreground = new SolidColorBrush(dark ? Color(255, 0x14, 0x19, 0x23) : Microsoft.UI.Colors.White);
        _codexScheduleWarning.Background = new SolidColorBrush(Color(255, 0xF5, 0xB8, 0x42));
        _codexScheduleWarningText.Foreground = new SolidColorBrush(Color(255, 0x1B, 0x24, 0x34));
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, RequestedTheme));
        _codexScheduleConfirmation.FlyoutPresenterStyle = style;
        _codexScheduleConfirmationText.Foreground = TextBrush();
        RenderCodexScheduleNotice();
    }

    private void ShowCodexScheduleConfirmation(DateTimeOffset at)
    {
        if (_operations.IsStopping || _destination != Services.ComposerDestination.Codex) return;
        _codexScheduleConfirmedAt = at;
        _codexScheduleConfirmationUntil = DateTimeOffset.UtcNow.AddSeconds(5);
        _codexScheduleConfirmationText.Text = UiLanguage.Value(_codexScheduleConfirmationText, "Text",
            () => UiText.Format("CodexScheduleSaved", CodexScheduleLocalTime(at)));
        try
        {
            _codexScheduleConfirmation.ShowAt(CodexScheduleButton, new FlyoutShowOptions
                { Placement = FlyoutPlacementMode.Top, ShowMode = FlyoutShowMode.Transient });
        }
        catch (Exception exception) { _codexScheduleConfirmedAt = null; _services.ReportError("Scheduled message confirmation", exception); }
    }

    private void TickCodexScheduleConfirmation(DateTimeOffset now)
    {
        if (_codexScheduleConfirmedAt is not null && (now >= _codexScheduleConfirmationUntil ||
            _destination != Services.ComposerDestination.Codex || HasAppearanceOverlay || _operations.IsStopping))
            _codexScheduleConfirmation.Hide();
    }
}
