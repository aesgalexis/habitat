using System.IO.Compression;
using System.Security.Cryptography;

namespace Habitat.Services;

// The caller holds the same store lock used to write revisions. Older copies
// remain readable as ordinary JSON entries, with recent files left unpacked.
internal sealed class RevisionHistory(string directory, string pattern, Func<string, bool> isRevision)
{
    private const int KeepLoose = 8;
    private const int MinimumBatch = 32;
    private const int MaximumBatch = 128;
    private const long MaximumBatchBytes = 64 * 1024 * 1024;
    private const string Prefix = "history-";

    internal IEnumerable<string> CatalogFiles() => Loose().Select(file => file.FullName)
        .Concat(Archives().Select(batch => batch.File.FullName));

    private IEnumerable<FileInfo> Loose() => new DirectoryInfo(directory).EnumerateFiles(pattern)
        .Where(file => !file.Attributes.HasFlag(FileAttributes.ReparsePoint) && isRevision(file.Name));

    private sealed record Batch(FileInfo File, string LastRevision);

    private IEnumerable<Batch> Archives()
    {
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles(Prefix + "*.zip"))
        {
            var stem = Path.GetFileNameWithoutExtension(file.Name);
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint) || stem.Length <= Prefix.Length + 33 ||
                stem[^33] != '-' || !Guid.TryParseExact(stem.AsSpan(stem.Length - 32), "N", out _)) continue;
            var last = stem[Prefix.Length..^33] + ".json";
            if (isRevision(last)) yield return new(file, last);
        }
    }

    internal byte[]? ReadPacked(string name, long maxBytes) => ReadPacked(Archives(), name, maxBytes);

    private static byte[]? ReadPacked(IEnumerable<Batch> batches, string name, long maxBytes)
    {
        foreach (var batch in batches)
        {
            if (StringComparer.Ordinal.Compare(name, batch.LastRevision) > 0) continue;
            using var archive = ZipFile.OpenRead(batch.File.FullName);
            if (archive.GetEntry(name) is { } entry) return ReadEntry(entry, maxBytes);
        }
        return null;
    }

    internal IEnumerable<(string Name, byte[] Bytes)> ReadNewestFirst(long maxBytes)
    {
        var archives = new List<ZipArchive>();
        var revisions = new List<(string Name, Func<byte[]> Read)>();
        try
        {
            foreach (var file in Loose())
                revisions.Add((file.Name, () => File.ReadAllBytes(file.FullName)));
            foreach (var batch in Archives())
            {
                var archive = ZipFile.OpenRead(batch.File.FullName);
                archives.Add(archive);
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName != entry.Name || !isRevision(entry.Name) ||
                        StringComparer.Ordinal.Compare(entry.Name, batch.LastRevision) > 0)
                        throw new InvalidDataException("A packed recovery revision has an invalid name.");
                    // Keep both candidates until the store verifies their content.
                    revisions.Add((entry.Name, () => ReadEntry(entry, maxBytes)));
                }
            }
            foreach (var (name, read) in revisions.OrderByDescending(revision => revision.Name, StringComparer.Ordinal))
                yield return (name, read());
        }
        finally
        {
            foreach (var archive in archives) archive.Dispose();
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, long maxBytes)
    {
        if (entry.Length < 0 || entry.Length > maxBytes || entry.Length > Array.MaxLength)
            throw new InvalidDataException("A packed recovery revision exceeds the size limit.");
        using var input = entry.Open();
        var bytes = new byte[(int)entry.Length];
        input.ReadExactly(bytes);
        if (input.ReadByte() != -1) throw new InvalidDataException("A packed recovery revision has an invalid size.");
        return bytes;
    }

    internal void Compact(Action<Exception> reportError)
    {
        try { CompactCore(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { reportError(error); }
    }

    private void CompactCore()
    {
        var info = new DirectoryInfo(directory);
        if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
        var loose = Loose().OrderByDescending(file => file.Name, StringComparer.Ordinal).ToArray();
        if (loose.Length < KeepLoose + MinimumBatch) return;

        var batches = Archives().ToArray();
        var pending = new List<(FileInfo File, byte[] Hash)>();
        long bytes = 0;
        foreach (var file in loose.Skip(KeepLoose).Reverse().Take(MaximumBatch))
        {
            if (file.Length > MaximumBatchBytes - bytes) break;
            var hash = Hash(file.FullName);
            if (ReadPacked(batches, file.Name, MaximumBatchBytes) is { } packed)
            {
                if (hash.AsSpan().SequenceEqual(SHA256.HashData(packed))) DeleteIfUnchanged(file, hash);
                continue;
            }
            bytes += file.Length;
            pending.Add((file, hash));
        }
        if (pending.Count == 0) return;

        var last = Path.GetFileNameWithoutExtension(pending[^1].File.Name);
        var target = Path.Combine(directory, $"{Prefix}{last}-{Guid.NewGuid():N}.zip");
        var temporary = target + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                    foreach (var (file, _) in pending)
                        archive.CreateEntryFromFile(file.FullName, file.Name, CompressionLevel.Fastest);
                stream.Flush(flushToDisk: true);
            }
            if (new FileInfo(temporary).Length >= bytes) return;
            using (var verified = ZipFile.OpenRead(temporary))
            {
                foreach (var (file, hash) in pending)
                {
                    using var entry = (verified.GetEntry(file.Name) ?? throw new InvalidDataException("A recovery revision is missing from its archive.")).Open();
                    if (!SHA256.HashData(entry).AsSpan().SequenceEqual(hash))
                        throw new InvalidDataException("The recovery archive did not preserve a revision.");
                }
            }
            File.Move(temporary, target);
            foreach (var (file, hash) in pending) DeleteIfUnchanged(file, hash);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static byte[] Hash(string path)
    {
        using var input = File.OpenRead(path);
        return SHA256.HashData(input);
    }

    private static void DeleteIfUnchanged(FileInfo file, byte[] hash)
    {
        file.Refresh();
        if (file.Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
        // Keep writes excluded until the duplicate has been removed.
        using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (SHA256.HashData(input).AsSpan().SequenceEqual(hash)) file.Delete();
    }
}
