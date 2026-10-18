using Habitat.Features.Search;
using Habitat.Services;

internal static class SearchChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var service = new BrowserSearchService();
        async Task Reject(Task<SearchPage> task, string message, string? contains = null)
        {
            try { await task; throw new Exception("Invalid search succeeded: " + message); }
            catch (BrowserSearchException error) { check(contains is null || error.Message.Contains(contains, StringComparison.OrdinalIgnoreCase), message); }
        }
        await Reject(service.SearchAsync("Synthetic query", 0, default), "A missing extension explains search recovery", "extension");
        var owner = new object();
        service.Attach(owner);
        const string query = "  café & cats+dogs #1? 雪 ☕\nnext / https://example.com  ";
        var search = service.SearchAsync(query, 0, default);
        var request = service.Pending(owner)!;
        check(request.Query == query && request.Start == 0, "Google search carries the exact Unicode query and page offset");
        var results = new[] { new SearchResult("<b>Plain title</b> ☕", "https://example.com/one", "Exact fragment & 雪"),
            new SearchResult("Second", "https://example.org/two", "") };
        service.Complete(new object(), request.Id, results, 10, null);
        service.Complete(owner, "stale-id", results, 10, null);
        check(!search.IsCompleted, "Search ignores unauthenticated owners and unrelated job IDs");
        service.Complete(owner, request.Id, results, 10, null);
        var page = await search;
        check(page.Results.SequenceEqual(results) && page.NextStart == 10 && service.Pending(owner) is null,
            "Google title, link, snippet, order and next-page offset return as plain data");
        foreach (var unsafeUrl in new[] { "javascript:alert(1)", "file:///C:/secret.txt", "https://user:password@example.com", "/relative" })
        {
            search = service.SearchAsync("Safe links", 0, default);
            service.Complete(owner, service.Pending(owner)!.Id, [new("Unsafe", unsafeUrl, "")], null, null);
            await Reject(search, "Reject unsafe result URLs: " + unsafeUrl);
        }
        search = service.SearchAsync("Bad page", 10, default);
        service.Complete(owner, service.Pending(owner)!.Id, results, 10, null);
        await Reject(search, "Pagination must advance rather than repeat the same page");
        search = service.SearchAsync("No matches", 0, default);
        service.Complete(owner, service.Pending(owner)!.Id, [], null, null);
        check((await search).Results.Count == 0, "A confirmed Google empty result page is represented explicitly");
        using var cancellation = new CancellationTokenSource();
        search = service.SearchAsync("Canceled", 0, cancellation.Token);
        var oldId = service.Pending(owner)!.Id;
        cancellation.Cancel();
        try { await search; throw new Exception("Search cancellation ignored."); } catch (OperationCanceledException) { }
        search = service.SearchAsync("Current", 0, default);
        service.Complete(owner, oldId, results, null, null);
        check(!search.IsCompleted, "Canceled results never populate a new search");
        service.Complete(owner, service.Pending(owner)!.Id, null, null, "consent");
        await Reject(search, "Google consent has an actionable English recovery message", "consent");
        foreach (var code in new[] { "blocked", "permission", "page-changed", "timeout" })
        {
            search = service.SearchAsync("Recovery", 0, default);
            service.Complete(owner, service.Pending(owner)!.Id, null, null, code);
            await Reject(search, "Google search explains recovery for " + code, "retry");
        }
        var nextOwner = new object();
        service.Attach(nextOwner);
        service.Detach(owner);
        check(service.IsConnected, "An old search connection cannot detach its replacement");
        search = service.SearchAsync("Disconnected", 0, default);
        service.Detach(nextOwner);
        await Reject(search, "A disconnected search bridge releases the pending request", "disconnected");
        await Reject(service.SearchAsync(new string('a', 2049), 0, default), "Oversized queries are rejected before browser work");

        var clock = new DeadlineClock();
        var unresponsive = new BrowserSearchService(clock);
        unresponsive.Attach(owner);
        search = unresponsive.SearchAsync("Silent browser", 0, default);
        clock.Expire();
        await Reject(search, "An unresponsive browser times out and permits retry", "did not return");
        check(unresponsive.Pending(owner) is null, "Search timeout clears its pending browser job");
        var uri = BrowserSearchService.PageUri(query, 10);
        check(uri.Query.Contains("&udm=14&hl=en&start=10") && uri.Query.Contains(Uri.EscapeDataString(query)) && uri.Fragment == "",
            "Search URLs encode input and request a page of ordinary web results");

        var resetService = new BrowserSearchService();
        resetService.Reset();
        check(resetService.HasPendingReset, "An offline reset is retained until the browser reconnects");
        resetService.Attach(owner);
        var resetId = resetService.PendingState(owner).Reset!;
        resetService.Reset();
        check(resetService.PendingState(owner).Reset == resetId, "Repeated resets coalesce until Chrome acknowledges cleanup");
        search = resetService.SearchAsync("After reset", 0, default);
        var freshId = resetService.Pending(owner)!.Id;
        check(resetService.PendingState(owner).Request is null, "A new search waits for tab cleanup before being delivered");
        resetService.AcknowledgeReset(new object(), resetId, true);
        resetService.AcknowledgeReset(owner, "stale-reset", true);
        check(resetService.HasPendingReset, "Only the authenticated owner and current reset ID can acknowledge cleanup");
        resetService.AcknowledgeReset(owner, resetId, true);
        check(!resetService.HasPendingReset && resetService.LastResetClosedTab == true && resetService.PendingState(owner).Request?.Id == freshId,
            "Acknowledged cleanup releases the following search without erasing it");
        resetService.Complete(owner, freshId, results, null, null);
        await search;
        var interrupted = resetService.SearchAsync("Interrupted search", 0, default);
        var interruptedId = resetService.Pending(owner)!.Id;
        resetService.Reset();
        search = resetService.SearchAsync("Replacement query", 0, default);
        try { await interrupted; throw new Exception("Reset did not cancel the active browser request."); } catch (OperationCanceledException) { }
        resetService.Complete(owner, interruptedId, results, null, null);
        check(!search.IsCompleted && resetService.Pending(owner)?.Query == "Replacement query", "Reset cancels the old job and its finalizer cannot remove a new query");
        resetService.AcknowledgeReset(owner, resetService.PendingState(owner).Reset, false);
        resetService.Complete(owner, resetService.Pending(owner)!.Id, results, null, null);
        check((await search).Results.Count == 2 && resetService.LastResetClosedTab == false, "Reset also completes when its tab was already closed");
    }

    public static async Task BrowserSmokeAsync(Action<bool, string> check)
    {
        // Opt-in real browser check, no user tasks, account data or chat messages.
        await using var bridge = new BridgeService(8765);
        await bridge.StartAsync();
        if (bridge.Address is null) throw new Exception("Close HABITAT before the Google browser smoke check.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        while (!bridge.Search.IsConnected) await Task.Delay(200, timeout.Token);
        var page = await bridge.Search.SearchAsync("Microsoft WinUI documentation", 0, timeout.Token);
        check(page.Results.Count > 0 && page.Results.All(item => BrowserSearchService.IsSafeLink(item.Url)),
            "The real Google page returns ordinary web result titles, links and descriptions through Chrome");
        check(page.Results.Any(item => item.Snippet.Length > 0), "The live Google reader includes result descriptions");
        Console.WriteLine($"Live Google page: {page.Results.Count} results; descriptions: {page.Results.Count(item => item.Snippet.Length > 0)}; next page: {page.NextStart}");
        if (page.NextStart is { } next)
        {
            var second = await bridge.Search.SearchAsync("Microsoft WinUI documentation", next, timeout.Token);
            check(second.Results.Count > 0 && !second.Results.Select(item => item.Url).SequenceEqual(page.Results.Select(item => item.Url)),
                "The real Google next-page link returns a different ordered result page");
        }
        bridge.Search.Reset();
        while (bridge.Search.HasPendingReset) await Task.Delay(100, timeout.Token);
        check(bridge.Search.LastResetClosedTab == true, "Reset closes the actual Google search tab through the extension");
        page = await bridge.Search.SearchAsync("Microsoft WinUI documentation", 0, timeout.Token);
        check(page.Results.Count > 0, "A new real Google search succeeds after its previous tab was closed");
        bridge.Search.Reset();
        while (bridge.Search.HasPendingReset) await Task.Delay(100, timeout.Token);
        check(bridge.Search.LastResetClosedTab == true, "The live smoke check leaves its own search tab closed");
    }

    private sealed class DeadlineClock : TimeProvider
    {
        private Action? _expire;
        internal void Expire() => _expire!();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _expire = () => callback(state);
            return new DeadlineTimer();
        }
        private sealed class DeadlineTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
