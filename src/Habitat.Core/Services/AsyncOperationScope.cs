namespace Habitat.Services;

// Operations start on the caller's context (Windows consent and UI callbacks
// require it). Shutdown prevents new work, cancels and drains existing work.
internal sealed class AsyncOperationScope
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly CancellationToken _token;
    private readonly HashSet<Task> _pending = [];
    private Task? _stop;
    public AsyncOperationScope() => _token = _cancellation.Token;
    public CancellationToken Token => _token;
    public bool IsStopping { get { lock (_gate) return _stop is not null; } }

    public Task RunAsync(Func<Task> operation) => RunAsync(async () => { await operation(); return true; }, false);

    public Task<T> RunAsync<T>(Func<Task<T>> operation, T stoppedResult)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_stop is not null) return Task.FromResult(stoppedResult);
            _pending.Add(completion.Task);
        }
        _ = ExecuteAsync();
        return completion.Task;

        async Task ExecuteAsync()
        {
            try { completion.TrySetResult(await operation()); }
            catch (OperationCanceledException exception) { completion.TrySetCanceled(exception.CancellationToken); }
            catch (Exception exception) { completion.TrySetException(exception); }
            finally { lock (_gate) _pending.Remove(completion.Task); }
        }
    }

    public Task StopAsync()
    {
        lock (_gate)
        {
            if (_stop is not null) return _stop;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _stop = completion.Task;
            _ = DrainAsync(_pending.ToArray(), completion);
            return _stop;
        }
    }

    private async Task DrainAsync(Task[] pending, TaskCompletionSource completion)
    {
        try
        {
            Exception? cancellationError = null;
            try { _cancellation.Cancel(); }
            catch (Exception exception) { cancellationError = exception; }
            try { await Task.WhenAll(pending); }
            catch (Exception) { /* Each caller observes its own operation. Cleanup must still run. */ }
            if (cancellationError is null) completion.TrySetResult();
            else completion.TrySetException(cancellationError);
        }
        catch (Exception exception) { completion.TrySetException(exception); }
        finally { _cancellation.Dispose(); }
    }
}
