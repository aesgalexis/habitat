using Habitat.Features.Codex;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private static string ScheduleStateText(CodexScheduleState state) => UiText.Get(state switch
    {
        CodexScheduleState.Pending => "CodexScheduleStatePending", CodexScheduleState.Sending => "CodexScheduleStateSending",
        CodexScheduleState.Sent => "CodexScheduleStateSent", CodexScheduleState.Cancelled => "CodexScheduleStateCancelled",
        _ => "CodexScheduleStateNeedsAttention"
    });
    private readonly Button CodexScheduleButton = new()
    {
        Content = new FontIcon { Glyph = "\uE787", FontSize = 17 }, Width = 36, Height = 38,
        MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), BorderThickness = new Thickness(0),
        Visibility = Visibility.Collapsed
    };
    private bool _checkingCodexSchedules, _codexSchedulesFailed;
    private DateTimeOffset _nextCodexScheduleTick;

    private void BuildCodexScheduling()
    {
        UseStableWidgetButton(CodexScheduleButton);
        BuildCodexScheduleNotice();
        CodexScheduleButton.Click += async (_, _) => await RunOperationAsync(ShowCodexScheduleAsync);
        _root.Children.Add(CodexScheduleButton);
    }

    private void PositionCodexScheduleButton(bool conversation)
    {
        var visible = _destination == ComposerDestination.Codex;
        CodexScheduleButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        CodexScheduleButton.VerticalAlignment = SendButton.VerticalAlignment;
        CodexScheduleButton.HorizontalAlignment = conversation ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        CodexScheduleButton.Margin = conversation ? new Thickness(0, 0, 0, SendButton.Margin.Bottom) :
            new Thickness((SendButton.ActualWidth > 0 ? SendButton.ActualWidth : 128) + 52, SendButton.Margin.Top, 0, 0);
    }

    private void TickCodexSchedules(DateTimeOffset now)
    {
        TickCodexScheduleConfirmation(now);
        if (_services.Paths.IsIsolated || _operations.IsStopping || _checkingCodexSchedules || _codexSchedulesFailed ||
            HasAppearanceOverlay || _activeDialog is not null || _openingUpdate || now < _nextCodexScheduleTick ||
            _codexSendCancellation is not null || _composer.IsExclusiveBusy || CodexChat.IsSubmitting) return;
        _nextCodexScheduleTick = now.AddSeconds(2);
        _checkingCodexSchedules = true;
        _ = RunOperationAsync(async () =>
        {
            try
            {
                _codexScheduleSnapshot = await Task.Run(() => _services.CodexSchedules.Messages, _operations.Token);
                RenderCodexScheduleNotice();
                var due = _codexScheduleSnapshot.Any(item => item.State == CodexScheduleState.Pending && (item.RetryAt ?? item.SendAt) <= now);
                if (!due || _operations.IsStopping) return;
                await _services.CodexSchedules.RunDueAsync(now, async (message, token) =>
                {
                    UpdateSendState();
                    _codexScheduleSnapshot = await Task.Run(() => _services.CodexSchedules.Messages, token);
                    RenderCodexScheduleNotice();
                    if (_codexSendCancellation is not null || _composer.IsExclusiveBusy || CodexChat.IsSubmitting ||
                        HasAppearanceOverlay || _activeDialog is not null || _openingUpdate)
                        return new(CodexDeliveryResult.NotSent, DateTimeOffset.UtcNow.AddSeconds(30));
                    return await _services.Codex.SendScheduledAsync(message.Target, message.Text, token);
                }, _operations.Token);
                _codexScheduleSnapshot = await Task.Run(() => _services.CodexSchedules.Messages, _operations.Token);
                RenderCodexScheduleNotice();
                if (!_operations.IsStopping && _destination == ComposerDestination.Codex) await CodexChat.RefreshAsync();
            }
            catch (Exception exception)
            {
                _codexSchedulesFailed = true;
                _services.ReportError("Codex scheduled outbox", exception);
                RenderCodexScheduleNotice();
            }
            finally { _checkingCodexSchedules = false; UpdateSendState(); }
        });
    }

    private async Task ShowCodexScheduleAsync()
    {
        if (_operations.IsStopping || HasAppearanceOverlay || _activeDialog is not null || _services.CodexSchedules.IsSending) return;
        _codexScheduleConfirmation.Hide();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_operations.Token);
        var original = Composer.Text;
        var submission = _composerDrafts.Capture(_destination);
        var message = new TextBox { Header = UiText.Get("CodexScheduleMessage"), AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, MinHeight = 80, MaxHeight = 160, MaxLength = 200000, Text = original };
        var target = new ComboBox { Header = UiText.Get("CodexScheduleChat"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var date = new CalendarDatePicker { Header = UiText.Get("CodexScheduleDate"), Date = DateTimeOffset.Now.AddMinutes(5),
            MinDate = DateTimeOffset.Now.Date, MaxDate = DateTimeOffset.Now.AddYears(1) };
        var time = new TimePicker { Header = UiText.Get("CodexScheduleTime"), ClockIdentifier = "24HourClock",
            Time = DateTime.Now.AddMinutes(5).TimeOfDay };
        var reset = new Button { Content = UiText.Get("CodexScheduleAtReset"), IsEnabled = false };
        var status = new TextBlock { Text = UiText.Get("CodexScheduleLoading"), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        var queued = new StackPanel { Spacing = 12 };
        var queuedHeading = new TextBlock { Text = UiText.Get("CodexScheduleQueued"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var body = new StackPanel { Spacing = 12, MinWidth = 320, MaxWidth = 520 };
        body.Children.Add(new TextBlock { Text = UiText.Get("CodexScheduleRequirements"), TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
        body.Children.Add(message); body.Children.Add(target);
        body.Children.Add(date); body.Children.Add(time); body.Children.Add(reset); body.Children.Add(status);
        body.Children.Add(queuedHeading);
        body.Children.Add(queued);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = UiText.Get("CodexScheduleTitle"),
            Content = new ScrollViewer { Content = body, MaxHeight = 540, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = UiText.Get("CodexScheduleSave"), CloseButtonText = UiText.Get("Close"), IsPrimaryButtonEnabled = false };
        StyleDialog(dialog);
        dialog.Closed += (_, _) => cancel.Cancel();
        Guid? editing = null;
        var ready = false;
        var listing = false;
        var saving = false;
        DateTimeOffset? savedAt = null;
        Task<ContentDialogResult>? showing = null;
        DateTimeOffset? When()
        {
            if (date.Date is not { } day) return null;
            var local = DateTime.SpecifyKind(day.Date + time.Time, DateTimeKind.Unspecified);
            if (TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local)) return null;
            return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        }
        void Validate()
        {
            dialog.IsPrimaryButtonEnabled = ready && !listing && !saving && !string.IsNullOrWhiteSpace(message.Text) &&
                target.SelectedItem is ComboBoxItem { Tag: CodexScheduleTarget } && When() is { } when && when > DateTimeOffset.Now && when < DateTimeOffset.Now.AddYears(1);
            reset.IsEnabled = target.SelectedItem is ComboBoxItem { Tag: CodexScheduleTarget { ResumeAt: { } at } } && at > DateTimeOffset.Now;
        }
        void Choose(CodexChatTarget? chosen)
        {
            target.SelectedItem = target.Items.Cast<ComboBoxItem>().FirstOrDefault(item => ((CodexScheduleTarget)item.Tag).Target?.Id == chosen?.Id);
        }
        void RenderQueued()
        {
            _codexScheduleSnapshot = _services.CodexSchedules.Messages;
            RenderCodexScheduleNotice();
            queued.Children.Clear();
            foreach (var item in _services.CodexSchedules.Messages.OrderBy(item => item.State == CodexScheduleState.Pending ? 0 : 1).ThenBy(item => item.SendAt))
            {
                var row = new StackPanel { Spacing = 4 };
                row.Children.Add(new TextBlock { Text = item.Target?.Title ?? UiText.Get("CodexScheduleNewChat"), TextWrapping = TextWrapping.Wrap });
                row.Children.Add(new TextBlock { Text = UiText.Format("CodexScheduleSummary", item.SendAt.ToLocalTime(), ScheduleStateText(item.State)),
                    TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
                if (item.State == CodexScheduleState.Pending && item.RetryAt is { } retry)
                    row.Children.Add(new TextBlock { Text = UiText.Format("CodexScheduleRetryAt", retry.ToLocalTime()), TextWrapping = TextWrapping.Wrap });
                row.Children.Add(new TextBlock { Text = item.Text, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap });
                if (item.Issue is not null) row.Children.Add(new TextBlock { Text = UiText.Get(item.Issue), TextWrapping = TextWrapping.Wrap });
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                if (item.State is CodexScheduleState.Pending or CodexScheduleState.NeedsAttention)
                {
                    var edit = new Button { Content = UiText.Get("CodexScheduleEdit"), IsEnabled = !saving };
                    edit.Click += (_, _) =>
                    {
                        editing = item.Id; message.Text = item.Text; Choose(item.Target);
                        var at = item.SendAt > DateTimeOffset.Now ? item.SendAt.ToLocalTime() : DateTimeOffset.Now.AddMinutes(5);
                        date.Date = at; time.Time = at.TimeOfDay; dialog.PrimaryButtonText = UiText.Get("CodexScheduleReschedule"); Validate();
                    };
                    actions.Children.Add(edit);
                }
                if (item.State != CodexScheduleState.Sending)
                {
                    var remove = new Button { Content = UiText.Get(item.State == CodexScheduleState.Pending ? "Cancel" : "Remove") };
                    remove.Click += async (_, _) =>
                    {
                        remove.IsEnabled = false;
                        try
                        {
                            await Task.Run(() => item.State == CodexScheduleState.Pending ? _services.CodexSchedules.Cancel(item.Id) : _services.CodexSchedules.Remove(item.Id));
                            if (editing == item.Id) { editing = null; dialog.PrimaryButtonText = UiText.Get("CodexScheduleSave"); }
                            RenderQueued(); Validate();
                        }
                        catch (Exception exception)
                        {
                            _services.ReportError("Cancel scheduled Codex message", exception); status.Text = UiText.Get("CodexScheduleStorageFailed");
                            _codexSchedulesFailed = true; RenderCodexScheduleNotice();
                        }
                    };
                    actions.Children.Add(remove);
                }
                row.Children.Add(actions); queued.Children.Add(row);
            }
            if (queued.Children.Count == 0) queued.Children.Add(new TextBlock { Text = UiText.Get("CodexScheduleEmpty"), TextWrapping = TextWrapping.Wrap });
        }
        async Task ListAsync()
        {
            if (listing) return;
            listing = true; Validate();
            var previous = (target.SelectedItem as ComboBoxItem)?.Tag as CodexScheduleTarget;
            var hasPreferred = previous is not null || editing is not null;
            var preferred = previous?.Target ?? (editing is { } editId ? _services.CodexSchedules.Messages.SingleOrDefault(item => item.Id == editId)?.Target : null);
            try
            {
                using var loading = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
                loading.CancelAfter(TimeSpan.FromSeconds(45));
                var candidates = await _services.Codex.ListScheduleTargetsAsync(loading.Token);
                cancel.Token.ThrowIfCancellationRequested();
                target.Items.Clear();
                foreach (var candidate in candidates.Where(item => item.Target is not null).OrderByDescending(item => item.UsageLimited))
                    target.Items.Add(new ComboBoxItem { Tag = candidate, Content = candidate.UsageLimited
                        ? candidate.ResumeAt is { } at ? UiText.Format("CodexScheduleLimitedUntil", candidate.Target!.Title, at.ToLocalTime())
                            : UiText.Format("CodexScheduleLimited", candidate.Target!.Title) : candidate.Target!.Title });
                target.Items.Add(new ComboBoxItem { Tag = new CodexScheduleTarget(null, false), Content = UiText.Get("CodexScheduleNewChat") });
                if (hasPreferred) Choose(preferred);
                if (!hasPreferred && target.SelectedIndex < 0) target.SelectedIndex = 0;
                status.Text = UiText.Get(candidates.Count == 0 ? "CodexScheduleNoChats" : "CodexScheduleChooseTime");
                ready = true;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            catch (Exception exception)
            {
                _services.ReportError("List scheduled Codex chats", exception);
                target.Items.Clear();
                target.Items.Add(new ComboBoxItem { Tag = new CodexScheduleTarget(null, false), Content = UiText.Get("CodexScheduleNewChat") });
                if (!hasPreferred || preferred is null) target.SelectedIndex = 0;
                ready = true;
                status.Text = UiText.Get("CodexScheduleLoadFailed");
            }
            finally { listing = false; Validate(); }
        }
        message.TextChanged += (_, _) => Validate(); target.SelectionChanged += (_, _) => Validate();
        date.DateChanged += (_, _) => Validate(); time.TimeChanged += (_, _) => Validate();
        reset.Click += (_, _) =>
        {
            if ((target.SelectedItem as ComboBoxItem)?.Tag is CodexScheduleTarget { ResumeAt: { } at })
            { at = at.AddSeconds(10).ToLocalTime().AddMinutes(1); date.Date = at; time.Time = new TimeSpan(at.Hour, at.Minute, 0); Validate(); }
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (!dialog.IsPrimaryButtonEnabled || saving || target.SelectedItem is not ComboBoxItem { Tag: CodexScheduleTarget selected } || When() is not { } at) return;
            var deferral = args.GetDeferral(); saving = true; Validate();
            try
            {
                var text = message.Text;
                if (editing is { } id) await Task.Run(() => _services.CodexSchedules.Reschedule(id, selected.Target, text, at, DateTimeOffset.Now));
                else await Task.Run(() => _services.CodexSchedules.Add(selected.Target, text, at, DateTimeOffset.Now));
                _codexSchedulesFailed = false;
                _codexScheduleSnapshot = _services.CodexSchedules.Messages;
                RenderCodexScheduleNotice();
                if (_destination == ComposerDestination.Codex && text == original && Composer.Text == original)
                { _composerDrafts.Accept(submission); _composerDrafts.Clear(submission); RestoreComposerDraft(); }
                savedAt = at;
                args.Cancel = false;
            }
            catch (Exception exception)
            {
                _services.ReportError("Save scheduled Codex message", exception); status.Text = UiText.Get("CodexScheduleStorageFailed");
                _codexSchedulesFailed = true; RenderCodexScheduleNotice();
            }
            finally { saving = false; Validate(); deferral.Complete(); }
        };
        _appearanceDialogOpen = true;
        try
        {
            showing = ShowDialogAsync(dialog);
            await Task.Run(() => _services.CodexSchedules.Messages, cancel.Token);
            _codexSchedulesFailed = false;
            RenderQueued();
            if (_codexScheduleSnapshot.Count > 0 && string.IsNullOrWhiteSpace(original))
            {
                body.Children.Remove(queuedHeading); body.Children.Remove(queued);
                body.Children.Insert(0, queuedHeading); body.Children.Insert(1, queued);
            }
            await ListAsync();
            await showing;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _services.ReportError("Codex scheduled messages", exception); SetStatus(UiText.Get("CodexScheduleStorageFailed"), StatusTone.Warning);
            _codexSchedulesFailed = true; RenderCodexScheduleNotice();
        }
        finally
        {
            dialog.Hide();
            if (showing is not null) await showing;
            _appearanceDialogOpen = false;
            if (!_operations.IsStopping) FocusComposer();
        }
        if (savedAt is { } confirmed) ShowCodexScheduleConfirmation(confirmed);
    }
}
