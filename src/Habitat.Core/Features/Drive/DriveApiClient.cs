using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Habitat.Services;

namespace Habitat.Features.Drive;

internal sealed class DriveApiClient(GmailAuthService auth, HttpClient http)
{
    internal const string Api = "https://www.googleapis.com/drive/v3/";
    internal const string Upload = "https://www.googleapis.com/upload/drive/v3/files";
    internal const string FolderMime = "application/vnd.google-apps.folder";
    internal const string FileFields = "id,name,mimeType,trashed,ownedByMe,parents,appProperties,md5Checksum,webViewLink,createdTime";

    internal async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, CancellationToken token, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", await auth.GetAccessTokenAsync(token));
        request.Content = content;
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
    }

    internal async Task<JsonElement?> MetadataAsync(string id, CancellationToken token)
    {
        using var response = await SendAsync(HttpMethod.Get, FilePath(id) + "?fields=" + FileFields, token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        return await JsonAsync(response, token);
    }

    internal async Task<JsonElement[]> FindAsync(string query, CancellationToken token)
    {
        using var response = await SendAsync(HttpMethod.Get, Api + "files?spaces=drive&pageSize=100&fields=" +
            Uri.EscapeDataString("nextPageToken,incompleteSearch,files(" + FileFields + ")") + "&q=" + Uri.EscapeDataString(query), token);
        var root = await JsonAsync(response, token);
        if (root.TryGetProperty("nextPageToken", out _) ||
            root.TryGetProperty("incompleteSearch", out var incomplete) && incomplete.GetBoolean())
            throw new InvalidDataException("Google Drive returned an incomplete HABITAT file search. Your local data was kept.");
        return root.GetProperty("files").EnumerateArray().Select(file => file.Clone()).ToArray();
    }

    internal async Task<string> OwnerAsync(CancellationToken token)
    {
        using var response = await SendAsync(HttpMethod.Get, Api + "about?fields=user(permissionId)", token);
        var root = await JsonAsync(response, token);
        return String(root.GetProperty("user"), "permissionId") is { Length: > 0 } id ? id :
            throw new InvalidDataException("Google Drive did not return an account identifier.");
    }

    internal async Task<string> GenerateIdAsync(bool privateFile, CancellationToken token)
    {
        using var response = await SendAsync(HttpMethod.Get, Api + "files/generateIds?count=1&space=" +
            (privateFile ? "appDataFolder" : "drive"), token);
        var root = await JsonAsync(response, token);
        return root.GetProperty("ids")[0].GetString() is { Length: > 0 } id ? id :
            throw new InvalidDataException("Google Drive did not return a file identifier.");
    }

    internal static async Task<JsonElement> JsonAsync(HttpResponseMessage response, CancellationToken token)
    {
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new InvalidDataException("Google Drive metadata is too large.");
        var bytes = await BytesAsync(response, 2 * 1024 * 1024, token);
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }

    internal static async Task<byte[]> BytesAsync(HttpResponseMessage response, int limit, CancellationToken token)
    {
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("The Google Drive file is too large to sync.");
        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + count > limit) throw new InvalidDataException("The Google Drive file is too large to sync.");
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }

    internal static MultipartContent Multipart<T>(T metadata, byte[] bytes, string mime)
    {
        var content = new MultipartContent("related");
        content.Add(JsonContent.Create(metadata));
        var body = new ByteArrayContent(bytes);
        body.Headers.ContentType = new(mime) { CharSet = "utf-8" };
        content.Add(body);
        return content;
    }

    internal static string FilePath(string id) => Api + "files/" + Uri.EscapeDataString(id);
    internal static string ContentHash(byte[] bytes) => Convert.ToHexString(MD5.HashData(bytes));
    internal static string? String(JsonElement value, string key) => value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    internal static string Property(JsonElement value, string key) => String(value, key) is { Length: > 0 } text ? text :
        throw new InvalidDataException("The Google Drive HABITAT metadata is invalid. Your local data was kept.");
    internal static string[] Parents(JsonElement file) => file.TryGetProperty("parents", out var parents) && parents.ValueKind == JsonValueKind.Array
        ? parents.EnumerateArray().Select(item => item.GetString() ?? "").ToArray() : [];
    internal static bool Owned(JsonElement file) => file.TryGetProperty("ownedByMe", out var owned) && owned.ValueKind == JsonValueKind.True &&
        file.TryGetProperty("trashed", out var trashed) && trashed.ValueKind == JsonValueKind.False;
}
