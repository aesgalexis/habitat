using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Habitat.Services;

internal sealed record UpdateDownloadProgress(long Received, long Total)
{
    internal double Percent => Total > 0 ? Math.Clamp(100d * Received / Total, 0, 100) : 0;
}

internal sealed record DownloadedUpdate(UpdatePackage Package, string File, string Sha256);

internal sealed class AppUpdateDownload : IDisposable
{
    internal const long MaximumBytes = 512L * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    internal AppUpdateDownload(HttpClient? http = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
    }

    internal async Task<DownloadedUpdate> DownloadAsync(UpdatePackage package, string cacheRoot,
        IProgress<UpdateDownloadProgress>? progress, CancellationToken token)
    {
        if (!Regex.IsMatch(package.Version, @"^\d+\.\d+\.\d+$") ||
            package.Tag != "v" + package.Version && package.Tag != package.Version ||
            package.Name != $"HABITAT-{package.Version}-x64-setup.exe" && package.Name != $"HABITAT-{package.Version}-win-x64.zip" ||
            package.Size <= 0 || package.Size > MaximumBytes) throw new InvalidDataException("Invalid update package.");
        var expected = $"https://github.com/aesgalexis/habitat/releases/download/{package.Tag}/{package.Name}";
        if (package.Download.AbsoluteUri != expected || package.Checksum.AbsoluteUri != expected + ".sha256")
            throw new InvalidDataException("The update is not an official HABITAT asset.");

        Directory.CreateDirectory(cacheRoot);
        var directory = Path.Combine(Path.GetFullPath(cacheRoot), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, package.Name);
        try
        {
            using var checksumResponse = await SendAsync(package.Checksum, token).ConfigureAwait(false);
            if (checksumResponse.Content.Headers.ContentLength is > 4096) throw new InvalidDataException("Oversized checksum.");
            await using var checksumStream = await checksumResponse.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var smallBuffer = new byte[4097];
            var count = 0;
            while (count < smallBuffer.Length)
            {
                var read = await checksumStream.ReadAsync(smallBuffer.AsMemory(count), token).ConfigureAwait(false);
                if (read == 0) break;
                count += read;
            }
            if (count > 4096) throw new InvalidDataException("Oversized checksum.");
            var checksum = System.Text.Encoding.UTF8.GetString(smallBuffer, 0, count).Trim('\uFEFF', ' ', '\r', '\n', '\t');
            var match = Regex.Match(checksum, @"^([a-fA-F0-9]{64})[ \t]+\*?" + Regex.Escape(package.Name) + @"$", RegexOptions.CultureInvariant);
            if (!match.Success) throw new InvalidDataException("Invalid update checksum.");
            var expectedHash = Convert.FromHexString(match.Groups[1].Value);

            using var response = await SendAsync(package.Download, token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is { } length && length != package.Size)
                throw new InvalidDataException("Unexpected update length.");
            await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(destination + ".partial", FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                long received = 0;
                var lastProgress = Environment.TickCount64;
                progress?.Report(new(0, package.Size));
                while (true)
                {
                    var read = await source.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (read == 0) break;
                    received += read;
                    if (received > package.Size || received > MaximumBytes) throw new InvalidDataException("Oversized update.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    if (received == package.Size || Environment.TickCount64 - lastProgress >= 100)
                    {
                        progress?.Report(new(received, package.Size));
                        lastProgress = Environment.TickCount64;
                    }
                }
                if (received != package.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), expectedHash))
                    throw new InvalidDataException("The update failed integrity verification.");
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            File.Move(destination + ".partial", destination);
            return new(package, destination, Convert.ToHexString(expectedHash));
        }
        catch
        {
            // Only the fresh GUID directory created by this invocation is retired.
            try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("HABITAT-Updater/1.0");
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        try { response.EnsureSuccessStatusCode(); return response; }
        catch { response.Dispose(); throw; }
    }

    public void Dispose() { if (_ownsHttp) _http.Dispose(); }

    internal static void Discard(DownloadedUpdate update, string cacheRoot)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(update.File));
        var parent = directory is null ? null : Path.GetDirectoryName(directory);
        if (!string.Equals(parent, Path.GetFullPath(cacheRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) return;
        try { Directory.Delete(directory!, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
