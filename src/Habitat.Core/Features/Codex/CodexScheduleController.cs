using Habitat.Services;

namespace Habitat.Features.Codex;

// Periodic outbox coordination is independent of WinUI. The host supplies its
// current availability; it is checked again immediately before each delivery.
internal sealed class CodexScheduleController(CodexScheduleQueue queue,
    Func<CodexScheduledMessage, CancellationToken, Task<CodexScheduledAttempt>> send,
    Func<bool> canDeliver, Action<Exception> report, Func<Task>? refreshVisible = null,
    TimeProvider? time = null)
{
    private readonly AsyncOperationScope _operations = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private bool _polling;
    private DateTimeOffset _nextPoll;
    internal bool HasFailed { get; set; }
    internal IReadOnlyList<CodexScheduledMessage> Snapshot { get; set; } = [];
    internal event Action? Changed;

    internal Task TickAsync(DateTimeOffset now, bool enabled)
    {
        if (!enabled || HasFailed || _polling || _operations.IsStopping || now < _nextPoll) return Task.CompletedTask;
        _polling = true;
        _nextPoll = now.AddSeconds(2);
        return _operations.RunAsync(async () =>
        {
            try
            {
                await RefreshAsync(_operations.Token);
                if (!Snapshot.Any(item => item.State == CodexScheduleState.Pending && (item.RetryAt ?? item.SendAt) <= now) || _operations.IsStopping) return;
                await queue.RunDueAsync(now, async (message, token) =>
                {
                    await RefreshAsync(token);
                    if (_operations.IsStopping || !canDeliver())
                        return new(CodexDeliveryResult.NotSent, _time.GetUtcNow().AddSeconds(30));
                    return await send(message, token);
                }, _operations.Token);
                await RefreshAsync(_operations.Token);
                if (!_operations.IsStopping && refreshVisible is not null) await refreshVisible();
            }
            catch (OperationCanceledException) when (_operations.IsStopping) { }
            catch (Exception exception) { HasFailed = true; report(exception); Notify(); }
            finally { _polling = false; Notify(); }
        });
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        Snapshot = await Task.Run(() => queue.Messages, token);
        Notify();
    }
    private void Notify() { if (!_operations.IsStopping) Changed?.Invoke(); }
    internal Task StopAsync() { Changed = null; return _operations.StopAsync(); }
}
