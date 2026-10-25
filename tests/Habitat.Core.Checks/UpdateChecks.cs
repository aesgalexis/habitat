using Habitat.Services;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class UpdateChecks
{
    internal static async Task RunAsync(string root, Action<bool, string> check)
    {
        var cache = Path.Combine(root, "updates");
        foreach (var invalid in new[] { "1.2", "01.2.3", "1.2.3-beta", "v1.2.3", "65536.0.0", "1.2.3.4", "-1.2.3" })
            check(!ReleaseUpdateClient.TryVersion(invalid, out _), "Invalid release versions are excluded: " + invalid);
        check(ReleaseUpdateClient.TryVersion("0.11.0", out var version) && version > new Version(0, 10, 0), "Versions compare numerically.");
        foreach (var url in new[] { "http://github.com/file", "https://github.com.evil.test/file", "https://user@github.com/file", "https://github.com:999/file", "https://github.com/file#fragment" })
            check(!ReleaseUpdateClient.AllowedDownload(new Uri(url)), "Unsafe download URLs are refused: " + url);

        var fixture = new UpdateFixture();
        using var http = new HttpClient(fixture);
        using var client = new ReleaseUpdateClient(new(0, 10, 0, 0), UpdatePackageKind.Installer, cache, http);
        foreach (var old in new[] { "0.9.9", "0.10.0", "0.10.0-beta", "01.11.0" })
        {
            fixture.Version = old;
            check(await client.CheckAsync(default) is null, "Equal, older and nonstable versions produce no update.");
        }
        fixture.Version = "0.11.0";
        fixture.Draft = true;
        check(await client.CheckAsync(default) is null, "Draft releases cannot be installed.");
        fixture.Draft = false; fixture.Prerelease = true;
        check(await client.CheckAsync(default) is null, "Prereleases cannot be installed.");
        fixture.Prerelease = false; fixture.MissingHash = true;
        check(await client.CheckAsync(default) is null, "A release without its checksum is not ready.");
        fixture.MissingHash = false; fixture.State = "new";
        check(await client.CheckAsync(default) is null, "Incomplete uploads are not ready.");
        fixture.State = "uploaded"; fixture.AssetHost = "evil.test";
        check(await client.CheckAsync(default) is null, "Metadata cannot redirect the installer to another repository or host.");
        fixture.AssetHost = "github.com";
        var release = (await client.CheckAsync(default))!;
        check(release.Version == "0.11.0" && release.Kind == UpdatePackageKind.Installer, "A complete newer stable installer is offered.");
        var progress = new UpdateProgress();
        var prepared = await client.PrepareAsync(release, progress, default);
        check(File.ReadAllBytes(prepared.PackagePath).SequenceEqual(fixture.Package) && progress.Percent == 100,
            "Downloads are complete and verified before an installer can be launched.");
        check(fixture.Addresses.All(uri => uri.Scheme == "https") && fixture.RequestsWithCredentials == 0,
            "Checks and downloads do not send credentials or user content.");
        fixture.CorruptHash = true;
        await Reject(() => client.PrepareAsync(release, progress, default), "A checksum mismatch rejects the package.");
        fixture.CorruptHash = false;
        fixture.Redirect = new Uri("http://evil.test/package");
        var before = fixture.Addresses.Count;
        await Reject(() => client.PrepareAsync(release, progress, default), "Unsafe redirects are rejected before another request.");
        check(fixture.Addresses.Skip(before).All(uri => uri.Host != "evil.test"), "Unsafe redirect hosts never receive a request.");
        fixture.Redirect = null;
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            await Reject(() => client.PrepareAsync(release, progress, canceled.Token), "Canceled updates cannot create an installable package.");
        }
        fixture.Version = "0.12.0";
        await Reject(() => client.PrepareAsync(release, progress, default), "A changed release is rechecked before download.");
        fixture.Version = "0.11.0";
        fixture.Offline = true;
        await Reject(() => client.CheckAsync(default), "A network failure remains recoverable.");
        fixture.Offline = false; fixture.NotFound = true;
        check(await client.CheckAsync(default) is null, "A repository without a published release has no update.");
        fixture.NotFound = false;

        var zipFixture = new UpdateFixture { Zip = true, Package = ZipPackage() };
        using var zipHttp = new HttpClient(zipFixture);
        using var zipClient = new ReleaseUpdateClient(new(0, 10, 0), UpdatePackageKind.Zip, cache, zipHttp);
        var zipRelease = (await zipClient.CheckAsync(default))!;
        var zipPrepared = await zipClient.PrepareAsync(zipRelease, progress, default);
        check(File.Exists(Path.Combine(zipPrepared.ExtractedDirectory!, "artifacts/staged/app/HABITAT.exe")),
            "ZIP updates verify their complete manifest before installation.");
        zipFixture.Package = ZipPackage(damaged: true);
        zipRelease = (await zipClient.CheckAsync(default))!;
        await Reject(() => zipClient.PrepareAsync(zipRelease, progress, default), "A manifest mismatch rejects a correctly hashed ZIP.");
        zipFixture.Package = ZipPackage(version: "0.12.0");
        zipRelease = (await zipClient.CheckAsync(default))!;
        await Reject(() => zipClient.PrepareAsync(zipRelease, progress, default), "Package version mismatches are rejected.");
        foreach (var bad in new[] { "../escape.txt", "/absolute.txt", "folder/../../escape.txt", "folder\\escape.txt", "C:/escape.txt", "trailing./file" })
        {
            var archive = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
            using (var stream = File.Create(archive))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry(bad).Open())) writer.Write("synthetic");
            var destination = Path.Combine(root, "extract-" + Guid.NewGuid().ToString("N"));
            await Reject(() => Task.Run(() => ReleaseUpdateClient.ExtractPackage(archive, destination, default)), "Archive path traversal is rejected: " + bad);
        }
        var exeRoot = Path.Combine(root, "EXE installation");
        Directory.CreateDirectory(exeRoot);
        File.WriteAllText(Path.Combine(exeRoot, "HABITAT.exe"), "synthetic");
        File.WriteAllText(Path.Combine(exeRoot, "unins000.exe"), "synthetic");
        File.WriteAllText(Path.Combine(exeRoot, "release.json"), "{}");
        check(UpdateInstallation.Find(exeRoot)?.Kind == UpdatePackageKind.Installer, "EXE installations retain their installation format.");
        var zipRoot = Path.Combine(root, "ZIP installation");
        var active = Path.Combine(zipRoot, "artifacts/releases/release-" + new string('a', 32));
        Directory.CreateDirectory(active);
        File.WriteAllText(Path.Combine(active, "HABITAT.exe"), "synthetic");
        File.WriteAllText(Path.Combine(zipRoot, "HABITAT.vbs"), "synthetic");
        File.WriteAllText(Path.Combine(zipRoot, "artifacts/current-release.txt"), "releases/release-" + new string('a', 32));
        check(UpdateInstallation.Find(active) is { Kind: UpdatePackageKind.Zip } found && found.Root == zipRoot,
            "ZIP and active development releases resolve their stable root.");
        check(UpdateInstallation.Find(Path.Combine(root, "uninstalled")) is null, "Loose build outputs cannot update another installation.");

        async Task Reject(Func<Task> action, string message)
        {
            var rejected = false;
            try { await action(); }
            catch (Exception exception) when (exception is InvalidDataException or HttpRequestException or OperationCanceledException) { rejected = true; }
            check(rejected, message);
        }
    }

    private static byte[] ZipPackage(bool damaged = false, string version = "0.11.0")
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var names = new[] { "HABITAT.exe", "HABITAT.dll", "Habitat.Core.dll", "HABITAT.Hotkey.exe", "HABITAT.Hotkey.dll", "Habitat.Activation.dll" };
            var bytes = Encoding.UTF8.GetBytes("synthetic application file");
            foreach (var name in names) Write("artifacts/staged/app/" + name, bytes);
            Write("artifacts/staged/app/build-manifest.json", JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 1, files = names.Select(name => new { path = name, length = bytes.Length,
                    sha256 = damaged ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(bytes)) })
            }));
            Write("release.json", JsonSerializer.SerializeToUtf8Bytes(new { version, runtime = "win-x64" }));
            Write("extension/manifest.json", "{}"u8.ToArray());
            Write("vscode-extension/synthetic.vsix", "synthetic"u8.ToArray());
            void Write(string name, byte[] content) { using var target = zip.CreateEntry(name).Open(); target.Write(content); }
        }
        return stream.ToArray();
    }

    private sealed class UpdateProgress : IProgress<int>
    {
        public int Percent;
        public void Report(int value) => Percent = value;
    }

    private sealed class UpdateFixture : HttpMessageHandler
    {
        public string Version = "0.11.0", State = "uploaded", AssetHost = "github.com";
        public bool Draft, Prerelease, MissingHash, CorruptHash, Offline, NotFound, Zip;
        public Uri? Redirect;
        public byte[] Package = "synthetic installer"u8.ToArray();
        public readonly List<Uri> Addresses = [];
        public int RequestsWithCredentials;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Addresses.Add(request.RequestUri!);
            if (request.Headers.Authorization is not null || request.Content is not null) RequestsWithCredentials++;
            if (Offline) throw new HttpRequestException("Synthetic offline failure");
            var name = Zip ? $"HABITAT-{Version}-win-x64.zip" : $"HABITAT-{Version}-x64-setup.exe";
            var checksum = Encoding.UTF8.GetBytes((CorruptHash ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(Package))) + " *" + name + "\n");
            HttpResponseMessage result;
            if (request.RequestUri == ReleaseUpdateClient.LatestRelease)
            {
                if (NotFound) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                var assets = new[] { new { name, size = Package.Length, state = State, browser_download_url = $"https://{AssetHost}/aesgalexis/habitat/releases/download/v{Version}/{name}" },
                    new { name = name + ".sha256", size = checksum.Length, state = State, browser_download_url = $"https://{AssetHost}/aesgalexis/habitat/releases/download/v{Version}/{name}.sha256" } };
                result = new(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new
                { tag_name = "v" + Version, draft = Draft, prerelease = Prerelease, assets = MissingHash ? assets.Take(1) : assets })) };
            }
            else if (request.RequestUri!.AbsolutePath.EndsWith(".sha256")) result = new(HttpStatusCode.OK) { Content = new ByteArrayContent(checksum) };
            else if (Redirect is not null) { result = new(HttpStatusCode.Redirect); result.Headers.Location = Redirect; }
            else result = new(HttpStatusCode.OK) { Content = new ByteArrayContent(Package) };
            return Task.FromResult(result);
        }
    }
}
