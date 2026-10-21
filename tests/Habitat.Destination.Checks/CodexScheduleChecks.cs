using Habitat.Features.Codex;
using Habitat.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class CodexScheduleChecks
{
    private static readonly CodexChatTarget A = new("11111111-1111-1111-1111-111111111111", "Original title ☕");
    private static readonly CodexChatTarget B = new("22222222-2222-2222-2222-222222222222", "Limited chat");
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var now = new DateTimeOffset(2026, 10, 21, 16, 0, 0, TimeSpan.FromHours(2));
        var root = Path.Combine(AppContext.BaseDirectory, "isolated-codex-schedules", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "outbox.json");
        var queue = new CodexScheduleQueue(file);
        const string text = "  Continúa mañana ☕\r\n**literal** 雪  ";
        var first = queue.Add(A, text, now.AddMinutes(5), now);
        check(new CodexScheduleQueue(file).Messages.Single() is { State: CodexScheduleState.Pending } saved &&
            saved.Target == A && saved.Text == text && saved.SendAt == now.AddMinutes(5),
            "Codex schedules persist the exact target ID, original text and absolute time across restarts");
        var calls = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<CodexScheduledAttempt>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<CodexScheduledAttempt> Send(CodexScheduledMessage item, CancellationToken token)
        {
            calls++;
            check(item.Target == A && item.Text == text && File.ReadAllText(file).Contains("\"Sending\""),
                "The exact scheduled chat and text are fixed and Sending is durable before attempting delivery");
            started.TrySetResult(); return finish.Task;
        }
        await queue.RunDueAsync(now, Send, default);
        check(calls == 0, "A Codex timer never sends early");
        var pending = queue.RunDueAsync(now.AddMinutes(5), Send, default);
        await started.Task;
        await queue.RunDueAsync(now.AddMinutes(5), Send, default);
        check(calls == 1 && queue.IsSending && !queue.Cancel(first.Id) && !queue.Remove(first.Id),
            "Overlapping ticks, cancellation and removal cannot duplicate an in-flight scheduled send");
        finish.SetResult(new(CodexDeliveryResult.Sent)); await pending;
        await queue.RunDueAsync(now.AddMinutes(6), Send, default);
        check(calls == 1 && !queue.IsSending && new CodexScheduleQueue(file).Messages.Single().State == CodexScheduleState.Sent,
            "Confirmed scheduled delivery is persisted and cannot run again after a tick or restart");

        var uncertain = queue.Add(B, text, now.AddMinutes(8), now);
        await queue.RunDueAsync(now.AddMinutes(8), (_, _) => Task.FromResult(new CodexScheduledAttempt(CodexDeliveryResult.Unconfirmed)), default);
        await queue.RunDueAsync(now.AddMinutes(9), (_, _) => throw new Exception("Must not repeat an uncertain send"), default);
        check(queue.Messages.Single(item => item.Id == uncertain.Id) is { State: CodexScheduleState.NeedsAttention, Issue: "CodexScheduleUnconfirmed" },
            "Unconfirmed delivery always needs review instead of automatically pressing Enter again");
        queue.Reschedule(uncertain.Id, B, "New instruction", now.AddMinutes(10), now.AddMinutes(9));
        await queue.RunDueAsync(now.AddMinutes(10), (_, _) => Task.FromResult(new CodexScheduledAttempt(CodexDeliveryResult.NotSent, now.AddMinutes(12))), default);
        var deferredCalls = 0;
        Task<CodexScheduledAttempt> Deferred(CodexScheduledMessage item, CancellationToken _)
        { deferredCalls++; check(item.Target == B && item.Text == "New instruction", "A deferred retry keeps its authorized chat and revised text"); return Task.FromResult(new CodexScheduledAttempt(CodexDeliveryResult.Sent)); }
        await queue.RunDueAsync(now.AddMinutes(11), Deferred, default);
        check(deferredCalls == 0, "Busy chats and known usage resets defer without submitting before the retry time");
        await queue.RunDueAsync(now.AddMinutes(12), Deferred, default);
        check(deferredCalls == 1, "A known not-yet-attempted delivery can run once when its wait expires");

        var late = queue.Add(A, "Late message", now.AddMinutes(15), now);
        await queue.RunDueAsync(now.AddMinutes(21), (_, _) => throw new Exception("Must not send late"), default);
        check(queue.Messages.Single(item => item.Id == late.Id).Issue == "CodexScheduleMissed",
            "Messages over five minutes late remain available for explicit rescheduling");
        var cancelled = queue.Add(A, "Cancelled message", now.AddMinutes(30), now);
        check(queue.Cancel(cancelled.Id) && queue.Remove(cancelled.Id), "Pending messages can be cancelled and retired without executing anything");
        var interrupted = queue.Add(B, "Interrupted", now.AddMinutes(31), now);
        await queue.RunDueAsync(now.AddMinutes(31), (_, _) => throw new OperationCanceledException(), default);
        check(queue.Messages.Single(item => item.Id == interrupted.Id).Issue == "CodexScheduleUnconfirmed",
            "Shutdown during a scheduled handoff preserves an uncertain result without retrying");

        var crashedPath = Path.Combine(root, "crashed.json");
        new CodexScheduleQueue(crashedPath).Add(A, text, now.AddMinutes(5), now);
        var document = JsonNode.Parse(File.ReadAllText(crashedPath))!;
        document["Messages"]![0]!["State"] = "Sending";
        File.WriteAllText(crashedPath, document.ToJsonString());
        check(new CodexScheduleQueue(crashedPath).Messages.Single().Issue == "CodexScheduleUnconfirmed",
            "An interrupted app restart recovers Sending as Needs review rather than resending");
        var corrupt = Path.Combine(root, "corrupt.json"); File.WriteAllText(corrupt, "{broken");
        try { new CodexScheduleQueue(corrupt).Add(A, text, now.AddMinutes(1), now); check(false, "Corruption must fail closed"); }
        catch (JsonException) { check(File.ReadAllText(corrupt) == "{broken", "A corrupt outbox is preserved and never overwritten by a new schedule"); }
        var blocked = Path.Combine(root, "directory"); Directory.CreateDirectory(blocked);
        var rejected = new CodexScheduleQueue(blocked);
        try { rejected.Add(A, text, now.AddMinutes(1), now); check(false, "Persistence failure must fail closed"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { check(rejected.Messages.Count == 0, "A failed save cannot create an in-memory scheduled send"); }

        await CheckServiceAsync(check);
        CheckLimitMetadata(now, check);
    }

    private static void CheckLimitMetadata(DateTimeOffset now, Action<bool, string> check)
    {
        foreach (var info in new[] { "\"usageLimitExceeded\"", "{\"usageLimitExceeded\":{}}", "{\"type\":\"UsageLimitExceeded\"}" })
        {
            using var doc = JsonDocument.Parse("{\"data\":[{\"id\":\"last\",\"status\":\"failed\",\"items\":[],\"error\":{\"message\":\"Notice\",\"codexErrorInfo\":" + info + "}}]}");
            check(CodexUsageLimit.IsLimited(CodexReadClient.ParseHistory(A, doc.RootElement)), "Structured usage-limit errors prioritize the chat: " + info);
        }
        CodexChatTurn Turn(string id, string author, string notice) => new(id, "completed", [new("text", author, notice)]);
        check(!CodexUsageLimit.IsLimited(new(A, [Turn("old", "Codex", "You've hit your usage limit"), Turn("new", "Codex", "Finished")])) &&
            !CodexUsageLimit.IsLimited(new(A, [Turn("last", "YOU", "You've hit your usage limit")])),
            "An older limit or user-provided quotation cannot mark the last Codex turn as limited");
        check(CodexUsageLimit.IsLimited(new(A, [Turn("last", "Codex", "Llegaste al límite, puedes seguir a las 18:00")])) &&
            CodexUsageLimit.IsLimited(new(A, [Turn("last", "Codex", "You've hit your usage limit. Try again later.")])),
            "English and Spanish final limit notices are recognized without inventing a reset timestamp");
        using var limits = JsonDocument.Parse(JsonSerializer.Serialize(new { rateLimitsByLimitId = new {
            first = new { primary = new { usedPercent = 100, resetsAt = now.AddHours(1).ToUnixTimeSeconds() } },
            second = new { secondary = new { usedPercent = 100, resetsAt = now.AddHours(2).ToUnixTimeSeconds() } },
            unused = new { primary = new { usedPercent = 20, resetsAt = now.AddDays(2).ToUnixTimeSeconds() } } } }));
        check(CodexReadClient.ParseLimitReset(limits.RootElement, now) == now.AddHours(2),
            "The suggested account reset honors exhausted windows and ignores non-exhausted buckets");
        using var missing = JsonDocument.Parse("{\"rateLimits\":null}");
        check(CodexReadClient.ParseLimitReset(missing.RootElement, now) is null, "Unavailable reset metadata remains unknown");
    }

    private static async Task CheckServiceAsync(Action<bool, string> check)
    {
        var reader = new Reader(); var desktop = new Desktop(); var origins = new CodexChatOrigins(null); origins.Register(A);
        await using var service = new CodexChatService(desktop, reader, (_, _) => throw new Exception("Must never create a chat"), origins);
        var own = await service.ListScheduleTargetsAsync(false, default);
        check(own.Count == 1 && own[0].Target == A && !reader.Reads.Contains(B.Id), "The default schedule picker respects HABITAT chat provenance");
        var all = await service.ListScheduleTargetsAsync(true, default);
        check(all[0] is { UsageLimited: true, ResumeAt: not null } && all[0].Target == B,
            "Explicitly including local chats prioritizes the latest usage-limit failure and carries the known reset time");
        var deferred = await service.SendScheduledAsync(B, "continue", default);
        check(deferred.RetryAt == reader.Reset!.Value.AddSeconds(5) && desktop.Opens == 0 && desktop.Sends == 0,
            "A known future account reset never opens or submits the scheduled chat early");
        reader.Reset = null; reader.Running = true;
        check((await service.SendScheduledAsync(B, "continue", default)).RetryAt is not null && desktop.Opens == 0,
            "An active target turn defers the scheduled message before navigation");
        reader.Running = false; desktop.Empty = false;
        check((await service.SendScheduledAsync(B, "continue", default)).Result == CodexDeliveryResult.NotSent && desktop.Sends == 0,
            "A draft in the destination Codex editor is never replaced by a scheduled message");
        desktop.Empty = true;
        check((await service.SendScheduledAsync(B, "  Exact ☕\ntext ", default)).Result == CodexDeliveryResult.Sent &&
            desktop.Sends == 1 && desktop.LastTarget == B && desktop.Text == "  Exact ☕\ntext " && !origins.Contains(B),
            "Scheduled authorization submits once to the chosen foreign chat without changing its provenance or user text");
        check((await service.GetVisibleAsync(default)).Target is null && (await service.SendAsync(B, "ordinary send", default)).Result == CodexDeliveryResult.NotSent,
            "A scheduled foreign-chat send does not widen the normal composer origin boundary");
        reader.ResolveWrong = true;
        using var cancel = new CancellationTokenSource(80);
        try { await service.SendScheduledAsync(B, "must not send", cancel.Token); }
        catch (OperationCanceledException) { }
        check(desktop.Sends == 1, "Navigation that resolves to a different chat never sends to the currently visible conversation");
    }

    private sealed class Reader : ICodexHistoryReader
    {
        public List<string> Reads = [];
        public bool Running, ResolveWrong;
        public DateTimeOffset? Reset = DateTimeOffset.UtcNow.AddHours(1);
        public Task<IReadOnlyList<CodexChatTarget>> ListAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<CodexChatTarget>>([A, B]);
        public Task<DateTimeOffset?> ReadLimitResetAsync(CancellationToken token) => Task.FromResult(Reset);
        public Task<CodexChatTarget?> ResolveAsync(IReadOnlyList<string> titles, CancellationToken token) => Task.FromResult<CodexChatTarget?>(ResolveWrong ? A : B);
        public Task<CodexChatHistory> ReadAsync(CodexChatTarget target, CancellationToken token)
        {
            Reads.Add(target.Id);
            return Task.FromResult(new CodexChatHistory(target, [new("last", Running ? "inProgress" : target == B ? "failed" : "completed", [],
                target == B ? "You've hit your usage limit" : null, target == B ? "UsageLimitExceeded" : null)]));
        }
    }
    private sealed class Desktop : ICodexChatDesktop
    {
        public bool Empty = true;
        public int Opens, Sends;
        public CodexChatTarget? LastTarget;
        public string? Text;
        public Task<CodexVisibleChat?> ReadVisibleAsync(CancellationToken token) => Task.FromResult<CodexVisibleChat?>(new(new(1, "editor", "send", Empty ? "" : "Protected draft", false, true), [B.Title], Empty, true));
        public Task<bool> OpenChatAsync(CodexChatTarget? target, CancellationToken token) { Opens++; LastTarget = target; return Task.FromResult(true); }
        public Task<CodexDeliveryResult> SubmitVisibleAsync(CodexVisibleChat expected, string text, CancellationToken token) { Sends++; Text = text; return Task.FromResult(CodexDeliveryResult.Sent); }
        public void ReturnFocus(nint ownerWindow) { }
    }
}
