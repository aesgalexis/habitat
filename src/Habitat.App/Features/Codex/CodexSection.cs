using Habitat.Features.Destinations;
using Habitat.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat.Features.Codex;

internal sealed class CodexSection : IDestinationSection
{
    private readonly Grid _view = new() { RowSpacing = 10 };
    private readonly TextBlock _title = new() { Text = "Codex", FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _messages = new() { Spacing = 22 };
    private readonly ScrollViewer _scroll;
    private readonly Button _stop = new() { Content = "Stop waiting", Visibility = Visibility.Collapsed };
    private readonly Func<ICodexChatService> _service;
    private readonly Action<Exception> _report;
    private readonly AsyncOperationScope _operations = new();
    private readonly DispatcherQueueTimer _timer;
    private readonly CodexChatSession _session = new();
    private TaskCompletionSource? _refresh;
    private bool _active, _disposed, _canStartNew;
    private int _sendRevision;
    private string _rendered = "";
    public ComposerDestination Destination => ComposerDestination.Codex;
    public FrameworkElement View => _view;
    public bool IsSubmitting { get; private set; }
    public bool IsBusy => IsSubmitting || _session.IsWaiting || _session.Current?.IsRunning is true;
    public bool HasContent => HasMessages || IsBusy || _session.Issue.Length > 0;
    public bool HasMessages => _session.Messages.Count > 0;
    public event Action? StateChanged;
    internal CodexChatTarget? Target => _session.Selected;
    internal IReadOnlyList<CodexChatMessage> Messages => _session.Messages;
    internal string StatusText => _status.Text;

    public CodexSection(Func<ICodexChatService> service, Action<Exception> report)
    {
        _service = service; _report = report;
        _view.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _view.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _view.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(_title);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var open = new Button { Content = "Open in Codex" };
        var reload = new Button { Content = "Reload chat" };
        open.Click += async (_, _) => await OpenAsync();
        reload.Click += async (_, _) => await RefreshAsync();
        _stop.Click += (_, _) => { _session.StopWaiting(); UpdateTimer(); Render(); };
        actions.Children.Add(open); actions.Children.Add(reload); actions.Children.Add(_stop);
        Grid.SetColumn(actions, 1); header.Children.Add(actions); _view.Children.Add(header);
        Grid.SetRow(_status, 1); _view.Children.Add(_status);
        _scroll = new ScrollViewer { Content = _messages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 8, 12, 8) };
        Grid.SetRow(_scroll, 2); _view.Children.Add(_scroll);
        AutomationProperties.SetName(_title, "Linked Codex chat");
        AutomationProperties.SetName(_messages, "Codex conversation");
        AutomationProperties.SetHelpText(_stop, "Stop waiting in HABITAT. This does not interrupt Codex.");
        AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1.5);
        _timer.Tick += async (_, _) => await RefreshAsync();
        Render();
    }

    public void SetActive(bool active)
    {
        if (_disposed) return;
        var changed = _active != active;
        _active = active; UpdateTimer();
        if (active && changed) _ = RefreshAsync();
    }

    private void UpdateTimer()
    {
        if (!_disposed && (_active || _session.IsWaiting)) _timer.Start();
        else _timer.Stop();
    }

    internal Task RefreshAsync()
    {
        if (_disposed || IsSubmitting) return Task.CompletedTask;
        if (_refresh is not null) return _refresh.Task;
        _refresh = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = _refresh;
        _ = _operations.RunAsync(() => RefreshCoreAsync(completion));
        return _refresh?.Task ?? Task.CompletedTask;
    }

    private async Task RefreshCoreAsync(TaskCompletionSource completion)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_operations.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var pending = _session.PendingTarget;
        try
        {
            if (_disposed) return;
            if (_active)
            {
                var provider = _operations.RunAsync(() => _service().GetVisibleAsync(timeout.Token), default(CodexChatSelection)!);
                var selection = await provider.WaitAsync(timeout.Token);
                if (_disposed) return;
                _canStartNew = selection.CanStartNew;
                _session.Select(selection.Target);
                if (selection.Issue is not null || selection.Target is null) _session.SetIssue(selection.Target, selection.Issue ?? "");
                if (selection.Target is { } target && target.Id != pending?.Id)
                    await ReadHistoryAsync(target, timeout.Token);
            }
            if (pending is not null) await ReadHistoryAsync(pending, timeout.Token);
        }
        catch (OperationCanceledException) when (_operations.Token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (_disposed) return;
            _report(exception);
            if (pending is not null) _session.SetIssue(pending, "Could not read the Codex response. Retrying…");
            else
            {
                _session.Select(null); _canStartNew = false;
                _session.SetIssue(null, exception is InvalidDataException ? exception.Message : "Could not connect to Codex. Open its chat and reload.");
            }
        }
        finally
        {
            if (ReferenceEquals(_refresh, completion)) _refresh = null;
            completion.TrySetResult();
            if (!_disposed) { UpdateTimer(); Render(); }
        }
    }

    private async Task ReadHistoryAsync(CodexChatTarget target, CancellationToken token)
    {
        try
        {
            var provider = _operations.RunAsync(() => _service().ReadAsync(target, token), default(CodexChatHistory)!);
            var history = await provider.WaitAsync(token);
            if (!_disposed) _session.Observe(history);
        }
        catch (OperationCanceledException) when (_operations.Token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (_disposed) return;
            _report(exception);
            _session.SetIssue(target, "Could not read the Codex response. Retrying…");
        }
    }

    public async Task SubmitAsync(string text) => await SendAsync(text, _operations.Token);

    internal Task<CodexChatSubmission> SendAsync(string text, CancellationToken token) =>
        _operations.RunAsync(() => SendCoreAsync(text, token), new CodexChatSubmission(CodexDeliveryResult.NotSent));

    private async Task<CodexChatSubmission> SendCoreAsync(string text, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _operations.Token);
        token = linked.Token;
        if (_disposed || IsBusy || string.IsNullOrWhiteSpace(text)) return new(CodexDeliveryResult.NotSent);
        await RefreshAsync();
        token.ThrowIfCancellationRequested();
        if (_disposed || IsBusy || Target is null && !_canStartNew) return new(CodexDeliveryResult.NotSent);
        var target = Target;
        var revision = ++_sendRevision;
        IsSubmitting = true; Render();
        try
        {
            var provider = _operations.RunAsync(() => _service().SendAsync(target, text, token), new CodexChatSubmission(CodexDeliveryResult.Cancelled));
            var submission = await provider.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (_disposed || revision != _sendRevision) return new(CodexDeliveryResult.Cancelled);
            if (submission.Result == CodexDeliveryResult.Sent)
            {
                _session.Accept(submission, text);
                UpdateTimer();
            }
            else if (submission.Result != CodexDeliveryResult.Cancelled)
                _session.SetIssue(target, submission.Result == CodexDeliveryResult.Unconfirmed
                    ? "Codex submission could not be confirmed. Check Codex before sending again. Your instruction was kept."
                    : "Codex is not ready, its chat changed, or its editor has another draft. Your instruction was kept. Reload chat and try again.");
            return submission;
        }
        finally
        {
            if (revision == _sendRevision)
            {
                IsSubmitting = false;
                if (!_disposed) Render();
            }
        }
    }

    public void CancelPendingSend()
    {
        if (!IsSubmitting) return;
        _sendRevision++; IsSubmitting = false;
        if (!_disposed) Render();
    }

    internal void ReturnFocus(nint ownerWindow) => _service().ReturnFocus(ownerWindow);

    internal Task OpenAsync() => _operations.RunAsync(OpenCoreAsync);

    private async Task OpenCoreAsync()
    {
        if (_disposed) return;
        try
        {
            if (!await _service().OpenChatAsync(Target, _operations.Token) && !_disposed)
                _session.SetIssue(Target, "Could not open Codex. Try again.");
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception exception)
        {
            if (_disposed) return;
            _report(exception);
            _session.SetIssue(Target, "Could not open Codex. Try again.");
        }
        if (!_disposed) Render();
    }

    private void Render()
    {
        var messages = Messages;
        _title.Text = Target is { } target ? "Codex · " + target.Title : "Codex";
        _status.Text = IsSubmitting ? "Sending to Codex…" : _session.Issue.Length > 0 ? _session.Issue :
            _session.IsWaiting ? _session.PendingTarget?.Id == Target?.Id ? "Codex is responding…" : "Waiting for Codex in another chat…" :
            _session.Current?.IsRunning is true ? "Codex is working. Open in Codex if it needs your input." :
            Target is null ? "" : "Following a Codex chat started from HABITAT.";
        _stop.Visibility = _session.IsWaiting ? Visibility.Visible : Visibility.Collapsed;
        var rendered = System.Text.Json.JsonSerializer.Serialize(new { Id = Target?.Id, Messages = messages });
        if (rendered != _rendered)
        {
            var changedChat = !_rendered.Contains(Target?.Id ?? "\0", StringComparison.Ordinal);
            var nearBottom = changedChat || _scroll.ScrollableHeight - _scroll.VerticalOffset < 60;
            _rendered = rendered; _messages.Children.Clear();
            foreach (var message in messages)
            {
                var group = new StackPanel { Spacing = 5 };
                group.Children.Add(new TextBlock { Text = message.Author, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = 0.75 });
                group.Children.Add(new TextBlock { Text = message.Text, FontSize = 15, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
                _messages.Children.Add(group);
            }
            if (nearBottom) _scroll.DispatcherQueue.TryEnqueue(() =>
            { if (!_disposed) { _scroll.UpdateLayout(); _scroll.ChangeView(null, _scroll.ScrollableHeight, null, true); } });
        }
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _timer.Stop(); StateChanged = null;
        _ = _operations.StopAsync();
    }

    public Task StopAsync()
    {
        Dispose();
        return _operations.StopAsync();
    }
}
