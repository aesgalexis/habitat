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
                var saved = _services.CodexSchedules.Messages.Single();
                if (saved.Target != fake.Limited || saved.Text != literal || saved.State != CodexScheduleState.Pending || Composer.Text.Length != 0 || fake.Sends != 0)
                    throw new InvalidOperationException("Scheduling must durably preserve the exact draft and chosen chat, then clear only that draft without sending early.");
                Composer.Text = "A newer draft remains here";
                showing = ShowCodexScheduleAsync(); await Task.Delay(150);
                dialog = _activeDialog!;
                await InvokeAsync(FindButton(dialog, UiText.Get("CodexScheduleEdit")));
                if (dialog.PrimaryButtonText != UiText.Get("CodexScheduleReschedule")) throw new InvalidOperationException("Queued messages must support explicit editing.");
                await InvokeAsync(FindButton(dialog, UiText.Get("Cancel")));
                if (_services.CodexSchedules.Messages.Single().State != CodexScheduleState.Cancelled || Composer.Text != "A newer draft remains here" || fake.Sends != 0)
                    throw new InvalidOperationException("Cancelling a queued message must preserve a newer composer draft and never send.");
                dialog.Hide(); await showing; showing = null;
                _services.CodexSchedules.Remove(saved.Id);
                SelectDestination(ComposerDestination.OpenAI); UpdateLayout();
                if (CodexScheduleButton.Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("The scheduler icon belongs only to Codex.");
                File.AppendAllText(log, $"PASS: Codex scheduling in {language}: adjacent icon, centered initial layout, loading cancellation, limited-chat priority, reset-time suggestion, persistence, draft preservation, editing and cancellation; no real messages.\n");
            }
        }
        finally
        {
            _activeDialog?.Hide(); if (showing is not null) await showing;
            _services.Codex = previous; _preferences = preferences; ApplyInterfaceLanguage();
            SelectDestination(destination); Composer.Text = original;
        }
    }

    private sealed class ScheduleUiService : ICodexChatService
    {
        public CodexChatTarget Limited = new("22222222-2222-2222-2222-222222222222", "Keep original title — límite");
        public DateTimeOffset Reset = DateTimeOffset.Now.AddHours(1);
        public bool WaitForCancellation, CancelObserved;
        public int Sends;
        public async Task<IReadOnlyList<CodexScheduleTarget>> ListScheduleTargetsAsync(CancellationToken token)
        {
            if (WaitForCancellation)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { CancelObserved = true; throw; }
            }
            return [new(new("11111111-1111-1111-1111-111111111111", "Regular chat"), false), new(Limited, true, Reset)];
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
