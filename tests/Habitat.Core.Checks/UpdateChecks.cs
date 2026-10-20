using Habitat.Services;
using System.Net;
using System.Text.Json;

internal static class UpdateChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var installed = new Version(0, 10, 0, 0);
        string Release(string tag = "v0.11.0", bool draft = false, bool prerelease = false, string? asset = null, string state = "uploaded") =>
            JsonSerializer.Serialize(new { tag_name = tag, draft, prerelease, html_url = "https://untrusted.invalid/download",
                assets = new[] { new { name = asset ?? "HABITAT-0.11.0-x64-setup.exe", state, size = 1024 } } });
        AppUpdate? Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return AppUpdateService.ParseRelease(doc.RootElement, installed);
        }
        check(Parse(Release()) is { Version: "0.11.0", ReleasePage.AbsoluteUri: "https://github.com/aesgalexis/habitat/releases/tag/v0.11.0" },
            "A newer published Windows release uses a constructed official URL, ignoring server-supplied links.");
        check(Parse(Release(asset: "HABITAT-0.11.0-win-x64.zip")) is not null, "ZIP releases also enable the notice.");
        foreach (var tag in new[] { "v0.10.0", "v0.9.99", "v0.11.0-beta.1", "v01.11.0", "v999999999999.0.0", "../download", "v0.11.0/other" })
            check(Parse(Release(tag)) is null, "Ignore equal/older, prerelease or malformed versions: " + tag);
        check(Parse(Release("v0.10.1", asset: "HABITAT-0.10.1-x64-setup.exe")) is not null &&
            Parse(Release("v0.9.100", asset: "HABITAT-0.9.100-x64-setup.exe")) is null,
            "Compare versions numerically and normalize the installed assembly revision.");
        check(Parse(Release(draft: true)) is null && Parse(Release(prerelease: true)) is null,
            "Draft and prerelease publications never trigger an update notice.");
        check(Parse(Release(asset: "source.zip")) is null && Parse(Release(state: "new")) is null,
            "Source-only releases and assets still uploading do not trigger a notice.");
        check(Parse(Release().Replace("\"size\":1024", "\"size\":\"broken\"")) is null,
            "Malformed asset metadata is ignored safely.");

        var calls = 0;
        var failures = 0;
        var mode = "available";
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async (request, token) =>
        {
            calls++;
            check(request.RequestUri == AppUpdateService.LatestRelease && request.Headers.UserAgent.ToString().Contains("HABITAT/0.10.0") &&
                request.Headers.Authorization is null, "Public release checks use the official endpoint and send no credentials.");
            if (mode == "cancel") { started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            if (mode == "offline") throw new HttpRequestException("Synthetic outage");
            if (mode == "timeout") throw new TaskCanceledException("Synthetic timeout");
            return new HttpResponseMessage(mode == "missing" ? HttpStatusCode.NotFound : mode == "rate-limit" ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
            { Content = new StringContent(mode == "invalid" ? "{" : mode == "incomplete" ? "{}" : Release(mode == "current" ? "v0.10.0" : "v0.11.0")) };
        });
        using var http = new HttpClient(handler);
        using var service = new AppUpdateService(http, installed, _ => failures++);
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        await service.CheckIfDueAsync(now);
        check(calls == 1 && service.Available is not null && service.NextCheck == now.AddHours(6),
            "Check immediately at startup and schedule the next check six hours later.");
        await service.CheckIfDueAsync(now.AddHours(6).AddTicks(-1));
        check(calls == 1, "Frequent timer ticks do not cause extra requests.");
        foreach (var failure in new[] { "offline", "timeout", "rate-limit", "invalid", "incomplete" })
        {
            mode = failure;
            now = service.NextCheck;
            await service.CheckIfDueAsync(now);
            check(service.Available is not null && service.NextCheck == now.AddHours(1),
                "Preserve the existing notice and retry in an hour after " + failure);
        }
        check(failures == 5, "Log failed checks without losing a known update.");
        mode = "current";
        await service.CheckIfDueAsync(service.NextCheck);
        check(service.Available is null, "A successful current-version response clears an obsolete notice.");
        mode = "missing";
        now = service.NextCheck;
        await service.CheckIfDueAsync(now);
        check(service.Available is null && service.NextCheck == now.AddHours(6) && failures == 5,
            "No public release is a normal result and uses the regular schedule.");
        mode = "cancel";
        using var cancellation = new CancellationTokenSource();
        now = service.NextCheck;
        var pending = service.CheckIfDueAsync(now, cancellation.Token);
        await started.Task;
        var previousCalls = calls;
        await service.CheckIfDueAsync(now.AddDays(1));
        check(calls == previousCalls, "A slow check never overlaps another request.");
        cancellation.Cancel();
        var cancelled = false;
        try { await pending; } catch (OperationCanceledException) { cancelled = true; }
        check(cancelled && failures == 5 && service.Available is null, "Shutdown cancellation drains the request without a late notice or outage log.");
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
