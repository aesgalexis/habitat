using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Habitat.Services;

namespace Habitat.Features.Drive;

internal sealed record DriveFolderChoice(string Id, string Name, DateTimeOffset? CreatedAt, Uri Url);
internal sealed record DriveWorkspaceContext(DriveApiClient Api, string ClientId, string Owner, string FolderId);

internal sealed class DriveWorkspace(string path, string legacyContactsPath, string? legacyTasksPath = null)
{
    internal const string Marker = "habitatWorkspace";
    private sealed record State(int Schema, string ClientId, string Owner, string? FolderId = null,
        string? PendingFolderId = null, bool ExplicitFolder = false);
    internal IReadOnlyList<DriveFolderChoice> Choices { get; private set; } = [];
    internal Uri? FolderUrl { get; private set; }

    internal string? ExpectedClientId => Read()?.ClientId ?? Legacy().Select(link => link.ClientId).FirstOrDefault();
    private (string ClientId, string Owner)[] Legacy()
    {
        var links = new List<(string, string)>();
        foreach (var linkPath in new[] { legacyContactsPath, legacyTasksPath })
        {
            if (linkPath is null || !File.Exists(linkPath)) continue;
            if (new FileInfo(linkPath).Length > 32 * 1024 * 1024) throw InvalidLink();
            using var doc = JsonDocument.Parse(File.ReadAllBytes(linkPath));
            links.Add((DriveApiClient.Property(doc.RootElement, "ClientId"), DriveApiClient.Property(doc.RootElement, "Owner")));
        }
        return links.ToArray();
    }

    internal async Task<DriveWorkspaceContext> EnsureAsync(DriveApiClient api, string clientId, CancellationToken token, int creationAttempt = 0)
    {
        if (creationAttempt >= 3) throw new HttpRequestException("Google Drive is still preparing the HABITAT folder. Sync again in a moment.");
        var owner = await api.OwnerAsync(token);
        var state = Read();
        // Before the first workspace link, retain the datasets' account guard.
        // Afterwards the workspace anchors identity and each module validates
        // its own link, so a damaged dataset link cannot stop the other module.
        var legacy = state is null ? Legacy() : [];
        if (state is not null && (state.ClientId != clientId || state.Owner != owner) ||
            legacy.Any(previous => previous.ClientId != clientId || previous.Owner != owner))
            throw new DriveIdentityException();
        if (state is null) { state = new(1, clientId, owner); Save(state); }

        // Check an established link first. A missing folder is recovery work,
        // not authorization to create a replacement with an empty history.
        JsonElement? linked = null;
        if (state.FolderId is { } linkedId)
            linked = Validate(await api.MetadataAsync(linkedId, token), linkedId);
        JsonElement[] files = state.ExplicitFolder && linked is not null ? [linked.Value] : await api.FindAsync(
            "trashed = false and 'me' in owners and mimeType = '" + DriveApiClient.FolderMime +
            "' and appProperties has { key='" + Marker + "' and value='1' }", token);
        Choices = files.Select(file => Choice(Validate(file, DriveApiClient.Property(file, "id")))).ToArray();
        if (files.Length > 1)
            throw new InvalidOperationException("More than one HABITAT folder exists. Open Google Drive settings and choose the folder to use on this PC.");
        if (files.Length == 1)
        {
            var folder = Validate(files[0], DriveApiClient.Property(files[0], "id"));
            var id = DriveApiClient.Property(folder, "id");
            if (state.FolderId is not null && state.FolderId != id) throw InvalidLink();
            if (state.FolderId != id || state.PendingFolderId is not null) Save(state with { FolderId = id, PendingFolderId = null });
            FolderUrl = Choice(folder).Url;
            Choices = [];
            return new(api, clientId, owner, id);
        }
        if (linked is not null) throw InvalidLink();
        var pending = state.PendingFolderId ?? await api.GenerateIdAsync(false, token);
        state = state with { PendingFolderId = pending };
        Save(state);
        using var content = JsonContent.Create(new { id = pending, name = "HABITAT", mimeType = DriveApiClient.FolderMime,
            parents = new[] { "root" }, appProperties = new Dictionary<string, string> { [Marker] = "1" } });
        using var response = await api.SendAsync(HttpMethod.Post, DriveApiClient.Api + "files?fields=id", token, content);
        if (response.StatusCode != HttpStatusCode.Conflict)
        {
            var created = await DriveApiClient.JsonAsync(response, token);
            if (DriveApiClient.Property(created, "id") != pending) throw InvalidLink();
        }
        // Discover again before using a new folder, including concurrent first
        // connections. A duplicate stays visible for explicit reconciliation.
        return await EnsureAsync(api, clientId, token, creationAttempt + 1);
    }

    internal async Task SelectAsync(DriveApiClient api, string clientId, string id, CancellationToken token)
    {
        if (!Choices.Any(choice => choice.Id == id)) throw new InvalidOperationException("Reload the available HABITAT folders before choosing one.");
        var owner = await api.OwnerAsync(token);
        var state = Read() ?? throw InvalidLink();
        if (state.ClientId != clientId || state.Owner != owner) throw new DriveIdentityException();
        var folder = Validate(await api.MetadataAsync(id, token), id);
        Save(state with { FolderId = id, PendingFolderId = null, ExplicitFolder = true });
        FolderUrl = Choice(folder).Url;
        Choices = [];
    }

    private static JsonElement Validate(JsonElement? value, string id)
    {
        if (value is not { } file || !DriveApiClient.Owned(file) || DriveApiClient.String(file, "id") != id ||
            DriveApiClient.String(file, "mimeType") != DriveApiClient.FolderMime ||
            !file.TryGetProperty("appProperties", out var properties) || DriveApiClient.String(properties, Marker) != "1")
            throw new InvalidOperationException("The linked HABITAT folder is missing or unavailable. Restore it in Google Drive. Your local data was kept.");
        return file;
    }
    private static DriveFolderChoice Choice(JsonElement file)
    {
        var id = DriveApiClient.Property(file, "id");
        var date = DateTimeOffset.TryParse(DriveApiClient.String(file, "createdTime"), out var created) ? created : (DateTimeOffset?)null;
        return new(id, DriveApiClient.String(file, "name") ?? "HABITAT", date,
            new Uri("https://drive.google.com/drive/folders/" + Uri.EscapeDataString(id)));
    }
    private State? Read()
    {
        if (!File.Exists(path)) return null;
        try
        {
            if (new FileInfo(path).Length > 65536) throw InvalidLink();
            var state = JsonSerializer.Deserialize<State>(File.ReadAllBytes(path));
            if (state is not { Schema: 1 } || string.IsNullOrWhiteSpace(state.ClientId) || string.IsNullOrWhiteSpace(state.Owner) ||
                state.FolderId is not null && string.IsNullOrWhiteSpace(state.FolderId) ||
                state.PendingFolderId is not null && string.IsNullOrWhiteSpace(state.PendingFolderId) || state.ExplicitFolder && state.FolderId is null)
                throw InvalidLink();
            return state;
        }
        catch (JsonException) { throw InvalidLink(); }
    }
    private void Save(State state) => AtomicFile.Write(path, JsonSerializer.SerializeToUtf8Bytes(state));
    private static InvalidDataException InvalidLink() => new("The Google Drive workspace link is invalid. Your local data was kept.");
}

internal sealed class DriveIdentityException() : InvalidOperationException("Connect the same Google account and OAuth client already linked to HABITAT on this PC.");
