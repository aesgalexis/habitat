using Habitat.Features.Composer;
using Habitat.Services;

internal static class ComposerCoordinatorChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var drafts = new ComposerDrafts();
        var coordinator = new ComposerCoordinator(drafts, () => { });
        var calls = new List<string>();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Register(ComposerDestination.Notepad, false, async request =>
        {
            calls.Add(request.Text);
            request.Accept();
            await waiting.Task;
            request.Clear();
        }, _ => false, exclusive: true);
        coordinator.Register(ComposerDestination.Google, false, request =>
        { calls.Add("search:" + request.Text); return Task.CompletedTask; }, _ => false);
        coordinator.Register(ComposerDestination.Google, true, request =>
        { calls.Add("browser:" + request.Text); return Task.CompletedTask; }, _ => false, exclusive: true);
        drafts.Edit(ComposerDestination.Notepad, "  café ☕\r\n literal text  ");
        var sending = coordinator.SubmitAsync(ComposerDestination.Notepad, drafts.Text(ComposerDestination.Notepad));
        await coordinator.SubmitAsync(ComposerDestination.Notepad, "duplicate");
        await coordinator.SubmitAsync(ComposerDestination.Google, "blocked during launcher");
        check(calls.Count == 1 && coordinator.IsExclusiveBusy && calls[0] == "  café ☕\r\n literal text  ",
            "An exclusive launcher captures exact text and blocks duplicate and competing actions");
        drafts.Edit(ComposerDestination.Notepad, "new revision");
        waiting.SetResult();
        await sending;
        check(!coordinator.IsExclusiveBusy && drafts.Text(ComposerDestination.Notepad) == "new revision",
            "Completing an older routed action preserves newer input and releases the launcher");
        await coordinator.SubmitAsync(ComposerDestination.Google, "one");
        await coordinator.SubmitAsync(ComposerDestination.Google, "two", external: true);
        check(calls.TakeLast(2).SequenceEqual(["search:one", "browser:two"]),
            "Integrated and external actions use distinct routes for the same destination");
        var beforeEmpty = calls.Count;
        await coordinator.SubmitAsync(ComposerDestination.Google, "  ");
        await coordinator.SubmitAsync(ComposerDestination.Google, "", external: true);
        check(calls.Count == beforeEmpty + 1 && calls[^1] == "browser:",
            "Empty internal submissions are rejected while explicit external opening remains available");
        coordinator.Register(ComposerDestination.Run, false, request =>
        { request.Accept(); throw new IOException("Synthetic launch failure"); }, _ => false, exclusive: true);
        drafts.Edit(ComposerDestination.Run, "retry me");
        try { await coordinator.SubmitAsync(ComposerDestination.Run, "retry me"); }
        catch (IOException) { }
        check(!coordinator.IsExclusiveBusy && drafts.Text(ComposerDestination.Run) == "retry me",
            "A failed routed action retains its accepted draft and cannot leave the launcher locked");
        var confirm = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executed = false;
        coordinator.Register(ComposerDestination.Terminal, false, async request =>
        {
            await confirm.Task;
            if (!request.CanProceed) return;
            request.Accept(); executed = true;
        }, _ => false, exclusive: true);
        drafts.Edit(ComposerDestination.Terminal, "pending confirmation");
        var confirming = coordinator.SubmitAsync(ComposerDestination.Terminal, "pending confirmation");
        coordinator.Stop();
        confirm.SetResult();
        await confirming;
        await coordinator.SubmitAsync(ComposerDestination.Google, "after shutdown");
        check(!executed && drafts.Text(ComposerDestination.Terminal) == "pending confirmation" && !coordinator.CanSend(ComposerDestination.Google, "text"),
            "Shutdown prevents deferred confirmation from launching and rejects all new routes without consuming the draft");
        check(ComposerCoordinator.OpenAIText("  user text  ", true) == "user text\n\n[HABITAT: Reply in plain text unless asked otherwise.]" &&
            ComposerCoordinator.OpenAIText("  user text  ", false) == "user text",
            "The OpenAI route preserves its existing trimming and optional plain-text instruction");
    }
}
