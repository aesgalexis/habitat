using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Habitat.Services;

internal enum UpdatePackageKind { Installer, Zip }
internal sealed record ReleaseAsset(string Name, Uri Url, long Size);
internal sealed record AppUpdate(string Version, UpdatePackageKind Kind, ReleaseAsset Package, ReleaseAsset Checksum);
internal sealed record PreparedAppUpdate(AppUpdate Release, string Directory, string PackagePath, string Sha256, string? ExtractedDirectory);

internal interface IAppUpdateService : IDisposable
{
    string? FailedVersion => null;
    Task<AppUpdate?> CheckAsync(CancellationToken token);
    Task<PreparedAppUpdate> PrepareAsync(AppUpdate release, IProgress<int> progress, CancellationToken token);
    Task LaunchInstallerAsync(PreparedAppUpdate prepared, CancellationToken token);
}

// Only published, stable releases of our repository are eligible. Download
// redirects are checked explicitly so metadata cannot turn this into a launcher
// for an arbitrary URL. No account credentials or user content are sent.
internal sealed class ReleaseUpdateClient : IDisposable
{
    internal static readonly Uri LatestRelease = new("https://api.github.com/repos/aesgalexis/habitat/releases/latest");
    internal const long MaxPackageBytes = 600L * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Version _current;
    private readonly UpdatePackageKind _kind;
    private readonly string _cache;

    internal ReleaseUpdateClient(Version current, UpdatePackageKind kind, string cache, HttpClient? http = null)
    {
        _current = new(current.Major, current.Minor, Math.Max(0, current.Build));
        _kind = kind;
        _cache = Path.GetFullPath(cache);
        _ownsHttp = http is null;
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    internal static bool TryVersion(string? text, out Version version)
    {
        version = new(0, 0, 0);
        return text is not null && Regex.IsMatch(text, @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant) &&
            Version.TryParse(text, out version!) && version.Major <= 65535 && version.Minor <= 65535 && version.Build <= 65535;
    }

    internal static bool AllowedDownload(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == "https" && uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 && uri.Host is "github.com" or "api.github.com" or
            "release-assets.githubusercontent.com" or "objects.githubusercontent.com" or "github-releases.githubusercontent.com";

    internal async Task<AppUpdate?> CheckAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var response = await GetAsync(LatestRelease, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var bytes = await ReadSmallAsync(response, 2 * 1024 * 1024, timeout.Token);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString();
        if (tag is null || !tag.StartsWith('v') || !TryVersion(tag[1..], out var version) || version <= _current) return null;
        var name = _kind == UpdatePackageKind.Installer ? $"HABITAT-{version}-x64-setup.exe" : $"HABITAT-{version}-win-x64.zip";
        ReleaseAsset? Find(string expected, long maximum)
        {
            var matches = root.GetProperty("assets").EnumerateArray()
                .Where(asset => asset.GetProperty("name").GetString() == expected).ToArray();
            if (matches.Length != 1) return null;
            var asset = matches[0];
            if (asset.GetProperty("state").GetString() != "uploaded") return null;
            var size = asset.GetProperty("size").GetInt64();
            if (size <= 0 || size > maximum || !Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var url) ||
                !AllowedDownload(url) || url.Host != "github.com" || url.Query.Length != 0 ||
                url.AbsolutePath != $"/aesgalexis/habitat/releases/download/{tag}/{expected}") return null;
            return new(expected, url, size);
        }
        var package = Find(name, MaxPackageBytes);
        var checksum = Find(name + ".sha256", 4096);
        return package is null || checksum is null ? null : new(version.ToString(), _kind, package, checksum);
    }

    internal async Task<PreparedAppUpdate> PrepareAsync(AppUpdate release, IProgress<int> progress, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        token = timeout.Token;
        // Re-resolve the release before downloading; callers cannot supply a URL
        // or version which bypasses the publication checks above.
        if (await CheckAsync(token) is not { } latest || latest != release) throw new InvalidDataException("The published update changed. Try again.");
        using var checksumResponse = await GetAsync(release.Checksum.Url, token);
        checksumResponse.EnsureSuccessStatusCode();
        var checksumBytes = await ReadSmallAsync(checksumResponse, 4096, token);
        var checksumText = System.Text.Encoding.UTF8.GetString(checksumBytes).Trim();
        var match = Regex.Match(checksumText, @"^([a-fA-F0-9]{64})[ \t]+\*?" + Regex.Escape(release.Package.Name) + "$", RegexOptions.CultureInvariant);
        if (!match.Success) throw new InvalidDataException("The update checksum is invalid.");
        var expectedHash = match.Groups[1].Value;
        EnsureNoLinks(_cache);
        var directory = Path.Combine(_cache, "update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var packagePath = Path.Combine(directory, release.Package.Name);
        try
        {
            using (var response = await GetAsync(release.Package.Url, token))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is { } length && length != release.Package.Size)
                    throw new InvalidDataException("The update size changed.");
                await using var source = await response.Content.ReadAsStreamAsync(token);
                await using var target = new FileStream(packagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long total = 0;
                var lastPercent = -1;
                int count;
                while ((count = await source.ReadAsync(buffer, token)) != 0)
                {
                    total += count;
                    if (total > release.Package.Size || total > MaxPackageBytes) throw new InvalidDataException("The update is too large.");
                    hash.AppendData(buffer, 0, count);
                    await target.WriteAsync(buffer.AsMemory(0, count), token);
                    var percent = (int)(total * 100 / release.Package.Size);
                    if (percent != lastPercent) { progress.Report(percent); lastPercent = percent; }
                }
                if (total != release.Package.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The update failed its SHA-256 check.");
            }
            string? extracted = null;
            if (release.Kind == UpdatePackageKind.Zip)
            {
                extracted = Path.Combine(directory, "package");
                await Task.Run(() => ExtractPackage(packagePath, extracted, token), token);
                using var metadata = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(extracted, "release.json"), token));
                if (metadata.RootElement.GetProperty("version").GetString() != release.Version ||
                    metadata.RootElement.GetProperty("runtime").GetString() != "win-x64") throw new InvalidDataException("The update metadata does not match.");
                await VerifyPayloadAsync(Path.Combine(extracted, "artifacts", "staged", "app"), token);
            }
            return new(release, directory, packagePath, expectedHash, extracted);
        }
        catch
        {
            // This unique directory contains only this download and is always a
            // direct child of our explicit cache; never touch installed files.
            EnsureNoLinks(directory);
            Directory.Delete(directory, true);
            throw;
        }
    }

    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (!AllowedDownload(uri)) throw new InvalidDataException("The update address is not allowed.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("HABITAT-Updater/1.0");
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var next = response.Headers.Location;
            response.Dispose();
            if (next is null) throw new InvalidDataException("The update redirect is missing.");
            uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
        }
        throw new InvalidDataException("Too many update redirects.");
    }

    private static async Task<byte[]> ReadSmallAsync(HttpResponseMessage response, int maximum, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("The update response is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + count > maximum) throw new InvalidDataException("The update response is too large.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    internal static void EnsureNoLinks(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(path)); directory is not null; directory = directory.Parent)
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Update paths cannot contain filesystem links.");
    }

    private static string ChildPath(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Contains('\\') || relative.Contains(':') || relative.StartsWith('/') ||
            relative.Split('/').Any(part => part is ".." or "." || part.TrimEnd(' ', '.') != part)) throw new InvalidDataException("The update contains an invalid path.");
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The update path escapes its directory.");
        return path;
    }

    internal static void ExtractPackage(string package, string destination, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(package);
        if (archive.Entries.Count > 20000) throw new InvalidDataException("The update has too many files.");
        long expanded = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var relative = entry.FullName.TrimEnd('/');
            var path = ChildPath(destination, relative);
            if (!names.Add(path) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("The update contains duplicate paths or filesystem links.");
            expanded += entry.Length;
            if (expanded > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("The extracted update is too large.");
            if (entry.FullName.EndsWith('/')) Directory.CreateDirectory(path);
            else { Directory.CreateDirectory(Path.GetDirectoryName(path)!); entry.ExtractToFile(path); }
        }
    }

    internal static async Task VerifyPayloadAsync(string directory, CancellationToken token)
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(directory, "build-manifest.json"), token));
        if (manifest.RootElement.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("The update manifest is invalid.");
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = ChildPath(directory, entry.GetProperty("path").GetString()!);
            if (!expected.Add(path) || new FileInfo(path).Length != entry.GetProperty("length").GetInt64())
                throw new InvalidDataException("The update manifest does not match its files.");
            await using var stream = File.OpenRead(path);
            if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(entry.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("An update file failed verification.");
        }
        foreach (var name in new[] { "HABITAT.exe", "HABITAT.dll", "Habitat.Core.dll", "HABITAT.Hotkey.exe", "HABITAT.Hotkey.dll", "Habitat.Activation.dll" })
            if (!expected.Contains(Path.Combine(directory, name))) throw new InvalidDataException("The update is missing application files.");
        if (Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count() != expected.Count + 1)
            throw new InvalidDataException("The update includes unexpected application files.");
    }

    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
