using Habitat.Models;
using System.Security.Cryptography;
using System.Text.Json;

namespace Habitat.Services;

internal sealed class TaskStore
{
    private readonly string _path;
    private readonly bool _isolated;
    private readonly BoundedDiagnosticLog _log;
    private readonly RevisionHistory _history;
    private bool _loaded;
    private string? _fingerprint;
    private HashSet<Guid> _knownIds = [];
    private List<TaskItem> _historyItems = [];
    private List<string> _historyRevisions = [];

    internal TaskStore(string? path = null)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var persistent = path is null ? AppDataPaths.DesktopLocalDirectory() : local;
        _path = Path.GetFullPath(path ?? Path.Combine(persistent, "HABITAT", "tasks.json"));
        _log = new(_path + ".log");
        _history = new(HistoryPath, "revision-*.json", IsRevision);
        // A packaged or sandboxed launch must not create another user list.
        // Explicit paths are reserved for isolated tests and recovery tools.
        _isolated = path is null && (AppDataPaths.HasPackageIdentity() || IsTemporaryProfile(local) ||
            !string.Equals(Path.TrimEndingDirectorySeparator(local), Path.TrimEndingDirectorySeparator(persistent), StringComparison.OrdinalIgnoreCase));
    }

    internal string StoragePath => _path;
    internal bool TemporaryProfileBlocked => _isolated;
    internal bool RecoveredFromBackup { get; private set; }
    private string HistoryPath => _path + ".history";
    private string SyncRecoveryPath => _path + ".sync-recovery";
    internal string? SyncRecoveryToken
    {
        get
        {
            try
            {
                if (new FileInfo(SyncRecoveryPath).Length != 32)
                    throw new InvalidDataException("Review recovered tasks before syncing with Google Drive.");
                var token = File.ReadAllText(SyncRecoveryPath);
                if (!Guid.TryParseExact(token, "N", out _)) throw new InvalidDataException("Review recovered tasks before syncing with Google Drive.");
                return token;
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }
    }
    internal void AcknowledgeSyncRecovery(string token)
    {
        EnsurePersistentProfile();
        using var storageLock = AcquireLock();
        if (SyncRecoveryToken != token) throw new InvalidOperationException("Tasks were recovered again. Review the current tasks before syncing.");
        File.Delete(SyncRecoveryPath);
    }

    internal static bool IsTemporaryProfile(string directory) => directory
        .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
        .Any(segment => segment.StartsWith("sandbox.", StringComparison.OrdinalIgnoreCase));

    public List<TaskItem> Load()
    {
        EnsurePersistentProfile();
        using var storageLock = AcquireLock();
        RecoveredFromBackup = false;
        _loaded = false;
        var bytes = ReadCurrent();
        var history = ReadHistory(out var revisions);
        var digest = Fingerprint(bytes);
        var rolledBack = digest is not null && revisions.Count > 0 && digest != revisions[0] && revisions.Contains(digest);
        List<TaskItem> items;
        try
        {
            items = bytes is null ? [] : Parse(bytes);
            if (bytes is null && HasHistory())
                throw new InvalidDataException("Task storage is missing, but backups exist.");
            var currentIds = items.Select(item => item.Id).ToHashSet();
            if (history.Any(task => !currentIds.Contains(task.Id)))
                throw new InvalidDataException("Task storage is missing previously saved tasks.");
            if (rolledBack)
                throw new InvalidDataException("Task storage reverted to an older saved revision.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            bytes = RestoreLatestBackup(bytes, history, rolledBack);
            items = Parse(bytes);
            RecoveredFromBackup = true;
        }
        // Keep a verified copy even before the first change in this session.
        if (bytes is not null) Archive(bytes, force: RecoveredFromBackup);
        if (bytes is not null)
        {
            CompactHistory();
            UpdateHistoryIndex(items, bytes);
        }
        Remember(items, bytes);
        LogLoad(items);
        return items;
    }

    public void Save(IReadOnlyList<TaskItem> items)
    {
        EnsurePersistentProfile();
        Validate(items);
        if (!_loaded) throw new InvalidDataException("Load tasks successfully before saving.");
        using var storageLock = AcquireLock();
        var current = ReadCurrent();
        if (Fingerprint(current) != _fingerprint)
            throw new TaskStoreConflictException("Task storage changed. Reload tasks before saving.");
        if (!_knownIds.IsSubsetOf(items.Select(item => item.Id)))
            throw new InvalidDataException("Saved tasks cannot be removed. Complete them instead.");
        // Recovery tools may have added a verified revision without replacing
        // the main file. Never publish an index that hides an unseen revision.
        ReadHistory(out _);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(items);
        // Flush both the previous and proposed versions before replacing the main
        // file. A failed write never destroys the only copy of a task list.
        if (current is not null) Archive(current);
        var prepared = Archive(bytes, force: true, pending: true)!;
        try
        {
            WriteCurrent(bytes, current is not null);
            File.Move(prepared, prepared[..^".pending".Length]);
        }
        finally { DeleteTemporary(prepared); }
        Remember(items, bytes);
        CompactHistory();
        UpdateHistoryIndex(items, bytes);
    }

    private void EnsurePersistentProfile()
    {
        if (_isolated) throw new InvalidDataException(
            "Tasks cannot be stored in a temporary or redirected profile. Open HABITAT from your Windows shortcut.");
    }

    private FileStream AcquireLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private byte[]? ReadCurrent()
    {
        try { return File.ReadAllBytes(_path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static List<TaskItem> Parse(byte[] bytes)
    {
        var items = JsonSerializer.Deserialize<List<TaskItem>>(bytes, new JsonSerializerOptions { RespectRequiredConstructorParameters = true })
            ?? throw new InvalidDataException("Invalid task storage.");
        Validate(items);
        return items;
    }

    private bool HasHistory() => Directory.Exists(HistoryPath) &&
        (Directory.EnumerateFiles(HistoryPath, "revision-*.json").Any() || _history.CatalogFiles().Any());

    private static bool IsRevision(string name) => name.StartsWith("revision-", StringComparison.Ordinal) &&
        name.EndsWith(".json", StringComparison.Ordinal) && name.Length > 79 && name[^70] == '-' &&
        name.AsSpan(name.Length - 69, 64).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    private void CompactHistory() => _history.Compact(error =>
        _log.Append($"{DateTimeOffset.Now:O} Task history compaction: {error.GetType().Name} {error.HResult:X8}", "history compaction"));

    private string? Archive(byte[] bytes, bool force = false, bool pending = false)
    {
        Directory.CreateDirectory(HistoryPath);
        var digest = Fingerprint(bytes)!;
        if (!force && (_historyRevisions.Contains(digest) ||
            Directory.EnumerateFiles(HistoryPath, $"revision-*-{digest}.json").Any())) return null;
        var destination = Path.Combine(HistoryPath, $"revision-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfffffff}-{Guid.NewGuid():N}-{digest}.json" + (pending ? ".pending" : ""));
        var temporary = destination + ".tmp";
        try
        {
            WriteDurably(temporary, bytes);
            File.Move(temporary, destination);
        }
        finally { DeleteTemporary(temporary); }
        return destination;
    }

    private List<TaskItem> ReadHistory(out List<string> revisions)
    {
        revisions = [];
        List<TaskItem> recovered = [];
        if (!Directory.Exists(HistoryPath)) return recovered;
        var catalog = TaskHistoryIndex.Catalog(_history);
        if (TaskHistoryIndex.TryRead(HistoryPath, catalog, out recovered, out revisions))
        {
            _historyItems = recovered;
            _historyRevisions = revisions;
            return recovered;
        }
        var ids = new HashSet<Guid>();
        var verifiedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, bytes) in _history.ReadNewestFirst(64 * 1024 * 1024))
        {
            try
            {
                if (verifiedNames.Contains(name)) continue;
                var fingerprint = Fingerprint(bytes)!;
                if (!Path.GetFileNameWithoutExtension(name).EndsWith("-" + fingerprint, StringComparison.Ordinal)) continue;
                var items = Parse(bytes);
                verifiedNames.Add(name);
                revisions.Add(fingerprint);
                foreach (var item in items)
                    if (ids.Add(item.Id)) recovered.Add(item);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException) { continue; }
        }
        _historyItems = recovered;
        _historyRevisions = revisions;
        if (catalog == TaskHistoryIndex.Catalog(_history)) TaskHistoryIndex.Write(HistoryPath, _history, recovered, revisions);
        return recovered;
    }

    private void UpdateHistoryIndex(IReadOnlyList<TaskItem> items, byte[] bytes)
    {
        var ids = items.Select(item => item.Id).ToHashSet();
        _historyItems = [.. items, .. _historyItems.Where(item => ids.Add(item.Id))];
        var digest = Fingerprint(bytes)!;
        _historyRevisions = [digest, .. _historyRevisions.Where(revision => revision != digest)];
        TaskHistoryIndex.Write(HistoryPath, _history, _historyItems, _historyRevisions);
    }

    private byte[] RestoreLatestBackup(byte[]? original, List<TaskItem> history, bool rolledBack)
    {
        if (history.Count == 0) throw new InvalidDataException("No valid task backup is available.");
        List<TaskItem> recovered = [];
        if (original is not null)
        {
            try { if (!rolledBack) recovered = Parse(original); }
            catch (Exception exception) when (exception is JsonException or InvalidDataException) { }
            // Preserve even a valid-but-truncated main file before restoring IDs.
            WriteDurably(Path.Combine(HistoryPath, $"damaged-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfffffff}-{Guid.NewGuid():N}.json"), original);
        }
        var ids = recovered.Select(item => item.Id).ToHashSet();
        recovered.AddRange(history.Where(item => ids.Add(item.Id)));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(recovered);
        // Persist the review barrier before replacing the damaged file. It
        // survives a restart even when the recovered main file is now healthy.
        AtomicFile.Write(SyncRecoveryPath, System.Text.Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N")));
        WriteCurrent(bytes, original is not null);
        return bytes;
    }

    private void LogLoad(IReadOnlyList<TaskItem> items)
    {
        var completed = items.Count(item => item.IsCompleted);
        var state = $"Pending={items.Count - completed} Completed={completed} Recovered={RecoveredFromBackup}";
        _log.Append($"{DateTimeOffset.Now:O} PID={Environment.ProcessId} {state}", state);
    }

    private void WriteCurrent(byte[] bytes, bool exists)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteDurably(temporary, bytes);
            if (exists) AtomicFile.Replace(temporary, _path);
            else File.Move(temporary, _path);
        }
        finally { DeleteTemporary(temporary); }
    }

    private static void WriteDurably(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private void Remember(IReadOnlyList<TaskItem> items, byte[]? bytes)
    {
        _fingerprint = Fingerprint(bytes);
        _knownIds = items.Select(item => item.Id).ToHashSet();
        _loaded = true;
    }

    private static string? Fingerprint(byte[]? bytes) => bytes is null ? null : Convert.ToHexString(SHA256.HashData(bytes));

    private static void DeleteTemporary(string temporary)
    {
        try { File.Delete(temporary); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static void Validate(IReadOnlyList<TaskItem> items)
    {
        var ids = new HashSet<Guid>();
        foreach (var item in items)
            if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) ||
                string.IsNullOrWhiteSpace(item.Text) || item.Text.Length > 1000 || item.CreatedAt == DateTimeOffset.MinValue)
                throw new InvalidDataException("Invalid task storage.");
    }
}

internal sealed class TaskStoreConflictException(string message) : IOException(message);
