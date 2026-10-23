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
        finish.SetResult(new(CodexDeliveryResult.Sent, now.AddMinutes(20))); await pending;
        await queue.RunDueAsync(now.AddMinutes(6), Send, default);
        check(calls == 1 && !queue.IsSending && new CodexScheduleQueue(file).Messages.Single() is { State: CodexScheduleState.Sent, Issue: null, RetryAt: null },
            "Acknowledged delivery takes precedence over retry metadata, persists Sent without review and never runs again");

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

        await CheckServiceAsync(root, check);
        await CheckNewChatsAsync(root, now, check);
        await CheckResumptionsAsync(root, now, check);
        CheckLimitMetadata(now, check);
    }

    private static async Task CheckNewChatsAsync(string root, DateTimeOffset now, Action<bool, string> check)
    {
        var file = Path.Combine(root, "first-message.json");
        const string literal = "  Primer mensaje ☕\r\n**literal**  ";
        var queue = new CodexScheduleQueue(file);
        var added = queue.Add(null, literal, now.AddMinutes(5), now);
        queue = new CodexScheduleQueue(file);
        check(queue.Messages.Single() is { Target: null, StartNewChat: true, Text: literal },
            "The first message can explicitly schedule a new chat and survives a restart without a fabricated existing chat ID");

        var origins = new CodexChatOrigins(Path.Combine(root, "new-origins.json"));
        var reader = new Reader(); var desktop = new Desktop(); var starts = 0;
        await using var service = new CodexChatService(desktop, reader, (text, token) =>
        {
            starts++;
            check(text == literal && desktop.Opens == 0 && desktop.Sends == 0 && reader.Reads.Count == 0,
                "A scheduled first message opens a separate new chat without reading or submitting to the visible foreign chat");
            reader.Created = new("33333333-3333-3333-3333-333333333333", "Created by HABITAT", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            reader.InitialText = text + "\n";
            desktop.NoEditor = false;
            return Task.FromResult(CodexDeliveryResult.Sent);
        }, origins);
        check((await service.ListScheduleTargetsAsync(default)).Single() is { Target: var limited, InterruptedTurnId: "last" } && limited == B,
            "An empty registry still offers external chats stopped by a usage limit for explicit resumption");
        reader.Reads.Clear();
        await queue.RunDueAsync(now, (item, token) => service.SendScheduledAsync(item.Target, item.Text, token), default);
        check(starts == 0, "Saving a first-message schedule does not create a Codex chat or send early");
        desktop.NewChat = true; desktop.Empty = false;
        check((await service.SendScheduledAsync(null, literal, default)).Result == CodexDeliveryResult.NotSent && starts == 0 &&
            desktop.Opens == 0 && desktop.Sends == 0 && reader.Reads.Count == 0,
            "A first-message schedule cannot replace an unsent draft in Codex's new-chat editor");
        desktop.NewChat = false;
        reader.Reset = null;
        desktop.Empty = false;
        desktop.NoEditor = true;
        check((await service.SendScheduledAsync(null, "", default)).Result == CodexDeliveryResult.NotSent && starts == 0,
            "An empty first-message request never navigates to Codex even without an editor");
        desktop.NoEditor = false;
        reader.Reset = DateTimeOffset.UtcNow.AddHours(1);
        await queue.RunDueAsync(now.AddMinutes(5), (item, token) => service.SendScheduledAsync(item.Target, item.Text, token), default);
        check(starts == 0 && queue.Messages.Single().RetryAt == reader.Reset!.Value.AddSeconds(5),
            "A new-chat schedule also waits for a known exhausted account limit before creating the chat");
        reader.Reset = null;
        desktop.NoEditor = true;
        await queue.RunDueAsync(queue.Messages.Single().RetryAt!.Value, (item, token) => service.SendScheduledAsync(item.Target, item.Text, token), default);
        check(starts == 1 && queue.Messages.Single().State == CodexScheduleState.Sent && origins.Contains(reader.Created!) &&
            (await service.ListScheduleTargetsAsync(default)).Any(item => item.Target == reader.Created) && !origins.Contains(B),
            "At the scheduled time a confirmed first message creates and registers only its own chat for future destinations");
        queue = new CodexScheduleQueue(file);
        await queue.RunDueAsync(now.AddYears(1), (_, _) => throw new Exception("Must not create another chat"), default);
        check(starts == 1, "Restarting after a confirmed new-chat send never creates a second chat");

        queue.Reschedule(queue.Add(A, literal, now.AddMinutes(10), now).Id, null, literal, now.AddMinutes(11), now);
        check(queue.Messages.Last() is { Target: null, StartNewChat: true }, "Explicitly changing an existing schedule to a new chat preserves its destination kind");
        check(queue.Cancel(queue.Messages.Last().Id), "A first-message schedule can be cancelled without sending");

        var legacyPath = Path.Combine(root, "version-one.json");
        new CodexScheduleQueue(legacyPath).Add(A, literal, now.AddMinutes(1), now);
        var doc = JsonNode.Parse(File.ReadAllText(legacyPath))!;
        doc["Version"] = 1;
        doc["Messages"]![0]!.AsObject().Remove("StartNewChat");
        File.WriteAllText(legacyPath, doc.ToJsonString());
        check(new CodexScheduleQueue(legacyPath).Messages.Single() is { Target: not null, StartNewChat: false },
            "Version-one schedules retain their existing destination and can still be loaded");
        doc["Messages"]![0]!.AsObject().Remove("Target");
        foreach (var version in new[] { 1, 2 })
        {
            doc["Version"] = version;
            File.WriteAllText(legacyPath, doc.ToJsonString());
            try { _ = new CodexScheduleQueue(legacyPath).Messages; check(false, "A missing target must not turn into a new-chat request"); }
            catch (InvalidDataException) { check(true, "Missing destinations never imply permission to start a new chat, schema " + version); }
        }

        var uncertain = new CodexScheduleQueue(Path.Combine(root, "new-unconfirmed.json"));
        uncertain.Add(null, literal, now.AddMinutes(1), now);
        await uncertain.RunDueAsync(now.AddMinutes(1), (_, _) => Task.FromResult(new CodexScheduledAttempt(CodexDeliveryResult.Unconfirmed)), default);
        await uncertain.RunDueAsync(now.AddMinutes(2), (_, _) => throw new Exception("No second chat"), default);
        check(uncertain.Messages.Single().Issue == "CodexScheduleUnconfirmed", "An uncertain new-chat delivery is never automatically retried");
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
            !CodexUsageLimit.IsLimited(new(A, [Turn("last", "YOU", "You've hit your usage limit")])) &&
            !CodexUsageLimit.IsLimited(new(A, [Turn("last", "Codex", "The message 'You've hit your usage limit' means you should wait.")])),
            "An older limit, user quotation or assistant explanation cannot mark the last Codex turn as limited");
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

    private static async Task CheckServiceAsync(string root, Action<bool, string> check)
    {
        var reader = new Reader(); var desktop = new Desktop(); var origins = new CodexChatOrigins(null); origins.Register(A);
        await using var service = new CodexChatService(desktop, reader, (_, _) => throw new Exception("Must never create a chat"), origins);
        var own = await service.ListScheduleTargetsAsync(default);
        check(own.Count == 2 && own[0] is { Target: var limited, InterruptedTurnId: "last" } && limited == B,
            "The picker identifies external interrupted chats by stable chat and turn IDs before regular HABITAT chats");
        var reads = reader.Reads.Count;
        check((await service.SendScheduledAsync(B, "forbidden follow-up", default)).Result == CodexDeliveryResult.NotSent &&
            desktop.Opens == 0 && desktop.Sends == 0 && reader.Reads.Count == reads,
            "Scheduled delivery rejects foreign chat IDs before reading their history or opening Codex");
        var legacy = new CodexScheduleQueue(Path.Combine(root, "legacy-outbox.json"));
        var now = DateTimeOffset.UtcNow;
        legacy.Add(B, "Persisted by an older version", now.AddMinutes(1), now);
        await legacy.RunDueAsync(now.AddMinutes(1), (item, token) => service.SendScheduledAsync(item.Target, item.Text, token), default);
        check(legacy.Messages.Single() is { State: CodexScheduleState.NeedsAttention, Issue: "CodexScheduleNotSent" } &&
            desktop.Opens == 0 && desktop.Sends == 0 && reader.Reads.Count == reads,
            "An older persisted foreign-chat schedule cannot bypass the origin boundary or send automatically");
        check((await service.GetVisibleAsync(default)).Target is null && (await service.SendAsync(B, "ordinary send", default)).Result == CodexDeliveryResult.NotSent,
            "Normal sends and legacy schedules still require HABITAT origin; external resumption is separate");
        origins.Register(B);
        var allowed = await service.ListScheduleTargetsAsync(default);
        check(allowed.Count == 2 && allowed[0] is { UsageLimited: true, ResumeAt: not null } && allowed[0].Target == B,
            "Among HABITAT-created chats, the latest usage-limit failure is prioritized with its known reset time");
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
            desktop.Sends == 1 && desktop.LastTarget == B && desktop.Text == "  Exact ☕\ntext " && origins.Contains(B),
            "Scheduled delivery submits once to the chosen HABITAT chat without changing its provenance or user text");
        reader.ResolveWrong = true;
        using var cancel = new CancellationTokenSource(80);
        try { await service.SendScheduledAsync(B, "must not send", cancel.Token); }
        catch (OperationCanceledException) { }
        check(desktop.Sends == 1, "Navigation that resolves to a different chat never sends to the currently visible conversation");
    }

    private static async Task CheckResumptionsAsync(string root, DateTimeOffset now, Action<bool, string> check)
    {
        var reader = new Reader(); var desktop = new Desktop(); var origins = new CodexChatOrigins(null);
        await using var service = new CodexChatService(desktop, reader, (_, _) => throw new Exception("Must not start a chat"), origins);
        var candidates = await service.ListScheduleTargetsAsync(default);
        check(candidates.Count == 1 && candidates[0].Target == B && candidates[0].InterruptedTurnId == "last",
            "External completed chats are excluded while the current usage-limit interruption is identifiable");
        reader.Running = true;
        check((await service.ListScheduleTargetsAsync(default)).Count == 0, "Running external chats are not offered as interrupted");
        reader.Running = false;
        var file = Path.Combine(root, "resumptions.json");
        var queue = new CodexScheduleQueue(file);
        var batch = new[] { candidates[0], new CodexScheduleTarget(A, true, reader.Reset, "other-turn") };
        queue.AddResumptions(batch, "  Continúa ☕  ", now.AddMinutes(1), now);
        queue = new CodexScheduleQueue(file);
        check(queue.Messages.Count == 2 && queue.Messages.All(item => item.Text == "  Continúa ☕  " && item.InterruptedTurnId is not null),
            "A multi-chat selection persists exact text and each interrupted turn atomically across restarts");
        try { queue.AddResumptions(batch, "Continue", now.AddMinutes(2), now); check(false, "Duplicate resumptions must fail"); }
        catch (InvalidOperationException) { check(queue.Messages.Count == 2, "A second resumption for the same pending turns cannot duplicate the batch"); }
        var blocked = Path.Combine(root, "blocked-resumptions"); Directory.CreateDirectory(blocked);
        var unsaved = new CodexScheduleQueue(blocked);
        try { unsaved.AddResumptions(batch, "Continue", now.AddMinutes(1), now); check(false, "Batch storage failure must fail closed"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { check(unsaved.Messages.Count == 0, "A failed batch save leaves no partial in-memory schedules"); }
        check((await service.SendScheduledAsync(B, "Continue", default, "last")).RetryAt == reader.Reset!.Value.AddSeconds(5) && desktop.Opens == 0,
            "An explicitly selected external interruption waits for the known account reset");
        reader.Reset = null;
        reader.LatestId = "continued";
        check((await service.SendScheduledAsync(B, "Continue", default, "last")).Result == CodexDeliveryResult.NotSent && desktop.Opens == 0,
            "A chat already continued by the user is never resumed again, even if its next turn also hits a limit");
        reader.LatestId = "last"; reader.Listed = false;
        check((await service.SendScheduledAsync(B, "Continue", default, "last")).Result == CodexDeliveryResult.NotSent && desktop.Opens == 0,
            "A chat archived or removed after scheduling is not reopened");
        reader.Listed = true; desktop.OnOpen = () => reader.LatestId = "racing-turn";
        check((await service.SendScheduledAsync(B, "Continue", default, "last")).Result == CodexDeliveryResult.NotSent && desktop.Sends == 0,
            "A new turn between navigation and delivery cancels the stale resumption");
        desktop.OnOpen = null; reader.LatestId = "last";
        check((await service.SendScheduledAsync(B, "Continúa", default, "last")).Result == CodexDeliveryResult.Sent && desktop.Sends == 1 &&
            desktop.LastTarget == B && !origins.Contains(B), "External resumption reaches the exact selected chat without adopting it into HABITAT history");
        var item = queue.Messages.First();
        queue.Reschedule(item.Id, item.Target, item.Text, now.AddMinutes(3), now);
        check(queue.Messages.First().InterruptedTurnId == item.InterruptedTurnId, "Editing the time preserves the interrupted-turn guard");
    }

    private sealed class Reader : ICodexHistoryReader
    {
        public List<string> Reads = [];
        public bool Running, ResolveWrong;
        public bool Listed = true;
        public string LatestId = "last";
        public CodexChatTarget? Created;
        public string? InitialText;
        public DateTimeOffset? Reset = DateTimeOffset.UtcNow.AddHours(1);
        public Task<IReadOnlyList<CodexChatTarget>> ListAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<CodexChatTarget>>(!Listed ? [] : Created is null ? [A, B] : [A, B, Created]);
        public Task<DateTimeOffset?> ReadLimitResetAsync(CancellationToken token) => Task.FromResult(Reset);
        public Task<CodexChatTarget?> ResolveAsync(IReadOnlyList<string> titles, CancellationToken token) => Task.FromResult<CodexChatTarget?>(Created ?? (ResolveWrong ? A : B));
        public Task<CodexChatHistory> ReadAsync(CodexChatTarget target, CancellationToken token)
        {
            Reads.Add(target.Id);
            if (target == Created) return Task.FromResult(new CodexChatHistory(target, [new("initial", "inProgress", [new("user", "YOU", InitialText!)])]));
            return Task.FromResult(new CodexChatHistory(target, [new(LatestId, Running ? "inProgress" : target == B ? "failed" : "completed", [],
                target == B ? "You've hit your usage limit" : null, target == B ? "UsageLimitExceeded" : null)]));
        }
    }
    private sealed class Desktop : ICodexChatDesktop
    {
        public bool Empty = true, NewChat, NoEditor;
        public int Opens, Sends;
        public CodexChatTarget? LastTarget;
        public string? Text;
        public Action? OnOpen;
        public Task<CodexVisibleChat?> ReadVisibleAsync(CancellationToken token) => Task.FromResult<CodexVisibleChat?>(NoEditor ? null : new(new(1, "editor", "send", Empty ? "" : "Protected draft", NewChat, true), [B.Title], Empty, true));
        public Task<bool> OpenChatAsync(CodexChatTarget? target, CancellationToken token) { Opens++; LastTarget = target; OnOpen?.Invoke(); return Task.FromResult(true); }
        public Task<CodexDeliveryResult> SubmitVisibleAsync(CodexVisibleChat expected, string text, CancellationToken token) { Sends++; Text = text; return Task.FromResult(CodexDeliveryResult.Sent); }
        public void ReturnFocus(nint ownerWindow) { }
    }
}
