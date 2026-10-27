using Habitat.Services;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

internal static class UpdateDiagnosticsChecks
{
    internal static async Task RunAsync(string root, Action<bool, string> check)
    {
        const string privateText = "Get-FileHash failed C:\\Users\\SyntheticPrivate\\draft.txt\nsynthetic@example.invalid token=TOP_SECRET https://example.invalid/?secret=PRIVATE\n\"password\": \"sensitive phrase\"";
        var redacted = UpdateDiagnosticLog.Redact(privateText);
        check(redacted.Contains("Get-FileHash") && !redacted.Contains("SyntheticPrivate") && !redacted.Contains("TOP_SECRET") &&
            !redacted.Contains("synthetic@example") && !redacted.Contains("example.invalid") && !redacted.Contains("sensitive") && !redacted.Contains("phrase"),
            "Updater diagnostics redact paths, email addresses, URLs and credential values while retaining useful command names.");
        var cache = Path.Combine(root, "preparation diagnostics");
        using (var http = new HttpClient(new Unavailable()))
        using (var client = new ReleaseUpdateClient(new(0, 16, 1), UpdatePackageKind.Zip, cache, http))
        {
            var failed = false;
            try { await client.PrepareAsync(new("0.16.4", UpdatePackageKind.Zip,
                new("package", new("https://github.com/unused"), 1), new("checksum", new("https://github.com/unused.sha256"), 1)), new UpdateMigrationChecks.Progress(), default); }
            catch (HttpRequestException) { failed = true; }
            var logs = Directory.GetFiles(Path.Combine(cache, "diagnostics"), "*.jsonl");
            var text = await File.ReadAllTextAsync(logs.Single());
            check(failed && text.Contains("release-check") && text.Contains("HttpRequestException") && !text.Contains("SyntheticPrivate"),
                "A failure before the download directory exists still leaves phase and exception diagnostics outside disposable download data.");
        }
        if (!OperatingSystem.IsWindows()) return;
        var native = Path.Combine(Path.GetTempPath(), "HabitatUpdateChecks", "artifacts", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(native);
        var script = Path.Combine(native, "Fail-Preparation.ps1");
        await File.WriteAllTextAsync(script, "[Console]::Out.WriteLine('helper stdout phase'); [Console]::Error.WriteLine('Get-FileHash token=TOP_SECRET C:\\Users\\SyntheticPrivate\\private.txt'); exit 7");
        var log = new UpdateDiagnosticLog(Path.Combine(native, "diagnostic.jsonl"));
        var start = AppUpdateInstall.CreateHelperStartInfo(script, "-Unused", "synthetic");
        var preparationFailed = false;
        try { await UpdateHelperHandoff.StartAsync(start, Path.Combine(native, "ready"), log, default); }
        catch (IOException) { preparationFailed = true; }
        var diagnostic = await File.ReadAllTextAsync(log.Path);
        using var last = JsonDocument.Parse(diagnostic.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last());
        check(preparationFailed && last.RootElement.GetProperty("exitCode").GetInt32() == 7 &&
            diagnostic.Contains("helper stdout phase") && diagnostic.Contains("Get-FileHash") &&
            !diagnostic.Contains("TOP_SECRET") && !diagnostic.Contains("SyntheticPrivate"),
            "Helper preparation failures retain redacted stdout/stderr, exit code, phase and exception before any application close.");
        await File.WriteAllTextAsync(script, "Start-Sleep -Seconds 30");
        var timedOut = false;
        try { await UpdateHelperHandoff.StartAsync(AppUpdateInstall.CreateHelperStartInfo(script, "-Unused", "synthetic"),
            Path.Combine(native, "ready"), log, default, TimeSpan.FromMilliseconds(250)); }
        catch (OperationCanceledException) { timedOut = true; }
        check(timedOut && (await File.ReadAllTextAsync(log.Path)).Contains("TaskCanceledException"),
            "A timed-out helper is retired and preserves its preparation failure instead of silently discarding diagnostics.");
    }

    private sealed class Unavailable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new HttpRequestException("Synthetic request failed at C:\\Users\\SyntheticPrivate\\settings.json");
    }
}
