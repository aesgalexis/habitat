#if HABITAT_UI_CHECKS
using Habitat.Features.Codex;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckCodexSchedulesUiAsync(string log)
    {
        var previous = _services.Codex;
        var preferences = _preferences;
        var destination = _destination;
        var original = Composer.Text;
        var fake = new ScheduleUiService();
        Task? showing = null;
        try
        {
            _services.Codex = fake;
            SelectDestination(ComposerDestination.Codex);
            await CodexChat.RefreshAsync();
            foreach (var language in new[] { "en", "es" })
            {
                _preferences = _preferences with { InterfaceLanguage = language };
                ApplyInterfaceLanguage();
                SelectDestination(ComposerDestination.Codex);
                Composer.Text = "  Continúa el trabajo ☕\r\n**sin cambiar este texto**  ";
                var literal = Composer.Text;
                await Task.Delay(80); UpdateLayout();
                var send = SendButton.TransformToVisual(this).TransformPoint(new());
                var timer = CodexScheduleButton.TransformToVisual(this).TransformPoint(new());
                if (CodexScheduleButton.Visibility != Visibility.Visible ||
                    AutomationProperties.GetName(CodexScheduleButton) != UiText.Get("CodexScheduleOpen") ||
                    SendButton.HorizontalAlignment != HorizontalAlignment.Center || Composer.VerticalAlignment != VerticalAlignment.Top ||
                    timer.X < send.X + SendButton.ActualWidth || Math.Abs(timer.Y - send.Y) > 1)
                    throw new InvalidOperationException("Scheduling must sit beside Send to Codex while preserving the common centered initial screen.");
                fake.WaitForCancellation = true;
                showing = ShowCodexScheduleAsync();
                await Task.Delay(150);
                if (_activeDialog?.Title as string != UiText.Get("CodexScheduleTitle") || !_appearanceDialogOpen)
                    throw new InvalidOperationException("The scheduling dialog must use the selected language and hold focus during loading.");
                _activeDialog!.Hide(); await showing; showing = null;
                if (Composer.Text != literal || _services.CodexSchedules.Messages.Count != 0 || !fake.CancelObserved)
                    throw new InvalidOperationException("Closing during chat discovery must cancel without queuing or erasing the original draft.");
                fake.WaitForCancellation = false;
                showing = ShowCodexScheduleAsync(); await Task.Delay(180);
                var dialog = _activeDialog ?? throw new InvalidOperationException("Schedule dialog missing.");
                var controls = Descendants(dialog).ToArray();
                var select = controls.OfType<ComboBox>().First(control => control.Header as string == UiText.Get("CodexScheduleChat"));
                var date = controls.OfType<CalendarDatePicker>().Single();
                var time = controls.OfType<TimePicker>().Single();
                if (controls.OfType<CheckBox>().Any() || (select.SelectedItem as ComboBoxItem)?.Tag is not CodexScheduleTarget { UsageLimited: true } picked || picked.Target != fake.Limited ||
                    dialog.PrimaryButtonText != UiText.Get("CodexScheduleSave") || !dialog.IsPrimaryButtonEnabled)
                    throw new InvalidOperationException("The limited destination must be prioritized while date, time and action follow the interface language.");
                await InvokeAsync(FindButton(dialog, UiText.Get("CodexScheduleAtReset")));
                var chosen = date.Date!.Value.Date + time.Time;
                if (chosen < fake.Reset.LocalDateTime || chosen > fake.Reset.LocalDateTime.AddMinutes(2))
                    throw new InvalidOperationException("Use reset time must set a local date/time after the known account reset.");
                await InvokeAsync(FindButton(dialog, UiText.Get("CodexScheduleSave")));
                await showing.WaitAsync(TimeSpan.FromSeconds(8)); showing = null;
                await Task.Delay(100);
                var saved = _services.CodexSchedules.Messages.Single();
                if (saved.Target != fake.Limited || saved.Text != literal || saved.State != CodexScheduleState.Pending || Composer.Text.Length != 0 || fake.Sends != 0)
                    throw new InvalidOperationException("Scheduling must durably preserve the exact draft and chosen chat, then clear only that draft without sending early.");
                var notice = AutomationProperties.GetName(CodexScheduleButton);
                var open = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot);
                if (_codexScheduleBadge.Visibility != Visibility.Visible || _codexScheduleCount.Text != "1" ||
                    !notice.Contains(UiText.Get("CodexSchedulePendingOne")) ||
                    !notice.Contains(UiText.Format("CodexScheduleNext", CodexScheduleLocalTime(saved.SendAt))) ||
                    ToolTipService.GetToolTip(CodexScheduleButton) as string != notice ||
                    !open.Any(popup => popup.Child is { } child && Descendants(child).OfType<TextBlock>().Any(text => text.Text == UiText.Format("CodexScheduleSaved", CodexScheduleLocalTime(saved.SendAt)))) ||
                    !Equals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot), Composer))
                    throw new InvalidOperationException($"Saving must show a localized confirmation and next-send count/tooltip while preserving editor focus: badge={_codexScheduleBadge.Visibility}, count={_codexScheduleCount.Text}, notice={notice}, tooltip={ToolTipService.GetToolTip(CodexScheduleButton)}, popups={open.Count}, confirmation={_codexScheduleConfirmationText.Text}, at={_codexScheduleConfirmedAt}, focus={Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot)?.GetType().Name}.");
                TickCodexScheduleConfirmation(DateTimeOffset.UtcNow.AddSeconds(6)); await Task.Delay(250);
                if (_codexScheduleConfirmedAt is not null) throw new InvalidOperationException("The save confirmation must dismiss automatically.");
                Composer.Text = "A newer draft remains here";
                showing = ShowCodexScheduleAsync(); await Task.Delay(150);
                dialog = _activeDialog!;
                await InvokeAsync(FindButton(dialog, UiText.Get("CodexScheduleEdit")));
                if (dialog.PrimaryButtonText != UiText.Get("CodexScheduleReschedule")) throw new InvalidOperationException("Queued messages must support explicit editing.");
                await InvokeAsync(FindButton(dialog, UiText.Get("Cancel")));
                if (_services.CodexSchedules.Messages.Single().State != CodexScheduleState.Cancelled || Composer.Text != "A newer draft remains here" || fake.Sends != 0)
                    throw new InvalidOperationException("Cancelling a queued message must preserve a newer composer draft and never send.");
                if (_codexScheduleBadge.Visibility != Visibility.Collapsed || AutomationProperties.GetName(CodexScheduleButton) != UiText.Get("CodexScheduleOpen"))
                    throw new InvalidOperationException("Cancelled messages must immediately disappear from the count and next-send tooltip.");
                dialog.Hide(); await showing; showing = null;
                _services.CodexSchedules.Remove(saved.Id);
                await CheckCodexScheduleNoticeStatesAsync(log);

                fake.Empty = true;
                Composer.Text = literal;
                showing = ShowCodexScheduleAsync(); await Task.Delay(180);
                dialog = _activeDialog!;
                controls = Descendants(dialog).ToArray();
                select = controls.OfType<ComboBox>().First(control => control.Header as string == UiText.Get("CodexScheduleChat"));
                if (select.Items.Count != 1 || (select.SelectedItem as ComboBoxItem) is not { Tag: CodexScheduleTarget { Target: null } } newChat ||
                    newChat.Content as string != UiText.Get("CodexScheduleNewChat") || !dialog.IsPrimaryButtonEnabled ||
                    !controls.OfType<TextBlock>().Any(item => item.Text == UiText.Get("CodexScheduleNoChats")))
                    throw new InvalidOperationException("A first message must offer a localized new-chat destination and allow scheduling with no existing HABITAT chats.");
                await InvokeAsync(FindButton(dialog, UiText.Get("CodexScheduleSave")));
                await showing.WaitAsync(TimeSpan.FromSeconds(8)); showing = null;
                saved = _services.CodexSchedules.Messages.Single();
                if (saved is not { Target: null, StartNewChat: true } || saved.Text != literal || fake.Sends != 0 || Composer.Text.Length != 0)
                    throw new InvalidOperationException("The first scheduled message must persist an explicit new-chat request and exact text without sending early.");
                showing = ShowCodexScheduleAsync(); await Task.Delay(150); dialog = _activeDialog!;
                if (((StackPanel)((ScrollViewer)dialog.Content).Content).Children.First() is not TextBlock { Text: var heading } || heading != UiText.Get("CodexScheduleQueued"))
                    throw new InvalidOperationException("Opening the calendar with an empty composer must show existing schedules first.");
                await InvokeAsync(FindButton(dialog, UiText.Get("CodexScheduleEdit")));
                if (!dialog.IsPrimaryButtonEnabled) throw new InvalidOperationException("Editing a scheduled new chat must restore its destination.");
                await InvokeAsync(FindButton(dialog, UiText.Get("Cancel")));
                dialog.Hide(); await showing; showing = null;
                _services.CodexSchedules.Remove(saved.Id);
                fake.FailListing = true; Composer.Text = literal;
                showing = ShowCodexScheduleAsync(); await Task.Delay(180); dialog = _activeDialog!;
                if (!dialog.IsPrimaryButtonEnabled || !Descendants(dialog).OfType<TextBlock>().Any(item => item.Text == UiText.Get("CodexScheduleLoadFailed")))
                    throw new InvalidOperationException("Failure to list existing chats must still allow explicitly scheduling a new chat, with a localized explanation.");
                dialog.Hide(); await showing; showing = null;
                if (Composer.Text != literal || _services.CodexSchedules.Messages.Count != 0 || fake.Sends != 0)
                    throw new InvalidOperationException("Closing the new-chat fallback must leave the draft and outbox unchanged.");
                fake.FailListing = false; fake.Empty = false;
                SelectDestination(ComposerDestination.OpenAI); UpdateLayout();
                if (CodexScheduleButton.Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("The scheduler icon belongs only to Codex.");
                File.AppendAllText(log, $"PASS: Codex scheduling in {language}: adjacent icon, centered initial layout, loading cancellation, limited-chat priority, reset-time suggestion, persistence, draft preservation, editing, cancellation, first message, listing fallback, count, next-send tooltip, transient confirmation and editor focus; no real messages.\n");
            }
        }
        finally
        {
            _codexScheduleConfirmation.Hide();
            _activeDialog?.Hide(); if (showing is not null) await showing;
            _services.Codex = previous; _preferences = preferences; ApplyInterfaceLanguage();
            SelectDestination(destination); Composer.Text = original;
        }
    }

    private async Task CheckCodexScheduleNoticeStatesAsync(string log)
    {
        var now = DateTimeOffset.UtcNow;
        var pending = _services.CodexSchedules.Add(null, "Pending synthetic message", now.AddMinutes(20), now);
        var failed = _services.CodexSchedules.Add(null, "Uncertain synthetic message", now.AddMinutes(1), now);
        await _services.CodexSchedules.RunDueAsync(now.AddMinutes(1), (_, _) => Task.FromResult(new CodexScheduledAttempt(CodexDeliveryResult.Unconfirmed)), default);
        // Use a fresh queue to verify the badge can be rebuilt after restart.
        _codexScheduleSnapshot = await Task.Run(() => new CodexScheduleQueue(_services.Paths.CodexSchedulesPath).Messages);
        RenderCodexScheduleNotice();
        var notice = AutomationProperties.GetName(CodexScheduleButton);
        if (_codexScheduleCount.Text != "1" || _codexScheduleBadge.Visibility != Visibility.Visible ||
            _codexScheduleWarning.Visibility != Visibility.Visible || !notice.Contains(UiText.Get("CodexScheduleReviewOne")))
            throw new InvalidOperationException("Pending and uncertain messages must restore separate count/review indicators after restarting.");
        foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
        {
            var previous = RequestedTheme;
            RequestedTheme = theme; PaintCodexScheduleNotice();
            if (_codexScheduleWarning.Visibility != Visibility.Visible || _codexScheduleBadge.ActualWidth > CodexScheduleButton.Width)
                throw new InvalidOperationException("The count and amber warning must fit inside the existing calendar button in both themes.");
            RequestedTheme = previous;
        }
        var language = _preferences.InterfaceLanguage;
        _preferences = _preferences with { InterfaceLanguage = language == "en" ? "es" : "en" };
        ApplyInterfaceLanguage();
        if (!AutomationProperties.GetName(CodexScheduleButton).Contains(UiText.Get("CodexScheduleReviewOne")))
            throw new InvalidOperationException("A pending/review tooltip must follow interface language changes without losing its state.");
        _preferences = _preferences with { InterfaceLanguage = language }; ApplyInterfaceLanguage();
        _services.CodexSchedules.Reschedule(failed.Id, null, failed.Text, now.AddMinutes(10), now);
        _codexScheduleSnapshot = _services.CodexSchedules.Messages; RenderCodexScheduleNotice();
        if (_codexScheduleWarning.Visibility != Visibility.Collapsed || _codexScheduleCount.Text != "2" ||
            !AutomationProperties.GetName(CodexScheduleButton).Contains(CodexScheduleLocalTime(now.AddMinutes(10))))
            throw new InvalidOperationException("Rescheduling a review item must clear its warning and update the pending count and next-send time.");
        foreach (var id in new[] { pending.Id, failed.Id }) { _services.CodexSchedules.Cancel(id); _services.CodexSchedules.Remove(id); }
        _codexScheduleSnapshot = _services.CodexSchedules.Messages; _codexSchedulesFailed = true; RenderCodexScheduleNotice();
        if (_codexScheduleBadge.Visibility != Visibility.Collapsed || _codexScheduleWarning.Visibility != Visibility.Visible ||
            !AutomationProperties.GetName(CodexScheduleButton).Contains(UiText.Get("CodexScheduleStorageFailed")))
            throw new InvalidOperationException("Unreadable storage must show a warning rather than imply that the queue is empty.");
        _codexSchedulesFailed = false; PaintCodexScheduleNotice();
        var delivered = _services.CodexSchedules.Add(null, "Delivered synthetic message", now.AddMinutes(1), now);
        await _services.CodexSchedules.RunDueAsync(now.AddMinutes(1), (_, _) => Task.FromResult(new CodexScheduledAttempt(CodexDeliveryResult.Sent)), default);
        _codexScheduleSnapshot = await Task.Run(() => new CodexScheduleQueue(_services.Paths.CodexSchedulesPath).Messages);
        RenderCodexScheduleNotice();
        if (_codexScheduleBadge.Visibility != Visibility.Collapsed || _codexScheduleWarning.Visibility != Visibility.Collapsed ||
            AutomationProperties.GetName(CodexScheduleButton) != UiText.Get("CodexScheduleOpen"))
            throw new InvalidOperationException("An acknowledged message must remain free of pending counts and review warnings after restarting.");
        var original = Composer.Text;
        Composer.Text = "";
        var showing = ShowCodexScheduleAsync();
        try
        {
            await Task.Delay(180);
            var dialog = _activeDialog!;
            var history = FindButton(dialog, UiText.Get("CodexScheduleHistory"));
            var finished = (StackPanel)((Panel)history.Parent).Children[((Panel)history.Parent).Children.IndexOf(history) + 1];
            if (finished.Visibility != Visibility.Collapsed || !Descendants(dialog).OfType<TextBlock>().Any(item => item.Text == UiText.Get("CodexScheduleEmpty")))
                throw new InvalidOperationException("Sent messages belong in collapsed history, with an empty active queue and no review request.");
            await InvokeAsync(history);
            if (finished.Visibility != Visibility.Visible || history.Content as string != UiText.Get("CodexScheduleHideHistory"))
                throw new InvalidOperationException("Completed-message history must be explicitly expandable without reopening an active request.");
        }
        finally { _activeDialog?.Hide(); await showing; Composer.Text = original; _services.CodexSchedules.Remove(delivered.Id); }
        File.AppendAllText(log, $"PASS: Codex schedule indicators in {UiText.Language}: restart recovery, distinct pending/review states, both themes, language changes, rescheduling and storage warnings; isolated data only.\n");
    }

    private sealed class ScheduleUiService : ICodexChatService
    {
        public CodexChatTarget Limited = new("22222222-2222-2222-2222-222222222222", "Keep original title — límite");
        public DateTimeOffset Reset = DateTimeOffset.Now.AddHours(1);
        public bool WaitForCancellation, CancelObserved, Empty, FailListing;
        public int Sends;
        public async Task<IReadOnlyList<CodexScheduleTarget>> ListScheduleTargetsAsync(CancellationToken token)
        {
            if (WaitForCancellation)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { CancelObserved = true; throw; }
            }
            if (FailListing) throw new IOException("Simulated unavailable chat list");
            return Empty ? [] : [new(new("11111111-1111-1111-1111-111111111111", "Regular chat"), false), new(Limited, true, Reset)];
        }
        public Task<CodexChatSelection> GetVisibleAsync(CancellationToken token) => Task.FromResult(new CodexChatSelection(null, true));
        public Task<CodexChatHistory> ReadAsync(CodexChatTarget target, CancellationToken token) => Task.FromResult(new CodexChatHistory(target, []));
        public Task<CodexChatSubmission> SendAsync(CodexChatTarget? expected, string text, CancellationToken token) { Sends++; return Task.FromResult(new CodexChatSubmission(CodexDeliveryResult.NotSent)); }
        public Task<bool> OpenChatAsync(CodexChatTarget? target, CancellationToken token) => Task.FromResult(false);
        public void ReturnFocus(nint ownerWindow) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
#endif
