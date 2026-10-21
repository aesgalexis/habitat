using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Habitat.Services;

internal sealed record AppUpdate(string Version, Uri ReleasePage)
{
    internal UpdatePackage? Installer { get; init; }
    internal UpdatePackage? Zip { get; init; }
}

internal sealed record UpdatePackage(string Version, string Tag, string Name, long Size, Uri Download, Uri Checksum);

// Discovery is read-only; downloading and installing require the update button.
internal sealed class AppUpdateService : IDisposable
{
    internal static readonly Uri LatestRelease = new("https://api.github.com/repos/aesgalexis/habitat/releases/latest");
    internal static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Version _currentVersion;
    private readonly Action<Exception>? _reportError;
    private readonly SemaphoreSlim _gate = new(1, 1);
    internal AppUpdate? Available { get; private set; }
    internal DateTimeOffset NextCheck { get; private set; } = DateTimeOffset.MinValue;

    internal AppUpdateService(HttpClient? http = null, Version? currentVersion = null, Action<Exception>? reportError = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 1024 * 1024 };
        var installed = currentVersion ?? typeof(AppUpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
        _currentVersion = new Version(installed.Major, installed.Minor, Math.Max(0, installed.Build));
        _reportError = reportError;
    }

    internal async Task CheckIfDueAsync(DateTimeOffset now, CancellationToken token = default, bool force = false)
    {
        if (force) await _gate.WaitAsync(token);
        else if (!await _gate.WaitAsync(0, token)) return;
        try
        {
            if (!force && now < NextCheck) return;
            NextCheck = now + CheckInterval;
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestRelease);
            request.Headers.UserAgent.ParseAdd($"HABITAT/{_currentVersion}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
            using var response = await _http.SendAsync(request, token);
            if (response.StatusCode == HttpStatusCode.NotFound) { Available = null; return; }
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var update = ParseRelease(document.RootElement, _currentVersion);
            token.ThrowIfCancellationRequested();
            Available = update;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            // Keep an existing notice during outages, without surfacing errors in the composer.
            NextCheck = now + RetryInterval;
            _reportError?.Invoke(exception);
        }
        finally { _gate.Release(); }
    }

    internal static AppUpdate? ParseRelease(JsonElement release, Version installed)
    {
        if (release.ValueKind != JsonValueKind.Object ||
            !release.TryGetProperty("draft", out var draft) || draft.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !release.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String)
            throw new JsonException("Incomplete release metadata.");
        if (draft.GetBoolean() || prerelease.GetBoolean()) return null;
        var match = Regex.Match(tag.GetString() ?? "", @"^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch)) return null;
        var version = new Version(major, minor, patch);
        if (version <= new Version(installed.Major, installed.Minor, Math.Max(0, installed.Build))) return null;
        // Ignore source-only releases or a release whose Windows assets are still uploading.
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new JsonException("Missing release assets.");
        var number = version.ToString(3);
        var ready = assets.EnumerateArray().Any(asset => asset.ValueKind == JsonValueKind.Object &&
            asset.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String &&
            (name.GetString() == $"HABITAT-{number}-x64-setup.exe" || name.GetString() == $"HABITAT-{number}-win-x64.zip") &&
            asset.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String && state.GetString() == "uploaded" &&
            asset.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out var length) && length > 0);
        if (!ready) return null;
        var releaseTag = tag.GetString()!;
        UpdatePackage? Package(string suffix)
        {
            var name = $"HABITAT-{number}-{suffix}";
            long? SizeOf(string file) => assets.EnumerateArray().Where(asset => asset.ValueKind == JsonValueKind.Object &&
                asset.TryGetProperty("name", out var item) && item.ValueKind == JsonValueKind.String && item.GetString() == file &&
                asset.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String && state.GetString() == "uploaded" &&
                asset.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out var length) && length > 0)
                .Select(asset => (long?)asset.GetProperty("size").GetInt64()).FirstOrDefault();
            var bytes = SizeOf(name);
            if (bytes is null || bytes > AppUpdateDownload.MaximumBytes || SizeOf(name + ".sha256") is not (> 0 and <= 4096)) return null;
            // Construct both links. Neither html_url nor browser_download_url can
            // redirect the executable download to an unrelated release or host.
            var prefix = $"https://github.com/aesgalexis/habitat/releases/download/{Uri.EscapeDataString(releaseTag)}/";
            return new(number, releaseTag, name, bytes.Value, new Uri(prefix + name), new Uri(prefix + name + ".sha256"));
        }
        return new(number, new Uri($"https://github.com/aesgalexis/habitat/releases/tag/{Uri.EscapeDataString(releaseTag)}"))
        {
            Installer = Package("x64-setup.exe"), Zip = Package("win-x64.zip")
        };
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
        _gate.Dispose();
    }
}
