using Habitat.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Habitat.Services;

// A disposable, checksummed summary of verified immutable revisions. A changed
// catalog or damaged index falls back to the loose and packed revisions.
// Recovery never depends solely on this optimization.
internal static class TaskHistoryIndex
{
    private sealed record Snapshot(string Catalog, List<TaskItem> Items, List<string> Revisions);
    private sealed record Envelope(Snapshot Snapshot, string Checksum);

    internal static string Catalog(RevisionHistory history)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in history.CatalogFiles().Order(StringComparer.Ordinal))
        {
            var info = new FileInfo(path);
            hash.AppendData(Encoding.UTF8.GetBytes($"{info.Name}\0{info.Length}\0{info.LastWriteTimeUtc.Ticks}\n"));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static bool TryRead(string directory, string catalog, out List<TaskItem> items, out List<string> revisions)
    {
        items = []; revisions = [];
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(Path.Combine(directory, "index.json")));
            if (envelope?.Snapshot is not { } snapshot || snapshot.Catalog != catalog ||
                envelope.Checksum != Digest(snapshot) || snapshot.Items is null || snapshot.Revisions is null ||
                snapshot.Revisions.Any(revision => revision is null || revision.Length != 64 || !revision.All(char.IsAsciiHexDigit))) return false;
            TaskStore.Validate(snapshot.Items);
            items = snapshot.Items; revisions = snapshot.Revisions;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { return false; }
    }

    internal static void Write(string directory, RevisionHistory history, List<TaskItem> items, List<string> revisions)
    {
        try
        {
            var snapshot = new Snapshot(Catalog(history), items, revisions);
            AtomicFile.Write(Path.Combine(directory, "index.json"), JsonSerializer.SerializeToUtf8Bytes(new Envelope(snapshot, Digest(snapshot))));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* Optional cache only. */ }
    }
    private static string Digest(Snapshot snapshot) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot)));
}
