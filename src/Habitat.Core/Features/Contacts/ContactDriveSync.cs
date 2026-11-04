using System.Text.Json;
using System.Security.Cryptography;
using Habitat.Models;
using Habitat.Services;

namespace Habitat.Features.Contacts;

internal sealed class ContactDriveSync(ContactSession contacts, string statePath, Func<GmailAuthService> createAuth,
    HttpClient http) : IDisposable
{
    private sealed record Link(string ClientId, string Owner, string? FileId = null, string? RootId = null,
        string? HeadId = null, byte[]? Baseline = null, string? PendingId = null);
    private static readonly byte[] EmptyCsv = ContactDriveCsv.Encode(new Dictionary<string, ContactValue[]>());
    private GmailAuthService? _auth;
    private GmailAuthService Auth => _auth ??= createAuth();
    private readonly SemaphoreSlim _gate = new(1);
    private string? _reviewedConflict;
    internal bool Busy => _gate.CurrentCount == 0;
    internal bool HasLink => File.Exists(statePath);
    internal bool NeedsAttention { get; private set; }
    internal bool HasConflict { get; private set; }
    internal string? Status { get; private set; }
    internal string? Email => _auth?.Email;
    internal bool Connected => _auth?.IsSignedIn == true;
    internal bool Configured => _auth?.IsConfigured == true;

    internal async Task ConnectAsync(Func<Task<GmailClientConfiguration?>> pickClient, CancellationToken token, bool chooseClient = false)
    {
        await _gate.WaitAsync(token);
        try
        {
            var client = chooseClient ? await pickClient() : Auth.Client ?? await pickClient();
            if (client is null) return;
            var link = ReadLink();
            if (link is not null && client.ClientId != link.ClientId)
                throw new InvalidOperationException("Use the Google Desktop OAuth client already linked to these contacts.");
            Auth.Configure(client);
            await Auth.SignInAsync(token);
            await SynchronizeCoreAsync(null, token);
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        { await SetErrorAsync(error, token); }
        finally { _gate.Release(); }
    }

    internal async Task SyncAsync(CancellationToken token, ContactDriveConflictChoice? choice = null)
    {
        if (!await _gate.WaitAsync(0, token)) return;
        try
        {
            if (HasLink && !Auth.IsSignedIn)
            {
                Status = "Reconnect Google Drive to resume syncing.";
                NeedsAttention = true;
                return;
            }
            await SynchronizeCoreAsync(choice, token);
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        { await SetErrorAsync(error, token); }
        finally { _gate.Release(); }
    }

    internal async Task DisconnectAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            await Auth.DisconnectAsync(token);
            NeedsAttention = true;
            HasConflict = false;
            Status = "Google Drive is disconnected on this PC. Your local contacts were kept.";
        }
        finally { _gate.Release(); }
    }

    private async Task SynchronizeCoreAsync(ContactDriveConflictChoice? choice, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var requestToken = timeout.Token;
        var drive = new ContactDriveClient(Auth, http);
        var owner = await drive.OwnerAsync(requestToken);
        var clientId = Auth.Client!.ClientId;
        var link = ReadLink();
        if (link is not null && (link.ClientId != clientId || link.Owner != owner))
        {
            await Auth.DisconnectAsync(requestToken);
            throw new InvalidOperationException("Connect the same Google account and OAuth client previously linked on this PC.");
        }
        link ??= new Link(clientId, owner);
        if (!contacts.Loaded) await contacts.LoadAsync(requestToken);
        await contacts.SynchronizeAsync(async book =>
        {
            var local = book.DriveContacts();
            var found = await drive.FindAsync(requestToken);
            if (link.FileId is not null && link.FileId != found)
                throw new InvalidOperationException("The linked contacts CSV could not be found. Restore the original file in Google Drive.");
            if (found is null)
            {
                var id = link.PendingId ?? await drive.GenerateIdAsync(false, requestToken);
                var rootId = link.RootId ?? await drive.GenerateIdAsync(true, requestToken);
                link = link with { PendingId = id, RootId = rootId };
                WriteLink(link);
                await drive.CreateProjectionAsync(id, rootId, EmptyCsv, requestToken);
                found = id;
            }
            var projection = await drive.ReadProjectionAsync(found, requestToken);
            if (projection.Edited)
                throw new InvalidOperationException("The visible contacts CSV was edited outside HABITAT. Restore its previous version in Google Drive.");
            if (link.FileId is not null && link.RootId != projection.RootId)
                throw new InvalidOperationException("The linked contacts CSV points to a different history. Restore the original file in Google Drive.");
            var baseline = link.Baseline is null ? new Dictionary<string, ContactValue[]>() : ContactDriveCsv.Decode(link.Baseline);
            var cachedHead = projection.HeadId ?? link.HeadId;
            var head = cachedHead is null ? null : await drive.ReadRevisionAsync(cachedHead, projection.RootId, requestToken)
                ?? throw new InvalidOperationException("The Google Drive contacts history is missing. Your local contacts were kept.");
            var nextId = head?.NextId ?? projection.RootId;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (await drive.ReadRevisionAsync(nextId, projection.RootId, requestToken) is { } revision)
                {
                    if (!visited.Add(revision.Id)) throw new InvalidDataException("The Google Drive contacts history is invalid.");
                    head = revision;
                    nextId = revision.NextId;
                }

                if (head is null && !string.Equals(projection.Hash, Convert.ToHexString(MD5.HashData(EmptyCsv)), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The Google Drive contacts history is missing. Your local contacts were kept.");
                var remote = head?.Content ?? EmptyCsv;
                var there = ContactDriveCsv.Decode(remote);
                var merged = MergeReviewed(baseline, local, there, remote, choice);
                var next = book.ApplyDriveContacts(merged);
                var csv = remote;
                if (merged.Count != there.Count || merged.Any(pair => !ContactDriveCsv.Same(pair.Value, there.GetValueOrDefault(pair.Key))))
                {
                    csv = ContactDriveCsv.Encode(merged);
                    var successor = await drive.GenerateIdAsync(true, requestToken);
                    if (!await drive.AppendAsync(nextId, projection.RootId, successor, csv, requestToken)) continue;
                    head = new(nextId, successor, csv);
                }

                // A visible CSV is a projection. Committing an immutable revision
                // first preserves contacts even if a concurrent export wins this race.
                await drive.PublishAsync(projection, head?.Id, csv, requestToken);
                var synced = link with { FileId = found, RootId = projection.RootId, HeadId = head?.Id, Baseline = csv, PendingId = null };
                return (next, () =>
                {
                    if (synced.FileId != link.FileId || synced.HeadId != link.HeadId || link.Baseline is null)
                        WriteLink(synced);
                });
            }
            throw new InvalidOperationException("Another device is syncing contacts. Sync again in a moment.");
        }, requestToken);
        NeedsAttention = HasConflict = false;
        _reviewedConflict = null;
        Status = "Contacts synced to Google Drive.";
    }

    private Dictionary<string, ContactValue[]> MergeReviewed(Dictionary<string, ContactValue[]> baseline,
        Dictionary<string, ContactValue[]> local, Dictionary<string, ContactValue[]> remote, byte[] remoteCsv,
        ContactDriveConflictChoice? choice)
    {
        var version = Convert.ToHexString(SHA256.HashData(ContactDriveCsv.Encode(local))) +
            Convert.ToHexString(SHA256.HashData(remoteCsv));
        try
        {
            return ContactDriveCsv.Merge(baseline, local, remote, version == _reviewedConflict ? choice : null);
        }
        catch (ContactDriveConflictException)
        {
            _reviewedConflict = version;
            throw;
        }
    }

    private async Task SetErrorAsync(Exception error, CancellationToken token)
    {
        HasConflict = error is ContactDriveConflictException;
        // Retry transient network failures automatically. A conflict or invalid file
        // requires an explicit action so the automatic retry cannot choose a side.
        var transient = error is OperationCanceledException || error is HttpRequestException httpError &&
            (httpError.StatusCode is null or System.Net.HttpStatusCode.TooManyRequests || (int)httpError.StatusCode >= 500);
        NeedsAttention = !transient;
        Status = error switch
        {
            GmailSignInRequiredException or HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized } =>
                "Google Drive needs authorization. Reconnect Google Drive to sync.",
            ContactReviewException => "Two contacts have overlapping or invalid details. Review your contacts and the Drive CSV, then sync again.",
            InvalidDataException or InvalidOperationException or ContactDriveConflictException => error.Message,
            HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden } =>
                "Google Drive denied access. Check that Drive API is enabled and the Google account has access.",
            _ => "Could not sync with Google Drive. Your local changes were kept. Try again."
        };
        if (error is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized })
            await Auth.DisconnectAsync(token);
    }

    private Link? ReadLink()
    {
        try
        {
            using var stream = File.OpenRead(statePath);
            if (stream.Length > ContactDriveCsv.MaxBytes * 2L) throw InvalidLink();
            var link = JsonSerializer.Deserialize<Link>(stream) ?? throw InvalidLink();
            if (string.IsNullOrWhiteSpace(link.Owner) || string.IsNullOrWhiteSpace(link.ClientId) ||
                (link.FileId is null) != (link.Baseline is null) ||
                (link.FileId is not null || link.PendingId is not null) && string.IsNullOrWhiteSpace(link.RootId)) throw InvalidLink();
            return link;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (JsonException) { throw InvalidLink(); }
    }

    private void WriteLink(Link link)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        var temporary = statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Open(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, link);
                stream.Flush(true);
            }
            File.Move(temporary, statePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static InvalidDataException InvalidLink() => new("The local Google Drive link is invalid. Your contacts were kept.");
    public void Dispose() => _auth?.Dispose();
}
