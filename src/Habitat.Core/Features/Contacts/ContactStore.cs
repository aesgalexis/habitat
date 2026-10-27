using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace Habitat.Features.Contacts;

// Explicit path, cross-process exclusion, optimistic revisions and immutable backups.
// A read error never turns an existing address book into an empty writable list.
internal sealed class ContactStore(string path)
{
    private readonly string _path = Path.GetFullPath(path);
    private readonly SemaphoreSlim _gate = new(1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private const long MaxBytes = 128 * 1024 * 1024;

    internal async Task<ContactBook> LoadAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(Read, token).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private ContactBook Read()
    {
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxBytes) throw new InvalidDataException("The contacts file is too large. The original file was kept.");
            var book = JsonSerializer.Deserialize<ContactBook>(stream, Json)
                ?? throw new InvalidDataException("The contacts file is invalid. The original file was kept.");
            Validate(book);
            return book;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            bool history;
            try { history = Directory.EnumerateFiles(_path + ".history", "*.json").Any(); }
            catch (DirectoryNotFoundException) { history = false; }
            if (history)
                throw new InvalidDataException("The contacts file is missing. Backups were kept in contacts.json.history; restore the file before making changes.");
            return ContactBook.Empty;
        }
    }

    internal async Task<ContactBook> SaveAsync(ContactBook next, long expectedRevision, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                using var fileLock = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var current = Read();
                if (current.Revision != expectedRevision)
                    throw new InvalidOperationException("Contacts changed in another session. Reload contacts before trying again.");
                var saved = next with { Revision = checked(expectedRevision + 1) };
                Validate(saved);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(saved, Json);
                if (bytes.LongLength > MaxBytes) throw new InvalidDataException("The contacts file would be too large. Nothing was saved.");
                var temporary = _path + "." + ContactBook.NewId() + ".tmp";
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                    { stream.Write(bytes); stream.Flush(true); }
                    var history = _path + ".history";
                    Directory.CreateDirectory(history);
                    if (File.Exists(_path))
                    {
                        var backup = Path.Combine(history, current.Revision.ToString("D12") + ".json");
                        if (!File.Exists(backup)) File.Copy(_path, backup);
                    }
                    var recovery = Path.Combine(history, saved.Revision.ToString("D12") + ".json");
                    if (File.Exists(recovery))
                    {
                        if (!File.ReadAllBytes(recovery).AsSpan().SequenceEqual(bytes))
                            throw new InvalidDataException("A different contacts revision already exists in recovery history. Restore the latest valid revision before making changes. Your files were kept.");
                    }
                    else
                    {
                        using var backup = new FileStream(recovery, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
                        backup.Write(bytes); backup.Flush(true);
                    }
                    // After the durable write, finish the commit even if cancellation arrives.
                    File.Move(temporary, _path, true);
                    return saved;
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    internal static void Validate(ContactBook book)
    {
        static void Require([DoesNotReturnIf(false)] bool condition) { if (!condition) throw new InvalidDataException("The contacts file is invalid. The original file was kept."); }
        Require(book.Schema == 1 && book.Revision >= 0 && book.Sources is not null && book.Records is not null &&
            book.People is not null && book.Merges is not null && book.DismissedPairs is not null);
        Require(book.Sources!.All(s => s is not null && !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Label) &&
            !string.IsNullOrWhiteSpace(s.Account) && s.Kind is "google" or "google-browser" or "vcard" or "facebook" or "local"));
        Require(book.Sources.Select(s => s.Id).Distinct().Count() == book.Sources.Length);
        Require(book.Sources.Select(s => (s.Kind, s.Account.ToUpperInvariant())).Distinct().Count() == book.Sources.Length);
        var sources = book.Sources.Select(s => s.Id).ToHashSet();
        Require(book.Records!.All(r => r is not null && !string.IsNullOrWhiteSpace(r.RemoteId) && sources.Contains(r.SourceId) &&
            r.Id == ContactBook.RecordId(r.SourceId, r.RemoteId) && r.Current is not null && r.History is not null));
        Require(book.Records.Select(r => r.Id).Distinct().Count() == book.Records.Length);
        foreach (var snapshot in book.Records.SelectMany(r => r.History.Append(r.Current)))
            Require(snapshot is not null && snapshot.Raw is not null && snapshot.Values is not null && snapshot.Values.All(v =>
                v is not null && !string.IsNullOrWhiteSpace(v.Kind) && !string.IsNullOrWhiteSpace(v.Value) && v.Label is not null));
        Require(book.People!.All(p => p is not null && !string.IsNullOrWhiteSpace(p.Id) && p.RecordIds is { Length: > 0 } &&
            (p.PreferredRecordId is null || p.RecordIds.Contains(p.PreferredRecordId))));
        Require(book.People.Select(p => p.Id).Distinct().Count() == book.People.Length);
        var members = book.People.SelectMany(p => p.RecordIds).ToArray();
        Require(members.Length == book.Records.Length && members.Distinct().Count() == members.Length &&
            members.ToHashSet().SetEquals(book.Records.Select(r => r.Id)));
        var ids = book.Records.Select(r => r.Id).ToHashSet();
        Require(book.Merges!.All(m => m is not null && m.Before is { Length: 2 } && m.Before.All(p =>
            p is not null && p.RecordIds is { Length: > 0 } && p.RecordIds.All(ids.Contains))));
        Require(book.DismissedPairs!.All(p => !string.IsNullOrWhiteSpace(p)));
    }
}

internal sealed class ContactSession(ContactStore store)
{
    private readonly SemaphoreSlim _gate = new(1);
    internal ContactBook Book { get; private set; } = ContactBook.Empty;
    internal bool Loaded { get; private set; }
    internal string? Error { get; private set; }
    internal event Action? Changed;

    internal async Task LoadAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try { Book = await store.LoadAsync(token); Loaded = true; Error = null; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { Loaded = false; Error = ReadError(exception); }
        finally { _gate.Release(); Changed?.Invoke(); }
    }

    internal async Task<bool> ChangeAsync(Func<ContactBook, ContactBook> change, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (!Loaded || Error is not null) throw new InvalidOperationException("Reload contacts before making changes.");
            var next = change(Book);
            if (!ReferenceEquals(next, Book)) Book = await store.SaveAsync(next, Book.Revision, token);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { Error = ReadError(exception); return false; }
        finally { _gate.Release(); Changed?.Invoke(); }
    }

    private static string ReadError(Exception exception) => exception is InvalidOperationException or InvalidDataException
        ? exception.Message : "Could not read or save contacts. Your existing data was kept. Reload contacts to try again.";
}
