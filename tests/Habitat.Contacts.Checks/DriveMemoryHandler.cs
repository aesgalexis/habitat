using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Habitat.Features.Drive;
using Microsoft.AspNetCore.WebUtilities;

internal class DriveMemoryHandler : HttpMessageHandler
{
    internal sealed record FileEntry(string Id, string Name, string Mime, Dictionary<string, string> Properties,
        string[] Parents, byte[] Bytes, bool Trashed = false, bool Owned = true);
    private readonly Dictionary<string, FileEntry> _files = new();
    private readonly object _gate = new();
    private TaskCompletionSource? _race;
    private string _raceKey = "habitatContactsNext";
    private int _raceArrivals, _ids;
    internal int RejectedAppends { get; private set; }
    internal int Writes { get; private set; }
    internal int Moves { get; private set; }
    internal string Owner { get; set; } = "drive-owner";
    internal bool FailAfterNextMove { get; set; }
    internal bool FailAfterNextAppend { get; set; }
    internal Func<Task>? BeforeTasksDownload { get; set; }
    internal Func<HttpRequestMessage, HttpResponseMessage?>? Override { get; set; }
    internal FileEntry[] Files { get { lock (_gate) return _files.Values.Select(Clone).ToArray(); } }
    internal string VisibleName => Files.Single(file => file.Properties.ContainsKey("habitatContactsCsv")).Name;
    internal byte[] VisibleBytes => Files.Single(file => file.Properties.ContainsKey("habitatContactsCsv")).Bytes;
    internal FileEntry TasksFile => Files.Single(file => file.Properties.ContainsKey("habitatTasksJson"));
    internal void RaceNextAppends(string key = "habitatContactsNext")
    { _raceKey = key; _raceArrivals = 0; _race = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    internal void Change(string id, Func<FileEntry, FileEntry> change)
    { lock (_gate) _files[id] = Clone(change(Clone(_files[id]))); }
    internal string AddFolder(bool managed = true, string name = "HABITAT")
    {
        var id = "folder-" + Interlocked.Increment(ref _ids);
        lock (_gate) _files.Add(id, new(id, name, DriveApiClient.FolderMime,
            managed ? new() { [DriveWorkspace.Marker] = "1" } : new(), ["root"], []));
        return id;
    }
    private static FileEntry Clone(FileEntry file) => file with { Properties = new(file.Properties), Parents = file.Parents.ToArray(), Bytes = file.Bytes.ToArray() };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (Override?.Invoke(request) is { } overridden) return overridden;
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath;
        var query = QueryHelpers.ParseQuery(uri.Query);
        if (uri.Host == "oauth2.googleapis.com" && path == "/token")
            return Json(new { access_token = "access", refresh_token = "refresh", expires_in = 3600,
                scope = "https://www.googleapis.com/auth/drive.file https://www.googleapis.com/auth/drive.appdata" });
        if (path.EndsWith("/about")) return Json(new { user = new { permissionId = Owner, emailAddress = "owner@example.test" } });
        if (path.EndsWith("/generateIds")) return Json(new { ids = new[] { "generated-" + Interlocked.Increment(ref _ids) } });
        if (request.Method == HttpMethod.Get)
        {
            FileEntry? download = null;
            lock (_gate)
            {
                if (path.EndsWith("/files"))
                {
                    var match = Regex.Match(query["q"].ToString(), @"key='([^']+)' and value='([^']+)'");
                    var files = _files.Values.Where(file => !file.Trashed && file.Owned &&
                        (!match.Success || file.Properties.GetValueOrDefault(match.Groups[1].Value) == match.Groups[2].Value));
                    if (query["q"].ToString().Contains("mimeType = '" + DriveApiClient.FolderMime + "'"))
                        files = files.Where(file => file.Mime == DriveApiClient.FolderMime);
                    return Json(new { files = files.Select(Metadata).ToArray() });
                }
                var id = Uri.UnescapeDataString(path.Split('/')[^1]);
                if (!_files.TryGetValue(id, out var file)) return new(HttpStatusCode.NotFound);
                if (query.TryGetValue("alt", out var alt) && alt == "media") download = Clone(file);
                else return Json(Metadata(file));
            }
            if (download.Properties.ContainsKey("habitatTasksNext") && BeforeTasksDownload is { } before)
            { BeforeTasksDownload = null; await before(); }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(download.Bytes) };
        }
        JsonElement metadata;
        byte[]? bytes = null;
        if (request.Content is MultipartContent multipart)
        {
            var parts = multipart.ToArray();
            using var document = JsonDocument.Parse(await parts[0].ReadAsStringAsync(token));
            metadata = document.RootElement.Clone();
            bytes = await parts[1].ReadAsByteArrayAsync(token);
        }
        else
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            metadata = document.RootElement.Clone();
        }
        var properties = metadata.TryGetProperty("appProperties", out var propertiesJson)
            ? propertiesJson.Deserialize<Dictionary<string, string>>()! : new Dictionary<string, string>();
        if (properties.ContainsKey(_raceKey) && _race is { } race)
        {
            if (Interlocked.Increment(ref _raceArrivals) == 2) { _race = null; race.TrySetResult(); }
            await race.Task.WaitAsync(token);
        }
        lock (_gate)
        {
            var id = request.Method == HttpMethod.Post ? metadata.GetProperty("id").GetString()! : Uri.UnescapeDataString(path.Split('/')[^1]);
            if (request.Method == HttpMethod.Post)
            {
                var parents = metadata.TryGetProperty("parents", out var parentJson) ? parentJson.Deserialize<string[]>()! : ["root"];
                if (!_files.TryAdd(id, new(id, metadata.GetProperty("name").GetString()!, metadata.GetProperty("mimeType").GetString()!,
                    new(properties), parents, bytes ?? [])))
                {
                    if (properties.ContainsKey("habitatContactsNext") || properties.ContainsKey("habitatTasksNext")) RejectedAppends++;
                    return new(HttpStatusCode.Conflict);
                }
            }
            else
            {
                if (!_files.TryGetValue(id, out var old)) return new(HttpStatusCode.NotFound);
                var merged = new Dictionary<string, string>(old.Properties);
                foreach (var (key, value) in properties) merged[key] = value;
                var parents = old.Parents;
                if (query.TryGetValue("addParents", out var added))
                {
                    var removed = (query.TryGetValue("removeParents", out var removedParents) ? removedParents.ToString() : "")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries);
                    parents = parents.Except(removed).Union(added.ToString().Split(',')).ToArray();
                    Moves++;
                }
                _files[id] = old with { Properties = merged, Parents = parents, Bytes = bytes ?? old.Bytes };
            }
            Writes++;
            if (query.ContainsKey("addParents") && FailAfterNextMove)
            { FailAfterNextMove = false; throw new HttpRequestException("Synthetic lost move response after commit."); }
            if ((properties.ContainsKey("habitatContactsNext") || properties.ContainsKey("habitatTasksNext")) && FailAfterNextAppend)
            { FailAfterNextAppend = false; throw new HttpRequestException("Synthetic lost revision response after commit."); }
            return Json(new { id });
        }
    }
    private static object Metadata(FileEntry file) => new
    {
        id = file.Id, name = file.Name, mimeType = file.Mime, trashed = file.Trashed, ownedByMe = file.Owned,
        parents = file.Parents, appProperties = new Dictionary<string, string>(file.Properties),
        md5Checksum = Convert.ToHexString(MD5.HashData(file.Bytes)).ToLowerInvariant(),
        createdTime = "2026-11-06T10:00:00Z"
    };
    private static HttpResponseMessage Json<T>(T data) => new(HttpStatusCode.OK) { Content = JsonContent.Create(data) };
}
