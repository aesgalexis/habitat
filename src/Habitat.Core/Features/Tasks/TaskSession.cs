using Habitat.Models;
using Habitat.Services;

namespace Habitat;

// A single serialized session keeps disk work off the UI thread. Mutations
// are evaluated after acquiring the gate, against the latest committed list.
internal sealed class TaskSession(TaskStore store)
{
    private readonly SemaphoreSlim _gate = new(1);
    public List<TaskItem> Items { get; internal set; } = [];
    public bool IsLoaded { get; internal set; }
    public bool HasAttemptedLoad { get; private set; }
    public bool RecoveredFromBackup => store.RecoveredFromBackup;
    public Exception? Error { get; private set; }

    public async Task<bool> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            Items = await Task.Run(store.Load);
            IsLoaded = true;
            Error = null;
            return true;
        }
        catch (Exception exception) when (IsStorageError(exception))
        { IsLoaded = false; Error = exception; return false; }
        finally { HasAttemptedLoad = true; _gate.Release(); }
    }

    public async Task<bool> UpdateAsync(Func<IReadOnlyList<TaskItem>, List<TaskItem>> update)
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsLoaded) return false;
            var next = update(Items);
            await Task.Run(() => store.Save(next));
            Items = next;
            Error = null;
            return true;
        }
        catch (Exception exception) when (IsStorageError(exception))
        { IsLoaded = false; Error = exception; return false; }
        finally { _gate.Release(); }
    }

    public async Task WaitForIdleAsync() { await _gate.WaitAsync(); _gate.Release(); }
    private static bool IsStorageError(Exception exception) => exception is IOException or
        InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException;
}
