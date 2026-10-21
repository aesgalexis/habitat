using Habitat.Services;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

internal static class UpdateDownloadChecks
{
    internal static async Task RunAsync(string root, Action<bool, string> check)
    {
        const string version = "0.15.0";
        var bytes = new byte[240_000];
        Random.Shared.NextBytes(bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var name = $"HABITAT-{version}-x64-setup.exe";
        var prefix = $"https://github.com/aesgalexis/habitat/releases/download/v{version}/";
        var package = new UpdatePackage(version, "v" + version, name, bytes.Length, new(prefix + name), new(prefix + name + ".sha256"));
        var mode = "ok";
        var requests = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            requests++;
            check(request.Headers.Authorization is null && request.RequestUri!.Host == "github.com", "Update downloads send no credentials and use the official host.");
            if (mode == "offline") throw new HttpRequestException("Synthetic network failure");
            if (mode == "cancel") { started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            if (request.RequestUri!.AbsolutePath.EndsWith(".sha256"))
                return new(HttpStatusCode.OK) { Content = new StringContent(mode == "wrong-name" ? hash + "  other.exe" :
                    mode == "big-checksum" ? new string('x', 5000) : (mode == "bad-hash" ? new string('0', 64) : hash) + "  " + name + "\n") };
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(mode == "truncated" ? bytes[..^1] : bytes) };
        }));
        using var downloader = new AppUpdateDownload(http);
        var cache = Path.Combine(root, "updates");
        var progress = new List<UpdateDownloadProgress>();
        var downloaded = await downloader.DownloadAsync(package, cache, new InlineProgress(progress.Add), default);
        check(File.ReadAllBytes(downloaded.File).SequenceEqual(bytes) && downloaded.Sha256 == hash &&
            !File.Exists(downloaded.File + ".partial") && progress[0].Received == 0 && progress[^1].Percent == 100,
            "A streamed download reaches 100%, verifies its exact checksum and only then exposes the completed package.");
        var priorFiles = Directory.GetFiles(cache, "*", SearchOption.AllDirectories).Order().ToArray();
        foreach (var failure in new[] { "offline", "wrong-name", "big-checksum", "bad-hash", "truncated" })
        {
            mode = failure;
            var failed = false;
            try { await downloader.DownloadAsync(package, cache, null, default); }
            catch (Exception exception) when (exception is InvalidDataException or HttpRequestException) { failed = true; }
            check(failed && Directory.GetFiles(cache, "*", SearchOption.AllDirectories).Order().SequenceEqual(priorFiles),
                "Failed updates remove partial files and preserve earlier verified downloads: " + failure);
        }
        mode = "cancel";
        using (var cancellation = new CancellationTokenSource())
        {
            var pending = downloader.DownloadAsync(package, cache, null, cancellation.Token);
            await started.Task;
            cancellation.Cancel();
            var cancelled = false;
            try { await pending; } catch (OperationCanceledException) { cancelled = true; }
            check(cancelled && Directory.GetFiles(cache, "*", SearchOption.AllDirectories).Order().SequenceEqual(priorFiles),
                "Cancellation drains the download and leaves no unfinished package.");
        }
        var previousRequests = requests;
        foreach (var invalid in new[] { package with { Download = new("https://other.invalid/setup.exe") },
            package with { Checksum = new("http://github.com/checksum") }, package with { Name = "../setup.exe" },
            package with { Tag = "v0.14.0" }, package with { Size = AppUpdateDownload.MaximumBytes + 1 } })
        {
            var rejected = false;
            try { await downloader.DownloadAsync(invalid, cache, null, default); } catch (InvalidDataException) { rejected = true; }
            check(rejected && requests == previousRequests, "Untrusted asset metadata is rejected before network or disk download.");
        }

        using var release = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            tag_name = "v" + version, draft = false, prerelease = false,
            assets = new[] { new { name, state = "uploaded", size = bytes.Length, browser_download_url = "https://untrusted.invalid/execute" },
                new { name = name + ".sha256", state = "uploaded", size = 100, browser_download_url = "https://untrusted.invalid/hash" } }
        }));
        var parsed = AppUpdateService.ParseRelease(release.RootElement, new Version(0, 14, 3));
        check(parsed?.Installer?.Download == package.Download && parsed.Installer.Checksum == package.Checksum && parsed.Zip is null,
            "Executable and checksum URLs are constructed from the fixed official release, ignoring untrusted supplied URLs.");

        var drafts = new ComposerDrafts();
        drafts.Edit("chatgpt", "Borrador sin enviar — 日本語");
        var accepted = drafts.Capture("chatgpt");
        drafts.Accept(accepted);
        drafts.Edit("google-search", "Another shared draft");
        var stateFile = Path.Combine(root, "update-drafts.json");
        new UpdateRestartState(1, "chatgpt", drafts.SaveForRestart()).Save(stateFile);
        var restored = new ComposerDrafts();
        var state = UpdateRestartState.Consume(stateFile)!;
        restored.RestoreAfterRestart(state.Drafts);
        check(state.Destination == "chatgpt" && restored.Text("chatgpt") == "Borrador sin enviar — 日本語" &&
            restored.Text("translate") == "Another shared draft" && !File.Exists(stateFile),
            "An update restart preserves shared and destination-specific drafts verbatim and consumes the recovery file once.");
        restored.Clear(accepted);
        check(restored.Text("chatgpt") == "Borrador sin enviar — 日本語", "Pre-restart submissions cannot clear a recovered draft.");

        await UpdateInstallChecks.RunAsync(root, downloaded, check);
        AppUpdateDownload.Discard(downloaded, Path.Combine(root, "other-root"));
        check(File.Exists(downloaded.File), "Cancelling an update cannot retire files outside its owned cache root.");
        AppUpdateDownload.Discard(downloaded, cache);
        check(!Directory.Exists(Path.GetDirectoryName(downloaded.File)), "Cancellation after verification removes only its owned staged download and helper files.");
    }

    private sealed class InlineProgress(Action<UpdateDownloadProgress> report) : IProgress<UpdateDownloadProgress>
    { public void Report(UpdateDownloadProgress value) => report(value); }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
}
