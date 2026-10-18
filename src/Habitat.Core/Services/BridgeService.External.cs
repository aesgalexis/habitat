namespace Habitat.Services;

public sealed partial class BridgeService
{
    private bool _supportsExternalOpen;
    private string? _openChatId;
    private TaskCompletionSource<bool>? _openChatCompletion;

    internal bool HasBrowserConnection { get { lock (_stateLock) return _connectionOwner is not null; } }

    public async Task<bool> OpenChatAsync()
    {
        TaskCompletionSource<bool> completion;
        lock (_stateLock)
        {
            if (_connectionOwner is null || !_supportsExternalOpen || _openChatCompletion is not null) return false;
            _openChatId = Guid.NewGuid().ToString("N");
            _openChatCompletion = completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try { return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException) { return false; }
        finally
        {
            lock (_stateLock) { _openChatId = null; _openChatCompletion = null; }
        }
    }
}
