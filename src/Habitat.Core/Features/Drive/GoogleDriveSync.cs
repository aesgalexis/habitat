using System.Text.Json;
using Habitat.Features.Contacts;
using Habitat.Features.Tasks;
using Habitat.Models;
using Habitat.Services;

namespace Habitat.Features.Drive;

internal sealed record DriveSyncSettings(int Schema = 1, bool Contacts = true, bool Tasks = false);

internal sealed class GoogleDriveSync(GoogleDriveConnection connection, ContactDriveSync contacts, TaskDriveSync tasks, string settingsPath)
{
    private readonly SemaphoreSlim _running = new(1);
    private DateTimeOffset _nextContacts, _nextTasks;
    private int _contactFailures, _taskFailures;
    private bool _invalidSettings;
    private DriveSyncSettings? _settings;
    internal GoogleDriveConnection Connection => connection;
    internal ContactDriveSync Contacts => contacts;
    internal TaskDriveSync Tasks => tasks;
    internal bool Busy => _running.CurrentCount == 0 || connection.Busy || contacts.Busy || tasks.Busy;
    internal string? Status { get; private set; }
    internal event Action? Changed;
    internal DriveSyncSettings Settings
    {
        get
        {
            if (_settings is not null) return _settings;
            if (!File.Exists(settingsPath)) return _settings = new();
            try
            {
                if (new FileInfo(settingsPath).Length > 65536) throw new InvalidDataException();
                _settings = JsonSerializer.Deserialize<DriveSyncSettings>(File.ReadAllBytes(settingsPath));
                if (_settings is not { Schema: 1 }) throw new InvalidDataException();
                return _settings;
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                _invalidSettings = true;
                Status = "Google Drive sync settings could not be loaded. Choose which data to sync. Your local data was kept.";
                return _settings = new(Contacts: false, Tasks: false);
            }
        }
    }

    internal void SetEnabled(bool syncContacts, bool syncTasks)
    {
        var previous = Settings;
        var next = new DriveSyncSettings(Contacts: syncContacts, Tasks: syncTasks);
        if (_invalidSettings && File.Exists(settingsPath))
            File.Copy(settingsPath, settingsPath + ".invalid-" + Guid.NewGuid().ToString("N"));
        AtomicFile.Write(settingsPath, JsonSerializer.SerializeToUtf8Bytes(next));
        _settings = next; _invalidSettings = false; Status = null;
        if (next.Contacts && !previous.Contacts) _nextContacts = DateTimeOffset.MinValue;
        if (next.Tasks && !previous.Tasks) { tasks.MarkDirty(); _nextTasks = DateTimeOffset.MinValue; }
        Changed?.Invoke();
    }

    internal void RequestTasksSync()
    {
        tasks.MarkDirty();
        var due = DateTimeOffset.UtcNow.AddSeconds(2);
        if (_nextTasks > due) _nextTasks = due;
    }

    internal async Task ConnectAsync(CancellationToken token, Func<Task<GmailClientConfiguration?>>? pickClient = null)
    {
        await _running.WaitAsync(token);
        var connected = false;
        try { connected = await connection.ConnectAsync(token, pickClient); Status = null; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or InvalidOperationException or HttpRequestException or JsonException)
        { Status = ReadError(error); }
        finally { _running.Release(); Changed?.Invoke(); }
        if (connected) await SyncNowAsync(token);
    }

    internal async Task DisconnectAsync(CancellationToken token)
    {
        await _running.WaitAsync(token);
        try { await connection.DisconnectAsync(token); Status = connection.Status; }
        finally { _running.Release(); Changed?.Invoke(); }
    }

    internal async Task SelectFolderAsync(string id, CancellationToken token)
    {
        await _running.WaitAsync(token);
        try { await connection.SelectFolderAsync(id, token); Status = null; }
        finally { _running.Release(); Changed?.Invoke(); }
        await SyncNowAsync(token);
    }

    internal Task SyncNowAsync(CancellationToken token) => SynchronizeAsync(DateTimeOffset.UtcNow, true, false, token);
    internal Task TickAsync(DateTimeOffset now, bool skipContacts, CancellationToken token) => SynchronizeAsync(now, false, skipContacts, token);

    internal bool IsDue(DateTimeOffset now, bool skipContacts) => !Busy && connection.Connected &&
        (Settings.Contacts && !skipContacts && !contacts.NeedsAttention && now >= _nextContacts ||
         Settings.Tasks && !tasks.NeedsAttention && now >= _nextTasks);

    private async Task SynchronizeAsync(DateTimeOffset now, bool force, bool skipContacts, CancellationToken token)
    {
        if (!connection.Connected || !await _running.WaitAsync(0, token)) return;
        var attempted = false;
        try
        {
            if (Settings.Contacts && !skipContacts && (force || !contacts.NeedsAttention && now >= _nextContacts))
            {
                attempted = true;
                var before = contacts.LastSyncedAt;
                await contacts.SyncAsync(token);
                _contactFailures = contacts.LastSyncedAt != before ? 0 : Math.Min(5, _contactFailures + 1);
                _nextContacts = DateTimeOffset.UtcNow + RetryDelay(_contactFailures);
                Changed?.Invoke();
            }
            if (Settings.Tasks && connection.Connected && (force || !tasks.NeedsAttention && now >= _nextTasks))
            {
                attempted = true;
                var before = tasks.LastSyncedAt;
                await tasks.SyncAsync(token);
                var success = tasks.LastSyncedAt != before;
                _taskFailures = success ? 0 : Math.Min(5, _taskFailures + 1);
                _nextTasks = DateTimeOffset.UtcNow + (success && tasks.HasPendingChanges ? TimeSpan.FromSeconds(2) : RetryDelay(_taskFailures));
            }
        }
        finally { _running.Release(); if (attempted) Changed?.Invoke(); }
    }
    private static TimeSpan RetryDelay(int failures) => TimeSpan.FromSeconds(Math.Min(300, 30 * Math.Pow(2, Math.Max(0, failures - 1))));
    private static string ReadError(Exception error) => error is InvalidOperationException or InvalidDataException ? error.Message :
        "Could not connect Google Drive. Your local data was kept. Try again.";
}
