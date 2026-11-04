using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Habitat.Services;

namespace Habitat.Features.Contacts;

internal sealed class ContactDriveClient(GmailAuthService auth, HttpClient http)
{
    internal const string FileName = "HABITAT Contacts.csv";
    private const string Api = "https://www.googleapis.com/drive/v3/";
    private const string Upload = "https://www.googleapis.com/upload/drive/v3/files";
    private const string Marker = "habitatContactsCsv";
    private const string Root = "habitatContactsRoot";
    private const string Hash = "habitatContactsHash";
    private const string Next = "habitatContactsNext";
    private const string Head = "habitatContactsHead";
    private const string CsvMime = "text/csv";
    private const string Fields = "id,mimeType,trashed,ownedByMe,appProperties,md5Checksum";
    private const string Query = "trashed = false and 'me' in owners and appProperties has { key='" + Marker + "' and value='1' }";

    internal sealed record Projection(string Id, string RootId, string? HeadId, string Hash, bool Edited);
    internal sealed record Revision(string Id, string NextId, byte[] Content);

    internal async Task<string> OwnerAsync(CancellationToken token)
    {
        using var response = await SendAsync(HttpMethod.Get, Api + "about?fields=user(permissionId)", token);
        using var json = await ReadJsonAsync(response, token);
        var id = json.RootElement.GetProperty("user").GetProperty("permissionId").GetString();
        return !string.IsNullOrWhiteSpace(id) ? id : throw new InvalidDataException("Google Drive did not return an account identifier.");
    }

    internal async Task<string?> FindAsync(CancellationToken token)
    {
        using var response = await SendAsync(HttpMethod.Get, Api + "files?spaces=drive&pageSize=100&fields=nextPageToken,incompleteSearch,files(id)&q=" +
            Uri.EscapeDataString(Query), token);
        using var json = await ReadJsonAsync(response, token);
        var root = json.RootElement;
        var ids = root.GetProperty("files").EnumerateArray().ToArray();
        if (ids.Length > 1 || root.TryGetProperty("nextPageToken", out _) ||
            root.TryGetProperty("incompleteSearch", out var incomplete) && incomplete.GetBoolean())
            throw new InvalidOperationException("More than one contacts CSV may exist in Google Drive. Keep one HABITAT Contacts.csv and try again.");
        return ids.Length == 0 ? null : ReadId(ids[0]);
    }

    internal async Task<Projection> ReadProjectionAsync(string id, CancellationToken token)
    {
        using var metadata = await SendAsync(HttpMethod.Get, FilePath(id) + "?fields=" + Fields, token);
        if (metadata.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("The linked contacts CSV is missing. Restore it in Google Drive before syncing.");
        using var json = await ReadJsonAsync(metadata, token);
        var file = json.RootElement;
        var properties = CheckFile(file);
        if (ReadProperty(properties, Marker) != "1") throw InvalidFile();
        var root = ReadProperty(properties, Root);
        var checksum = file.GetProperty("md5Checksum").GetString() ?? throw InvalidFile();
        var head = properties.TryGetProperty(Head, out var value) ? value.GetString() : null;
        return new(id, root, head, checksum, !string.Equals(ReadProperty(properties, Hash), checksum, StringComparison.OrdinalIgnoreCase));
    }

    internal async Task<Revision?> ReadRevisionAsync(string id, string rootId, CancellationToken token)
    {
        using var metadata = await SendAsync(HttpMethod.Get, FilePath(id) + "?fields=" + Fields, token);
        if (metadata.StatusCode == HttpStatusCode.NotFound) return null;
        using var json = await ReadJsonAsync(metadata, token);
        var file = json.RootElement;
        var properties = CheckFile(file);
        var next = ReadProperty(properties, Next);
        if (next == id || ReadProperty(properties, Root) != rootId) throw InvalidFile();
        using var response = await SendAsync(HttpMethod.Get, FilePath(id) + "?alt=media", token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > ContactDriveCsv.MaxBytes) throw TooLarge();
        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int size;
        while ((size = await stream.ReadAsync(chunk, token)) != 0)
        {
            if (buffer.Length + size > ContactDriveCsv.MaxBytes) throw TooLarge();
            buffer.Write(chunk, 0, size);
        }
        var content = buffer.ToArray();
        if (!file.TryGetProperty("md5Checksum", out var checksum) ||
            !string.Equals(checksum.GetString(), ContentHash(content), StringComparison.OrdinalIgnoreCase))
            throw InvalidFile();
        return new(id, next, content);
    }

    internal async Task<string> GenerateIdAsync(bool privateRevision, CancellationToken token)
    {
        using var response = await SendAsync(HttpMethod.Get, Api + "files/generateIds?count=1&space=" +
            (privateRevision ? "appDataFolder" : "drive"), token);
        using var json = await ReadJsonAsync(response, token);
        return json.RootElement.GetProperty("ids")[0].GetString() ?? throw new InvalidDataException("Google Drive did not return a file identifier.");
    }

    internal async Task CreateProjectionAsync(string id, string rootId, byte[] csv, CancellationToken token)
    {
        using var content = Multipart(new
        {
            id, name = FileName, mimeType = CsvMime,
            appProperties = new Dictionary<string, string> { [Marker] = "1", [Root] = rootId, [Hash] = ContentHash(csv) }
        }, csv);
        using var response = await SendAsync(HttpMethod.Post, Upload + "?uploadType=multipart&fields=id", token, content);
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new InvalidOperationException("Google Drive already created the contacts CSV. Sync again to reconnect it.");
        using var json = await ReadJsonAsync(response, token);
        if (ReadId(json.RootElement) != id) throw new InvalidDataException("Google Drive returned an unexpected contacts file identifier.");
    }

    // A generated ID is reserved for the next revision. Google rejects a second
    // create with that ID, so two PCs cannot overwrite one another's committed data.
    internal async Task<bool> AppendAsync(string id, string rootId, string nextId, byte[] csv, CancellationToken token)
    {
        using var content = Multipart(new
        {
            id, name = "contacts-" + id + ".csv", mimeType = CsvMime, parents = new[] { "appDataFolder" },
            appProperties = new Dictionary<string, string> { [Root] = rootId, [Next] = nextId }
        }, csv);
        using var response = await SendAsync(HttpMethod.Post, Upload + "?uploadType=multipart&fields=id", token, content);
        if (response.StatusCode == HttpStatusCode.Conflict) return false;
        using var json = await ReadJsonAsync(response, token);
        if (ReadId(json.RootElement) != id) throw InvalidFile();
        return true;
    }

    internal async Task PublishAsync(Projection file, string? headId, byte[] csv, CancellationToken token)
    {
        if (file.Edited)
            throw new InvalidOperationException("The visible contacts CSV was edited outside HABITAT. Restore its previous version in Google Drive.");
        var hash = ContentHash(csv);
        if (headId == file.HeadId && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)) return;
        var properties = new Dictionary<string, string> { [Hash] = hash };
        if (headId is not null) properties[Head] = headId;
        using var content = Multipart(new { appProperties = properties }, csv);
        using var response = await SendAsync(HttpMethod.Patch, Upload + "/" + Uri.EscapeDataString(file.Id) +
            "?uploadType=multipart&fields=id", token, content);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, CancellationToken token, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", await auth.GetAccessTokenAsync(token));
        request.Content = content;
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
    }

    private static MultipartContent Multipart<T>(T metadata, byte[] bytes)
    {
        var multipart = new MultipartContent("related");
        multipart.Add(JsonContent.Create(metadata));
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(CsvMime) { CharSet = "utf-8" };
        multipart.Add(content);
        return multipart;
    }

    private static string FilePath(string id) => Api + "files/" + Uri.EscapeDataString(id);
    private static string ContentHash(byte[] bytes) => Convert.ToHexString(MD5.HashData(bytes));
    private static InvalidDataException InvalidFile() => new("A contacts file in Google Drive is invalid. Your contacts were kept.");

    private static JsonElement CheckFile(JsonElement file)
    {
        if (file.GetProperty("trashed").GetBoolean() || !file.GetProperty("ownedByMe").GetBoolean() ||
            file.GetProperty("mimeType").GetString() != CsvMime || !file.TryGetProperty("appProperties", out var properties))
            throw InvalidFile();
        return properties;
    }

    private static string ReadProperty(JsonElement properties, string key) =>
        properties.TryGetProperty(key, out var value) && value.GetString() is { Length: > 0 } id ? id : throw InvalidFile();

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken token)
    {
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }

    private static string ReadId(JsonElement file) =>
        file.GetProperty("id").GetString() ?? throw new InvalidDataException("Google Drive did not return a file identifier.");
    private static InvalidDataException TooLarge() => new("The contacts CSV is too large to sync.");
}
