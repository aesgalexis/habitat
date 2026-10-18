using Habitat.Features.Translation;
using Habitat.Services;

internal static class TranslationChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        check(TranslationLanguages.DefaultCode == "en" && TranslationLanguages.All.Select(item => item.Code).SequenceEqual(new[] { "en", "el", "es" }),
            "English is the default; English, Greek and Spanish are the only translation targets");
        var service = new BrowserTranslationService();
        try { await service.TranslateAsync("Hello", "en", CancellationToken.None); throw new Exception("Missing browser connection was accepted."); }
        catch (BrowserTranslationException error) { check(error.Message.Contains("extension"), "A missing browser connection explains how to reconnect"); }
        var owner = new object();
        service.Attach(owner);
        const string original = "  Hola & café+雪☕ #1\r\n\r\n segunda línea  ";
        var translation = service.TranslateAsync(original, "en", CancellationToken.None);
        var request = service.Pending(owner)!;
        check(request.Text == original && request.Target == "en" && !translation.IsCompleted,
            "The browser job carries the exact original text and target without translating through HTTP");
        service.Complete(new object(), request.Id, "Wrong owner", null);
        service.Complete(owner, "wrong-id", "Wrong job", null);
        check(!translation.IsCompleted, "Only the matching job on the authenticated browser channel may return a result");
        service.Complete(owner, request.Id, "<b>plain text</b> & ☕\nsecond line", null);
        check((await translation).Text == "<b>plain text</b> & ☕\nsecond line" && service.Pending(owner) is null,
            "The visible page result returns as exact plain text and clears the completed job");
        using var cancellation = new CancellationTokenSource();
        translation = service.TranslateAsync("Cancel me", "el", cancellation.Token);
        var canceledId = service.Pending(owner)!.Id;
        cancellation.Cancel();
        try { await translation; throw new Exception("Cancellation was ignored."); } catch (OperationCanceledException) { }
        translation = service.TranslateAsync("New translation", "es", CancellationToken.None);
        service.Complete(owner, canceledId, "Stale text", null);
        check(!translation.IsCompleted, "Canceled browser jobs cannot fill a newer translation");
        service.Complete(owner, service.Pending(owner)!.Id, null, "consent");
        try { await translation; throw new Exception("Consent blocker was ignored."); }
        catch (BrowserTranslationException error) { check(error.Message.Contains("consent"), "Browser consent blockers have a specific recovery message"); }
        foreach (var errorCode in new[] { "background-permission", "background-unavailable" })
        {
            translation = service.TranslateAsync("Background recovery", "en", CancellationToken.None);
            service.Complete(owner, service.Pending(owner)!.Id, null, errorCode);
            try { await translation; throw new Exception("Background automation failure was ignored."); }
            catch (BrowserTranslationException error)
            {
                check(error.Message.Contains("background") && error.Message.Contains("retry"),
                    "Background translation failures explain recovery in English: " + errorCode);
            }
        }
        translation = service.TranslateAsync("Browser disconnect", "en", CancellationToken.None);
        service.Detach(owner);
        try { await translation; throw new Exception("Browser disconnect was ignored."); }
        catch (BrowserTranslationException) { check(!service.IsConnected, "A lost browser connection releases the pending translation"); }

        var deadline = new DeadlineClock();
        var unresponsive = new BrowserTranslationService(deadline);
        unresponsive.Attach(owner);
        translation = unresponsive.TranslateAsync("A browser that never returns", "en", CancellationToken.None);
        deadline.Expire();
        try { await translation; throw new Exception("An unresponsive browser left translation pending forever."); }
        catch (BrowserTranslationException error)
        {
            check(error.Message.Contains("did not return") && unresponsive.Pending(owner) is null,
                "A silent connected browser times out and releases its pending job for retry");
        }
        translation = unresponsive.TranslateAsync("Retry after timeout", "en", CancellationToken.None);
        unresponsive.Complete(owner, unresponsive.Pending(owner)!.Id, "Successful retry", null);
        check((await translation).Text == "Successful retry", "Translation can succeed immediately after a browser timeout");
    }

    public static async Task BrowserSmokeAsync(Action<bool, string> check)
    {
        // Explicit opt-in: HABITAT must be closed first. No task files, chat
        // messages, user text or translation HTTP endpoints are used.
        await using var bridge = new BridgeService(8765);
        await bridge.StartAsync();
        if (bridge.Address is null) throw new Exception("Close HABITAT before running the browser translation smoke check.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        while (!bridge.Translation.IsConnected) await Task.Delay(200, timeout.Token);
        string? translatorTab = null;
        foreach (var target in new[] { "en", "el", "es" })
        {
            var source = target == "es" ? "Hello world\nGood morning" : "Hola mundo\nBuenos días";
            TranslationResult result;
            try { result = await bridge.Translation.TranslateAsync(source, target, timeout.Token); }
            catch { Console.WriteLine("Browser stage: " + bridge.Translation.LastDiagnostic); throw; }
            check(result.Text.Length > 0 && result.Text != source, $"Chrome Google Translate page returns a real {target} result (synthetic text only)");
            var diagnostic = bridge.Translation.LastDiagnostic ?? "";
            Console.WriteLine("Browser stage: " + diagnostic);
            check(diagnostic.Contains(";background:true"), "The translator uses page emulation instead of activating Chrome");
            var tab = diagnostic.Split(';').FirstOrDefault(part => part.StartsWith("tab:"));
            check(tab is not null && (translatorTab is null || translatorTab == tab),
                "Successive translations use the same Chrome translator tab");
            translatorTab = tab;
            Console.WriteLine(result.Text);
        }
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
