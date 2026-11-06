using System.Net;

namespace Habitat.Features.Drive;

internal sealed record DriveDocumentKind(string Name, string Mime, string Marker, string Root, string Hash, string Next, string Head, int MaxBytes)
{
    internal static DriveDocumentKind Contacts { get; } = new("HABITAT Contacts.csv", "text/csv", "habitatContactsCsv",
        "habitatContactsRoot", "habitatContactsHash", "habitatContactsNext", "habitatContactsHead", Habitat.Features.Contacts.ContactDriveCsv.MaxBytes);
    internal static DriveDocumentKind Tasks { get; } = new("HABITAT Tasks.json", "application/json", "habitatTasksJson",
        "habitatTasksRoot", "habitatTasksHash", "habitatTasksNext", "habitatTasksHead", 16 * 1024 * 1024);
}

internal sealed record DriveProjection(string Id, string RootId, string? HeadId, string Hash, bool Edited, string[] Parents);
internal sealed record DriveRevision(string Id, string NextId, byte[] Content);

// The visible document is a projection. Immutable revisions in appDataFolder
// reserve the next ID, preserving the existing contacts concurrency protocol.
internal class DriveDocumentClient(DriveWorkspaceContext workspace, DriveDocumentKind kind)
{
    private DriveApiClient Api => workspace.Api;
    internal async Task<string?> FindAsync(CancellationToken token)
    {
        // Find old root-level CSVs before applying the managed parent folder.
        var files = await Api.FindAsync("trashed = false and 'me' in owners and appProperties has { key='" + kind.Marker + "' and value='1' }", token);
        if (files.Length > 1)
            throw new InvalidOperationException("More than one " + kind.Name + " exists. Review the HABITAT files in Google Drive before syncing.");
        return files.Length == 0 ? null : DriveApiClient.Property(files[0], "id");
    }

    internal async Task<DriveProjection> ReadProjectionAsync(string id, CancellationToken token)
    {
        var value = await Api.MetadataAsync(id, token);
        if (value is not { } file)
            throw new InvalidOperationException("The linked " + kind.Name + " is missing. Restore it in Google Drive. Your local data was kept.");
        var properties = CheckFile(file);
        if (DriveApiClient.Property(properties, kind.Marker) != "1") throw InvalidFile();
        var root = DriveApiClient.Property(properties, kind.Root);
        var checksum = DriveApiClient.Property(file, "md5Checksum");
        return new(id, root, DriveApiClient.String(properties, kind.Head), checksum,
            !string.Equals(DriveApiClient.Property(properties, kind.Hash), checksum, StringComparison.OrdinalIgnoreCase), DriveApiClient.Parents(file));
    }

    internal async Task<DriveProjection> PlaceAsync(DriveProjection projection, CancellationToken token)
    {
        if (projection.Edited) throw Edited();
        if (projection.Parents.Contains(workspace.FolderId, StringComparer.Ordinal)) return projection;
        // Relocation changes metadata only, retaining the ID and private history.
        var query = "?addParents=" + Uri.EscapeDataString(workspace.FolderId) + "&fields=id";
        if (projection.Parents.Length > 0) query += "&removeParents=" + Uri.EscapeDataString(string.Join(',', projection.Parents));
        using var content = System.Net.Http.Json.JsonContent.Create(new { });
        using var response = await Api.SendAsync(HttpMethod.Patch, DriveApiClient.FilePath(projection.Id) + query, token, content);
        response.EnsureSuccessStatusCode();
        var placed = await ReadProjectionAsync(projection.Id, token);
        if (placed.RootId != projection.RootId || placed.Edited || !placed.Parents.Contains(workspace.FolderId, StringComparer.Ordinal))
            throw InvalidFile();
        // An older PC can publish a newer head during the move; read that head.
        return placed;
    }

    internal async Task<DriveRevision?> ReadRevisionAsync(string id, string rootId, CancellationToken token)
    {
        var value = await Api.MetadataAsync(id, token);
        if (value is not { } file) return null;
        var properties = CheckFile(file);
        var next = DriveApiClient.Property(properties, kind.Next);
        if (next == id || DriveApiClient.Property(properties, kind.Root) != rootId) throw InvalidFile();
        using var response = await Api.SendAsync(HttpMethod.Get, DriveApiClient.FilePath(id) + "?alt=media", token);
        var bytes = await DriveApiClient.BytesAsync(response, kind.MaxBytes, token);
        if (!string.Equals(DriveApiClient.String(file, "md5Checksum"), DriveApiClient.ContentHash(bytes), StringComparison.OrdinalIgnoreCase))
            throw InvalidFile();
        return new(id, next, bytes);
    }

    internal Task<string> GenerateIdAsync(bool privateRevision, CancellationToken token) => Api.GenerateIdAsync(privateRevision, token);

    internal async Task CreateProjectionAsync(string id, string rootId, byte[] bytes, CancellationToken token)
    {
        using var content = DriveApiClient.Multipart(new
        {
            id, name = kind.Name, mimeType = kind.Mime, parents = new[] { workspace.FolderId },
            appProperties = new Dictionary<string, string> { [kind.Marker] = "1", [kind.Root] = rootId, [kind.Hash] = DriveApiClient.ContentHash(bytes) }
        }, bytes, kind.Mime);
        using var response = await Api.SendAsync(HttpMethod.Post, DriveApiClient.Upload + "?uploadType=multipart&fields=id", token, content);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var existing = await ReadProjectionAsync(id, token);
            if (existing.RootId != rootId || existing.Edited) throw InvalidFile();
            return;
        }
        var result = await DriveApiClient.JsonAsync(response, token);
        if (DriveApiClient.Property(result, "id") != id) throw InvalidFile();
    }

    internal async Task<bool> AppendAsync(string id, string rootId, string nextId, byte[] bytes, CancellationToken token)
    {
        if (bytes.Length > kind.MaxBytes) throw new InvalidDataException("The HABITAT document is too large to sync.");
        using var content = DriveApiClient.Multipart(new
        {
            id, name = (kind == DriveDocumentKind.Contacts ? "contacts-" : "tasks-") + id + (kind.Mime == "text/csv" ? ".csv" : ".json"),
            mimeType = kind.Mime, parents = new[] { "appDataFolder" },
            appProperties = new Dictionary<string, string> { [kind.Root] = rootId, [kind.Next] = nextId }
        }, bytes, kind.Mime);
        using var response = await Api.SendAsync(HttpMethod.Post, DriveApiClient.Upload + "?uploadType=multipart&fields=id", token, content);
        if (response.StatusCode == HttpStatusCode.Conflict) return false;
        var result = await DriveApiClient.JsonAsync(response, token);
        if (DriveApiClient.Property(result, "id") != id) throw InvalidFile();
        return true;
    }

    internal async Task PublishAsync(DriveProjection projection, string? headId, byte[] bytes, CancellationToken token)
    {
        if (projection.Edited) throw Edited();
        var hash = DriveApiClient.ContentHash(bytes);
        if (headId == projection.HeadId && string.Equals(projection.Hash, hash, StringComparison.OrdinalIgnoreCase)) return;
        var properties = new Dictionary<string, string> { [kind.Hash] = hash };
        if (headId is not null) properties[kind.Head] = headId;
        using var content = DriveApiClient.Multipart(new { appProperties = properties }, bytes, kind.Mime);
        using var response = await Api.SendAsync(HttpMethod.Patch, DriveApiClient.Upload + "/" + Uri.EscapeDataString(projection.Id) +
            "?uploadType=multipart&fields=id", token, content);
        response.EnsureSuccessStatusCode();
    }

    private System.Text.Json.JsonElement CheckFile(System.Text.Json.JsonElement file)
    {
        if (!DriveApiClient.Owned(file) || DriveApiClient.String(file, "mimeType") != kind.Mime ||
            !file.TryGetProperty("appProperties", out var properties)) throw InvalidFile();
        return properties;
    }
    private InvalidDataException InvalidFile() => new("A " + kind.Name + " file in Google Drive is invalid. Your local data was kept.");
    private InvalidOperationException Edited() => new("The visible " + kind.Name + " was edited outside HABITAT. Restore its previous version in Google Drive.");
}
