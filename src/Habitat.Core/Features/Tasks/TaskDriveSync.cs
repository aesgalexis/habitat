using System.Security.Cryptography;
using System.Text.Json;
using Habitat.Features.Drive;
using Habitat.Models;
using Habitat.Services;

namespace Habitat.Features.Tasks;

internal sealed class TaskDriveSync(TaskSession tasks, GoogleDriveConnection connection, string statePath)
{
    private sealed record Link(int Schema, string ClientId, string Owner, string? FileId = null, string? RootId = null,
        string? HeadId = null, byte[]? Baseline = null, string? PendingId = null);
    private static readonly byte[] Empty = TaskDriveDocument.Encode([]);
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Dictionary<Guid, TaskDriveChoice> _choices = new();
    private string? _reviewedVersion, _recoveryToken;
    internal bool Busy => _gate.CurrentCount == 0;
    internal bool NeedsAttention { get; private set; }
    internal bool NeedsRecoveryReview => _recoveryToken is not null;
    internal bool HasPendingChanges { get; private set; }
    internal IReadOnlyList<TaskDriveConflict> Conflicts { get; private set; } = [];
    internal string? Status { get; private set; }
    internal DateTimeOffset? LastSyncedAt { get; private set; }
    internal void MarkDirty() => HasPendingChanges = true;

    internal async Task SyncAsync(CancellationToken token, (Guid Id, TaskDriveChoice Choice)? resolution = null)
    {
        if (!await _gate.WaitAsync(0, token)) return;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            if (!tasks.IsLoaded && !await tasks.LoadAsync()) throw new InvalidOperationException("Reload tasks before syncing with Google Drive.");
            var snapshot = await tasks.SnapshotAsync(timeout.Token);
            _recoveryToken = snapshot.RecoveryToken;
            if (_recoveryToken is not null)
                throw new InvalidOperationException("Tasks were recovered from a backup. Review your pending and completed tasks, then choose Sync recovered tasks.");
            await connection.RunAsync(async workspace =>
            {
                await SynchronizeAsync(workspace, snapshot.Items, resolution, timeout.Token);
                return true;
            }, timeout.Token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or HttpRequestException or InvalidOperationException or
            JsonException or OperationCanceledException or TaskSyncLocalChangeException or UnauthorizedAccessException)
        {
            NeedsAttention = !IsTransient(error);
            Status = error switch
            {
                GmailSignInRequiredException => "Reconnect Google Drive to resume syncing tasks.",
                HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized } => "Reconnect Google Drive to resume syncing tasks.",
                TaskSyncLocalChangeException => error.Message,
                InvalidDataException or InvalidOperationException => error.Message,
                JsonException => "The Google Drive tasks file is invalid. Your local tasks were kept.",
                _ => "Could not sync tasks with Google Drive. Your local changes were kept. Try again."
            };
            if (error is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized })
                await connection.DisconnectAsync(token);
        }
        finally { _gate.Release(); }
    }

    internal async Task ConfirmRecoveryAsync(CancellationToken token)
    {
        if (Busy) return;
        var snapshot = await tasks.SnapshotAsync(token);
        if (_recoveryToken is null || snapshot.RecoveryToken != _recoveryToken)
            throw new InvalidOperationException("Recovered tasks changed. Review the current tasks before syncing.");
        await tasks.AcknowledgeSyncRecoveryAsync(_recoveryToken, token);
        _recoveryToken = null;
        NeedsAttention = false;
        await SyncAsync(token);
    }

    private async Task SynchronizeAsync(DriveWorkspaceContext workspace, TaskItem[] local,
        (Guid Id, TaskDriveChoice Choice)? resolution, CancellationToken token)
    {
        var drive = new DriveDocumentClient(workspace, DriveDocumentKind.Tasks);
        var link = ReadLink() ?? new(1, workspace.ClientId, workspace.Owner);
        if (link.ClientId != workspace.ClientId || link.Owner != workspace.Owner) throw new DriveIdentityException();
        var found = await drive.FindAsync(token);
        if (link.FileId is not null && link.FileId != found)
            throw new InvalidOperationException("The linked tasks file could not be found. Restore the original HABITAT Tasks.json in Google Drive.");
        if (found is null)
        {
            var id = link.PendingId ?? await drive.GenerateIdAsync(false, token);
            var rootId = link.RootId ?? await drive.GenerateIdAsync(true, token);
            link = link with { PendingId = id, RootId = rootId };
            WriteLink(link);
            await drive.CreateProjectionAsync(id, rootId, Empty, token);
            found = id;
        }
        var projection = await drive.ReadProjectionAsync(found, token);
        if (link.FileId is not null && link.RootId != projection.RootId)
            throw new InvalidOperationException("The linked tasks file points to a different history. Your local tasks were kept.");
        projection = await drive.PlaceAsync(projection, token);
        var baseline = link.Baseline is null ? [] : TaskDriveDocument.Decode(link.Baseline);
        var headId = projection.HeadId ?? link.HeadId;
        DriveRevision? head = headId is null ? null : await drive.ReadRevisionAsync(headId, projection.RootId, token)
            ?? throw new InvalidOperationException("The Google Drive tasks history is missing. Your local tasks were kept.");
        var nextId = head?.NextId ?? projection.RootId;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (await drive.ReadRevisionAsync(nextId, projection.RootId, token) is { } revision)
            {
                if (!visited.Add(revision.Id) || visited.Count > 10000)
                    throw new InvalidDataException("The Google Drive tasks history is invalid.");
                head = revision; nextId = revision.NextId;
            }
            if (head is null && !string.Equals(projection.Hash, DriveApiClient.ContentHash(Empty), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The Google Drive tasks history is missing. Your local tasks were kept.");
            var remoteBytes = head?.Content ?? Empty;
            var remote = TaskDriveDocument.Decode(remoteBytes);
            var version = Convert.ToHexString(SHA256.HashData(TaskDriveDocument.Encode(local))) +
                Convert.ToHexString(SHA256.HashData(remoteBytes)) + head?.Id;
            if (_reviewedVersion != version)
            {
                _choices.Clear(); _reviewedVersion = version;
                // A decision from an older review must never choose a side for
                // a different local or remote snapshot.
                if (resolution is not null) resolution = null;
            }
            if (resolution is { } selected && Enum.IsDefined(selected.Choice)) _choices[selected.Id] = selected.Choice;
            var merged = TaskDriveDocument.Merge(baseline, local, remote, _choices);
            if (merged.NeedsReview)
            {
                Conflicts = merged.Conflicts;
                NeedsAttention = true;
                Status = "Some tasks changed on both PCs. Choose which version to keep for each conflicting task.";
                return;
            }
            var bytes = TaskDriveDocument.Encode(merged.Items);
            if (!bytes.AsSpan().SequenceEqual(TaskDriveDocument.Encode(remote)))
            {
                var successor = await drive.GenerateIdAsync(true, token);
                if (!await drive.AppendAsync(nextId, projection.RootId, successor, bytes, token)) continue;
                head = new(nextId, successor, bytes);
            }
            else bytes = remoteBytes;
            await drive.PublishAsync(projection, head?.Id, bytes, token);
            var latest = await tasks.ApplyDriveAsync(local, TaskDriveDocument.Decode(bytes), token);
            // Only the acknowledged remote snapshot becomes the baseline.
            // Edits made while the network was pending remain unsynchronized.
            WriteLink(link with { FileId = found, RootId = projection.RootId, HeadId = head?.Id, Baseline = bytes, PendingId = null });
            HasPendingChanges = !TaskDriveDocument.Encode(latest).AsSpan().SequenceEqual(TaskDriveDocument.Encode(TaskDriveDocument.Decode(bytes)));
            NeedsAttention = false; Conflicts = []; _choices.Clear(); _reviewedVersion = null;
            LastSyncedAt = DateTimeOffset.UtcNow;
            Status = HasPendingChanges ? "Tasks synced. Newer local changes are waiting to sync." : "Tasks synced to Google Drive.";
            return;
        }
        throw new HttpRequestException("Another PC is syncing tasks. Try again in a moment.");
    }

    private Link? ReadLink()
    {
        if (!File.Exists(statePath)) return null;
        try
        {
            if (new FileInfo(statePath).Length > TaskDriveDocument.MaxBytes * 2L) throw InvalidLink();
            var link = JsonSerializer.Deserialize<Link>(File.ReadAllBytes(statePath));
            if (link is not { Schema: 1 } || string.IsNullOrWhiteSpace(link.ClientId) || string.IsNullOrWhiteSpace(link.Owner) ||
                (link.FileId is null) != (link.Baseline is null) ||
                (link.FileId is not null || link.PendingId is not null) && string.IsNullOrWhiteSpace(link.RootId)) throw InvalidLink();
            if (link.Baseline is not null) _ = TaskDriveDocument.Decode(link.Baseline);
            return link;
        }
        catch (JsonException) { throw InvalidLink(); }
    }
    private void WriteLink(Link link) => AtomicFile.Write(statePath, JsonSerializer.SerializeToUtf8Bytes(link));
    private static InvalidDataException InvalidLink() => new("The local Google Drive tasks link is invalid. Your tasks were kept.");
    internal static bool IsTransient(Exception error) => error is OperationCanceledException or TaskSyncLocalChangeException ||
        error is HttpRequestException http && (http.StatusCode is null or System.Net.HttpStatusCode.TooManyRequests || (int)http.StatusCode >= 500);
}
