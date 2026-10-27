using Habitat.Services;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LegacyClient = Habitat.LegacyUpdates.ReleaseUpdateClient;
using LegacyKind = Habitat.LegacyUpdates.UpdatePackageKind;

internal static class UpdateMigrationChecks
{
    internal static async Task RunAsync(string root, Action<bool, string> check, string[] args)
    {
        var migration = Path.Combine(root, "migration with spaces");
        Directory.CreateDirectory(migration);
        var windows = Package(windowsPaths: true);
        await OldRejectsWindowsZip(windows, Path.Combine(migration, "old-download"), check);
        var canonical = Package(windowsPaths: false);
        using (var http = new HttpClient(new DownloadFixture(canonical)))
        using (var old = new LegacyClient(new(0, 16, 1), LegacyKind.Zip, Path.Combine(migration, "old-canonical"), http))
        {
            var offered = (await old.CheckAsync(default))!;
            var prepared = await old.PrepareAsync(offered, new Progress(), default);
            check(File.Exists(Path.Combine(prepared.ExtractedDirectory!, "artifacts/staged/app/HABITAT.exe")),
                "The old 0.16.1-style client can prepare the same payload when ZIP entries use forward slashes.");
        }
        var officialIndex = Array.IndexOf(args, "--official-package");
        if (officialIndex >= 0)
        {
            var official = Path.GetFullPath(args[officialIndex + 1]);
            var expected = File.ReadAllText(official + ".sha256").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
            var bytes = await File.ReadAllBytesAsync(official);
            check(Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase),
                "The migration probe uses the exact checksum-verified official ZIP.");
            await OldRejectsWindowsZip(bytes, Path.Combine(migration, "old-official"), check);
            if (!args.Contains("--legacy-probe-only"))
            {
                using var officialHttp = new HttpClient(new DownloadFixture(bytes));
                using var fixedClient = new ReleaseUpdateClient(new(0, 16, 1), UpdatePackageKind.Zip, Path.Combine(migration, "fixed-official"), officialHttp);
                var prepared = await fixedClient.PrepareAsync((await fixedClient.CheckAsync(default))!, new Progress(), default);
                check(File.Exists(Path.Combine(prepared.ExtractedDirectory!, "artifacts/staged/app/updater/install-release-update.ps1")),
                    "The fixed client downloads, hashes, extracts and verifies the exact official 0.16.4 Windows ZIP.");
            }
        }
        if (args.Contains("--legacy-probe-only")) return;
        using (var http = new HttpClient(new DownloadFixture(windows)))
        using (var current = new ReleaseUpdateClient(new(0, 16, 1), UpdatePackageKind.Zip, Path.Combine(migration, "fixed-windows"), http))
        {
            var prepared = await current.PrepareAsync((await current.CheckAsync(default))!, new Progress(), default);
            check(File.Exists(Path.Combine(prepared.ExtractedDirectory!, "artifacts/staged/app/HABITAT.exe")),
                "The fixed reader accepts bounded Windows ZIP separators after verifying SHA-256.");
        }
        var duplicate = Path.Combine(migration, "mixed-separator-duplicate.zip");
        using (var stream = File.Create(duplicate))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            archive.CreateEntry("folder\\file.txt"); archive.CreateEntry("folder/file.txt");
        }
        var duplicateRejected = false;
        try { ReleaseUpdateClient.ExtractPackage(duplicate, Path.Combine(migration, "duplicates"), default); }
        catch (InvalidDataException) { duplicateRejected = true; }
        check(duplicateRejected, "Canonicalizing ZIP separators still rejects mixed-separator aliases and duplicate files.");
        await UpdateDiagnosticsChecks.RunAsync(root, check);
        if (OperatingSystem.IsWindows())
        {
            // Model C:\\proyectos on a plain local directory: the historical
            // PowerShell modules reject OneDrive Cloud Files metadata. Also keep
            // native .NET Framework executable paths below MAX_PATH. This is an
            // explicit synthetic artifacts root, never a real profile or app.
            var nativeRoot = Path.Combine(Path.GetTempPath(), "HabitatUpdateChecks", "artifacts", Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(nativeRoot);
            await LegacyUpdateLifecycleChecks.RunAsync(nativeRoot, check);
        }
    }

    private static async Task OldRejectsWindowsZip(byte[] package, string cache, Action<bool, string> check)
    {
        var fixture = new DownloadFixture(package);
        using var http = new HttpClient(fixture);
        using var old = new LegacyClient(new(0, 16, 1), LegacyKind.Zip, cache, http);
        var progress = new Progress();
        var offered = (await old.CheckAsync(default))!;
        try { await old.PrepareAsync(offered, progress, default); throw new Exception("The pinned legacy client unexpectedly accepted Windows ZIP entries."); }
        catch (InvalidDataException exception)
        {
            check(exception.Message == "The update contains an invalid path." && progress.Percent == 100 && fixture.Downloads == 1,
                "REPRODUCED: the old client downloads and hashes the Windows ZIP, then fails during extraction before launching any helper.");
            Console.WriteLine("CONFIRMED legacy extraction failure after verified download: " + exception.Message);
            check(!Directory.EnumerateFiles(cache, "request.json", SearchOption.AllDirectories).Any(),
                "An extraction failure cannot close the old application, activate a release or create helper diagnostics.");
        }
    }

    internal static byte[] Package(bool windowsPaths, byte[]? executable = null, bool startupFailure = false, int port = 0)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            var files = new Dictionary<string, byte[]>();
            foreach (var name in new[] { "HABITAT.exe", "HABITAT.dll", "Habitat.Core.dll", "HABITAT.Hotkey.exe", "HABITAT.Hotkey.dll", "Habitat.Activation.dll" })
                files[name] = name == "HABITAT.exe" && executable is not null ? executable : "synthetic payload"u8.ToArray();
            files["port"] = Encoding.UTF8.GetBytes(port.ToString());
            if (startupFailure) files["fail-startup"] = [];
            foreach (var (name, content) in files) Write("artifacts/staged/app/" + name, content);
            Write("artifacts/staged/app/build-manifest.json", JsonSerializer.SerializeToUtf8Bytes(new { version = 1,
                files = files.Select(file => new { path = file.Key, length = file.Value.Length, sha256 = Convert.ToHexString(SHA256.HashData(file.Value)) }) }));
            Write("release.json", "{\"version\":\"0.16.4\",\"runtime\":\"win-x64\"}"u8.ToArray());
            Write("extension/manifest.json", "{}"u8.ToArray());
            Write("vscode-extension/synthetic.vsix", "synthetic"u8.ToArray());
            void Write(string name, byte[] bytes)
            {
                using var stream = zip.CreateEntry(windowsPaths ? name.Replace('/', '\\') : name).Open();
                stream.Write(bytes);
            }
        }
        return output.ToArray();
    }

    internal sealed class Progress : IProgress<int>
    {
        public int Percent;
        public void Report(int value) => Percent = value;
    }

    internal sealed class DownloadFixture(byte[] package) : HttpMessageHandler
    {
        internal int Downloads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            const string name = "HABITAT-0.16.4-win-x64.zip";
            var checksum = Encoding.UTF8.GetBytes(Convert.ToHexString(SHA256.HashData(package)) + " *" + name + "\n");
            byte[] content;
            if (request.RequestUri == ReleaseUpdateClient.LatestRelease)
                content = JsonSerializer.SerializeToUtf8Bytes(new { tag_name = "v0.16.4", draft = false, prerelease = false,
                    assets = new[] { Asset(name, package.Length), Asset(name + ".sha256", checksum.Length) } });
            else if (request.RequestUri!.AbsolutePath.EndsWith(".sha256")) content = checksum;
            else { Downloads++; content = package; }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
            static object Asset(string name, int size) => new { name, size, state = "uploaded",
                browser_download_url = "https://github.com/aesgalexis/habitat/releases/download/v0.16.4/" + name };
        }
    }
}
