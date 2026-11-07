using Habitat.Storage;

namespace Habitat.Services;

internal sealed class GeneratedStorageService : IAsyncDisposable
{
    private readonly AppDataPaths _paths;
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationTokenSource _automatic = new();
    private readonly SemaphoreSlim _gate = new(1);
    private readonly TaskCompletionSource _windowReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _startup = Task.CompletedTask;
    private bool _started;

    internal GeneratedStorageService(AppDataPaths paths) => _paths = paths;
    internal void WindowReady() => _windowReady.TrySetResult();

    internal void StartAfterBridgeReady()
    {
        if (_started || _paths.IsIsolated || Environment.GetEnvironmentVariable("HABITAT_STORAGE_SKIP_AUTOMATIC") == "1") return;
        _started = true;
        _startup = MaintainAfterStartupAsync();
    }

    private async Task MaintainAfterStartupAsync()
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, _automatic.Token);
        try
        {
            await _windowReady.Task.WaitAsync(linked.Token);
            await Task.Delay(TimeSpan.FromSeconds(30), linked.Token);
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var report = await RunAsync(true, linked.Token);
                if (!report.Busy) return;
                await Task.Delay(TimeSpan.FromSeconds(5), linked.Token);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception) { /* Maintenance must never prevent startup or use. */ }
    }

    internal async Task SuspendForUpdateAsync(CancellationToken token)
    {
        await _automatic.CancelAsync();
        await _startup.WaitAsync(token);
        // Drain a manual Storage action too before the updater can close the app.
        await _gate.WaitAsync(token);
        _gate.Release();
    }

    internal async Task<StorageReport> RunAsync(bool clean, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        await _gate.WaitAsync(linked.Token);
        try
        {
            return await Task.Run(() =>
            {
                if (_paths.IsIsolated || AppDataPaths.HasPackageIdentity() ||
                    _paths.LocalDirectory.Contains("\\sandbox.", StringComparison.OrdinalIgnoreCase))
                    return new StorageReport { Unavailable = true };
                var installation = ReleaseUpdateInstallation.Find(AppContext.BaseDirectory);
                if (installation is null) return new StorageReport { Unavailable = true };
                var root = installation.Kind == UpdatePackageKind.Zip ? Path.Combine(installation.Root, "artifacts") :
                    GeneratedStorage.ReadRegisteredRoot(installation.Root, Path.Combine(_paths.LocalDirectory, "generated-storage.json"));
                if (root is null) return new StorageReport { Unavailable = true };
                return GeneratedStorage.Run(root, clean, AppContext.BaseDirectory, linked.Token);
            }, linked.Token);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _startup;
        await _gate.WaitAsync();
        _gate.Release();
        _stop.Dispose();
        _automatic.Dispose();
    }
}
