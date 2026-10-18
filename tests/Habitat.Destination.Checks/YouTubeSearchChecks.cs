using Habitat.Features.Search;
using Habitat.Services;

internal static class YouTubeSearchChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var service = new BrowserSearchService(destination: ComposerDestination.YouTube);
        try { await service.SearchAsync("Video query", 0, default); throw new Exception("Disconnected YouTube accepted a query."); }
        catch (BrowserSearchException error) { check(error.Message.Contains("YouTube"), "YouTube connection recovery names its own provider"); }
        var owner = new object();
        service.Attach(owner);
        const string query = "  café & snow 雪 + #1\nvideo  ";
        var pending = service.SearchAsync(query, 20, default);
        var request = service.Pending(owner)!;
        check(request.Query == query && request.Start == 20, "YouTube carries the exact query and loaded-video offset");
        var video = new SearchResult("Video title ☕", "https://www.youtube.com/watch?v=abcdefghijk", "Channel · 12:34 · 1K views\nDescription 雪");
        check(BrowserSearchService.ThumbnailUri(video.Url)?.AbsoluteUri == "https://i.ytimg.com/vi/abcdefghijk/mqdefault.jpg",
            "YouTube thumbnails use a small JPEG on the fixed CDN host from the validated video ID, without expanding the bridge payload");
        service.Complete(new object(), request.Id, [video], 40, null);
        check(!pending.IsCompleted, "YouTube rejects results from another channel owner");
        service.Complete(owner, request.Id, [video], 40, null);
        var page = await pending;
        check(page.Results.Single() == video && page.NextStart == 40, "YouTube titles, canonical video URLs and page metadata return as plain text");
        foreach (var link in new[] { "https://example.com/watch?v=abcdefghijk", "https://www.youtube.com/redirect?q=https://example.com",
            "https://www.youtube.com/watch?v=invalid", "https://www.youtube.com/watch?v=abcdefghijk&list=arbitrary", "javascript:alert(1)" })
        {
            check(BrowserSearchService.ThumbnailUri(link) is null, "Unsafe video links cannot request arbitrary thumbnail URLs");
            pending = service.SearchAsync("Unsafe video link", 0, default);
            service.Complete(owner, service.Pending(owner)!.Id, [video with { Url = link }], null, null);
            try { await pending; throw new Exception("Unsafe YouTube URL accepted."); }
            catch (BrowserSearchException) { check(true, "YouTube rejects noncanonical or unsafe video URL: " + link); }
        }
        foreach (var code in new[] { "consent", "permission", "blocked", "page-changed", "timeout", "result", "background-permission", "background-unavailable" })
        {
            pending = service.SearchAsync("Recovery", 0, default);
            service.Complete(owner, service.Pending(owner)!.Id, null, null, code);
            try { await pending; throw new Exception("YouTube blocker ignored."); }
            catch (BrowserSearchException error) { check(error.Message.Contains(code == "permission" ? "youtube.com" : code == "background-permission" ? "background" : "YouTube"), "YouTube recovery stays with its own site: " + code); }
        }
        var google = new BrowserSearchService();
        google.Attach(owner);
        var googleQuery = google.SearchAsync("Google stays active", 0, default);
        pending = service.SearchAsync("Cancel YouTube", 0, default);
        var oldId = service.Pending(owner)!.Id;
        service.Reset();
        try { await pending; throw new Exception("YouTube reset ignored."); } catch (OperationCanceledException) { }
        check(google.Pending(owner)?.Query == "Google stays active" && !google.HasPendingReset,
            "Resetting YouTube neither cancels Google's request nor closes its tab");
        pending = service.SearchAsync("Fresh YouTube", 0, default);
        service.Complete(owner, oldId, [video], null, null);
        service.AcknowledgeReset(owner, service.PendingState(owner).Reset, true);
        service.Complete(owner, service.Pending(owner)!.Id, [video], null, null);
        check((await pending).Results.Single() == video, "A fresh YouTube query survives cancellation and late results from the previous query");
        google.Complete(owner, google.Pending(owner)!.Id, [], null, null);
        await googleQuery;
        var uri = BrowserSearchService.PageUri(ComposerDestination.YouTube, query, 20);
        check(uri.Host == "www.youtube.com" && uri.AbsolutePath == "/results" && uri.Query.Contains(Uri.EscapeDataString(query)) &&
            !uri.Query.Contains("start="), "Open YouTube search encodes input without inventing a page URL for its infinite result list");
    }

    public static async Task BrowserSmokeAsync(Action<bool, string> check)
    {
        await using var bridge = new BridgeService(8765);
        await bridge.StartAsync();
        if (bridge.Address is null) throw new Exception("Close HABITAT before the YouTube browser check.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        while (!bridge.YouTubeSearch.IsConnected) await Task.Delay(200, timeout.Token);
        try
        {
            var page = await bridge.YouTubeSearch.SearchAsync("Microsoft Windows developer", 0, timeout.Token);
            check(page.Results.Count > 0 && page.Results.All(item => BrowserSearchService.IsVideoLink(item.Url)),
                "The real YouTube search page returns video titles and canonical links through Chrome");
            check(page.Results.Any(item => item.Snippet.Length > 0), "Real YouTube video results include channel, metadata or description");
            Console.WriteLine($"Live YouTube: {page.Results.Count} videos; details: {page.Results.Count(item => item.Snippet.Length > 0)}; next: {page.NextStart}");
            if (page.NextStart is not { } next) throw new Exception("Expected more results for the synthetic YouTube query.");
            var second = await bridge.YouTubeSearch.SearchAsync("Microsoft Windows developer", next, timeout.Token);
            check(second.Results.Count > 0 && !second.Results.Select(item => item.Url).SequenceEqual(page.Results.Select(item => item.Url)),
                "The real YouTube continuation loads a different page of videos");
            var previous = await bridge.YouTubeSearch.SearchAsync("Microsoft Windows developer", 0, timeout.Token);
            check(previous.Results.Select(item => item.Url).SequenceEqual(page.Results.Select(item => item.Url)),
                "Previous page preserves the original loaded YouTube video order");
            bridge.YouTubeSearch.Reset();
            while (bridge.YouTubeSearch.HasPendingReset) await Task.Delay(100, timeout.Token);
            check(bridge.YouTubeSearch.LastResetClosedTab == true, "YouTube reset closes its dedicated search tab");
            page = await bridge.YouTubeSearch.SearchAsync("Microsoft Windows developer", 0, timeout.Token);
            check(page.Results.Count > 0, "A new YouTube search works after reset closes the previous tab");
        }
        catch
        {
            Console.WriteLine("YouTube page stage: " + bridge.YouTubeSearch.LastDiagnostic);
            throw;
        }
        finally
        {
            bridge.YouTubeSearch.Reset();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (bridge.YouTubeSearch.HasPendingReset) await Task.Delay(100, cleanup.Token);
        }
        check(bridge.YouTubeSearch.LastResetClosedTab == true, "The live YouTube check leaves its own search tab closed");
    }
}
