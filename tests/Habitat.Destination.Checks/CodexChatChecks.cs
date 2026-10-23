using System.Text.Json;
using Habitat;
using Habitat.Features.Codex;
using Habitat.Services;

internal static class CodexChatChecks
{
    private static readonly CodexChatTarget A = new("11111111-1111-1111-1111-111111111111", "Chat A");
    private static readonly CodexChatTarget B = new("22222222-2222-2222-2222-222222222222", "Chat B");
    private static CodexChatTurn Turn(string id, string status, string text) => new(id, status,
        [new(id + "u", "YOU", "serialized input"), new(id + "a", "Codex", text)]);

    internal static async Task RunAsync(Action<bool, string> check)
    {
        CodexSettingsChecks.Run(check);
        using var fixture = JsonDocument.Parse("""
        {"data":[{"id":"new","status":"completed","items":[
          {"id":"user","type":"userMessage","content":[{"type":"text","text":"original Español"},{"type":"image","url":"ignored"}]},
          {"id":"plan","type":"agentMessage","phase":"commentary","text":"private progress"},
          {"id":"tool","type":"commandExecution","aggregatedOutput":"not a reply"},
          {"id":"answer","type":"agentMessage","phase":"final_answer","text":"  Respuesta ☕\nsecond line  "}]},
          {"id":"old","status":"completed","items":[]}]}
        """);
        var parsed = CodexReadClient.ParseHistory(A, fixture.RootElement);
        check(parsed.Turns.Select(turn => turn.Id).SequenceEqual(["old", "new"]) && parsed.Turns[1].Messages.Count == 2 &&
            parsed.Turns[1].Messages[0].Text == "original Español" && parsed.Turns[1].Messages[1].Text == "  Respuesta ☕\nsecond line  ",
            "Codex history keeps chronological user and final assistant text, Unicode and whitespace without treating tools or commentary as replies");

        var session = new CodexChatSession();
        session.Select(A); session.Observe(new(A, [Turn("before", "completed", "old answer")]));
        session.Accept(new(CodexDeliveryResult.Sent, A, "before"), "  literal **input**\rnext  ");
        session.Select(B); session.Observe(new(B, [Turn("other", "completed", "B answer")]));
        session.Observe(new(A, [Turn("before", "completed", "old answer"), Turn("ours", "inProgress", "partial")]));
        check(session.IsWaiting && session.Messages.Last().Text == "B answer",
            "Switching visible Codex chats cannot move an in-progress reply into the selected chat");
        session.Observe(new(A, [Turn("before", "completed", "old answer"), Turn("ours", "completed", "A final answer")]));
        check(!session.IsWaiting && session.Messages.Last().Text == "B answer", "A late Codex completion unlocks sending without replacing the visible chat history");
        session.Select(A);
        check(session.Messages[^2].Text == "  literal **input**\rnext  " && session.Messages[^1].Text == "A final answer",
            "Returning to a Codex chat recovers its own reply and the original HABITAT instruction, not serialized Markdown escapes");
        session.Accept(new(CodexDeliveryResult.Sent, A, "missing-baseline"), "do not associate");
        session.Observe(new(A, [Turn("unrelated", "completed", "do not accept")]));
        check(session.IsWaiting, "Codex cannot acknowledge an unrelated turn when the submission baseline is missing");
        session.StopWaiting();
        check(!session.IsWaiting, "Stopping the local Codex wait does not require executing or interrupting a turn");
        session.Accept(new(CodexDeliveryResult.Unconfirmed, A), "uncertain");
        check(!session.IsWaiting, "An uncertain Codex send never creates a second request or invents a confirmed pending turn");
        CheckOutcomeRefresh(check);

        var desktop = new Desktop(); var reader = new Reader { InitialText = "new chat\n" }; var newChats = 0;
        var newResult = CodexDeliveryResult.NotSent;
        var origins = new CodexChatOrigins(null);
        await using var service = new CodexChatService(desktop, reader, (_, _) =>
        { newChats++; if (newResult == CodexDeliveryResult.Sent) desktop.Visible = Desktop.Ready(); return Task.FromResult(newResult); }, origins);
        var foreign = await service.GetVisibleAsync(default);
        check(foreign.Target is null && foreign.CanStartNew && foreign.Issue is null && reader.Reads == 0,
            "A visible Codex chat started outside HABITAT is silently ignored without reading any messages");
        check((await service.ReadAsync(A, default)).Turns.Count == 0 && reader.Reads == 0 &&
            (await service.SendAsync(A, "forced foreign follow-up", default)).Result == CodexDeliveryResult.NotSent && desktop.Sends == 0 && newChats == 0,
            "Foreign chat IDs cannot bypass origin checks through direct history reads or follow-up submissions");
        desktop.Visible = null;
        newResult = CodexDeliveryResult.Sent;
        reader.Target = A with { CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        check((await service.SendAsync(null, "new chat", default)).Target == reader.Target && origins.Contains(A),
            "A confirmed HABITAT delivery records its origin even when Codex history appends its final paragraph newline");
        reader.Target = A; newChats = 0; newResult = CodexDeliveryResult.NotSent;
        check((await service.GetVisibleAsync(default)).Target == A, "Codex links the visible sidebar selection only when HABITAT started that exact chat identity");
        var sent = await service.SendAsync(A, "follow up", default);
        check(sent.Result == CodexDeliveryResult.Sent && sent.Target == A && sent.PreviousTurnId == "before" && desktop.Sends == 1 && newChats == 0,
            "Codex follow-up uses the visible existing chat and records its previous turn without opening a new chat");
        reader.Target = B;
        var readsBeforeForeign = reader.Reads;
        check((await service.GetVisibleAsync(default)).Target is null && (await service.ReadAsync(B, default)).Turns.Count == 0 &&
            reader.Reads == readsBeforeForeign,
            "Switching from a linked chat to an external Codex conversation imports no external history or replies");
        check((await service.SendAsync(A, "stale context", default)).Result == CodexDeliveryResult.NotSent && desktop.Sends == 1,
            "A changed Codex chat before submission preserves the instruction instead of sending into another context");
        reader.Target = A; desktop.Visible = desktop.Visible! with { IsEmpty = false, Composer = desktop.Visible.Composer with { Text = "another Codex draft" } };
        check((await service.SendAsync(A, "keep their draft", default)).Result == CodexDeliveryResult.NotSent && desktop.Sends == 1,
            "A draft written directly in Codex is never overwritten by HABITAT");
        desktop.Visible = Desktop.Ready(); reader.Running = true;
        check((await service.SendAsync(A, "busy", default)).Result == CodexDeliveryResult.NotSent && desktop.Sends == 1,
            "HABITAT does not interrupt or queue input into an already running Codex turn");
        reader.Running = false; desktop.Result = CodexDeliveryResult.Unconfirmed;
        check((await service.SendAsync(A, "once", default)).Result == CodexDeliveryResult.Unconfirmed && desktop.Sends == 2 && newChats == 0,
            "A missing Codex acknowledgement never retries the instruction in the same or a new chat");
        desktop.Visible = null;
        check((await service.SendAsync(A, "lost window", default)).Result == CodexDeliveryResult.NotSent && newChats == 0,
            "Losing the selected Codex window never replaces its context with a new chat");
        newResult = CodexDeliveryResult.Sent;
        reader.Target = new("33333333-3333-3333-3333-333333333333", "New HABITAT chat", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        check((await service.SendAsync(null, "new chat", default)).Target == reader.Target && newChats == 1,
            "A confirmed new Codex chat links its response only after resolving a recently created local chat");
        desktop.Visible = null; reader.Target = B with { CreatedAt = 1 };
        check((await service.SendAsync(null, "changed after send", default)) is { Result: CodexDeliveryResult.Sent, Target: null } && newChats == 2,
            "Switching to an older chat after acknowledged delivery cannot downgrade Sent, link the wrong chat or resend");
        desktop.Visible = null; reader.Target = B with { CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        check((await service.SendAsync(null, "different instruction", default)) is { Result: CodexDeliveryResult.Sent, Target: null } && !origins.Contains(B) &&
            (await service.GetVisibleAsync(default)).Target is null,
            "Even a newly created external chat cannot be adopted when its initial instruction differs from HABITAT's delivery");
        await CheckOriginsAsync(check);
        await CheckForeignStartAsync(check);
        await CheckReaderAsync(check);
    }

    private static void CheckOutcomeRefresh(Action<bool, string> check)
    {
        var language = UiText.Language;
        try
        {
            foreach (var currentLanguage in new[] { "en", "es" })
            {
                UiText.SetLanguage(currentLanguage);
                var session = new CodexChatSession();
                session.Select(A);
                session.Accept(new(CodexDeliveryResult.Sent, A), "original");
                session.Observe(new(A, [Turn("ours", "interrupted", "")]));
                check(!session.IsWaiting && session.Issue == UiText.Get("TheCodexTurnDidNotCompleteOpenThe"),
                    "An interrupted Codex turn retains its localized warning: " + currentLanguage);
                session.Select(B);
                session.Observe(new(A, [Turn("ours", "completed", "final reply")]));
                check(session.Issue.Length == 0, "A background completion cannot add a notice to another chat");
                session.Select(A);
                check(session.Issue.Length == 0 && session.Messages.Last().Text == "final reply",
                    "A completed snapshot clears a previous interrupted warning after waiting has ended");
                session.Observe(new(A, [Turn("ours", "completed", "  ")]));
                check(session.Issue == UiText.Get("CodexFinishedWithoutATextReplyOpenThe"),
                    "A completed Codex turn without text reports its localized empty reply notice");
                session.Observe(new(A, [Turn("ours", "completed", "late final text")]));
                check(session.Issue.Length == 0, "Final text arriving after completion clears the empty reply notice");
                session.Observe(new(A, [Turn("ours", "failed", "partial answer")]));
                check(session.Issue == UiText.Get("TheCodexTurnDidNotCompleteOpenThe"),
                    "Partial assistant text cannot conceal a real failed turn");
                session.SetIssue(A, UiText.Get("CouldNotReadTheCodexResponseRetrying"));
                session.Observe(new(A, [Turn("ours", "completed", "recovered")]));
                check(session.Issue.Length == 0, "A successful read recovers both connection and stale turn notices");
                session.Observe(new(A, [Turn("ours", "failed", ""), Turn("next", "inProgress", "")]));
                check(session.Issue.Length == 0 && session.Current!.IsRunning,
                    "A later Codex turn cannot inherit the previous turn's failure notice");
            }
        }
        finally { UiText.SetLanguage(language); }
    }

    private static async Task CheckForeignStartAsync(Action<bool, string> check)
    {
        var created = A with { CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        const string instruction = "  Literal **text** ☕\rnext line  ";
        var desktop = new Desktop();
        desktop.Visible = desktop.Visible! with { IsEmpty = false, Composer = desktop.Visible.Composer with { Text = "Private Codex draft" } };
        var reader = new Reader { Target = B, InitialText = instruction.Replace('\r', '\n') };
        var origins = new CodexChatOrigins(null);
        var starts = 0;
        await using var service = new CodexChatService(desktop, reader, (_, _) =>
        {
            starts++;
            reader.Target = created;
            desktop.Visible = Desktop.Ready();
            return Task.FromResult(CodexDeliveryResult.Sent);
        }, origins);
        var result = await service.SendAsync(null, instruction, default);
        check(result.Target == created && starts == 1 && desktop.Sends == 0 && origins.Contains(A) && !origins.Contains(B) &&
            reader.ReadTargets.All(id => id == A.Id),
            "Sending with an external chat and private draft visible starts a separate HABITAT chat without reading or submitting to the external chat");

        foreach (var delivery in new[] { CodexDeliveryResult.NotSent, CodexDeliveryResult.Unconfirmed, CodexDeliveryResult.Cancelled })
        {
            var rejected = new CodexChatOrigins(null);
            var freshDesktop = new Desktop { Visible = null };
            await using var unconfirmed = new CodexChatService(freshDesktop, reader, (_, _) => Task.FromResult(delivery), rejected);
            check((await unconfirmed.SendAsync(null, instruction, default)).Result == delivery && !rejected.Contains(A),
                "A new-chat delivery with result " + delivery + " cannot register a chat origin");
        }

        var root = Path.Combine(AppContext.BaseDirectory, "isolated-codex", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var blocked = new CodexChatOrigins(root);
        var emptyDesktop = new Desktop { Visible = null };
        await using var cannotPersist = new CodexChatService(emptyDesktop, reader, (_, _) =>
        {
            emptyDesktop.Visible = Desktop.Ready();
            return Task.FromResult(CodexDeliveryResult.Sent);
        }, blocked);
        check((await cannotPersist.SendAsync(null, instruction, default)) is { Result: CodexDeliveryResult.Sent, Target: null } &&
            !blocked.Contains(A) && (await cannotPersist.GetVisibleAsync(default)).Target is null,
            "An acknowledged delivery stays Sent when saving its origin fails, without retrying or importing an unlinked response");

        foreach (var failure in new Exception[] { new IOException(), new OperationCanceledException(), new InvalidOperationException() })
        {
            var noOrigins = new CodexChatOrigins(null);
            var unresolvedReader = new Reader { ResolveFailure = failure };
            var freshDesktop = new Desktop { Visible = null };
            var sends = 0;
            await using var acknowledged = new CodexChatService(freshDesktop, unresolvedReader, (_, _) =>
            { sends++; freshDesktop.Visible = Desktop.Ready(); return Task.FromResult(CodexDeliveryResult.Sent); }, noOrigins);
            check((await acknowledged.SendAsync(null, instruction, default)) is { Result: CodexDeliveryResult.Sent, Target: null } && sends == 1 && !noOrigins.Contains(A),
                "Post-delivery chat linking failures cannot downgrade Sent or repeat the message: " + failure.GetType().Name);
        }
        check(CodexDelivery.SameHistoryText("  literal\n", "  literal") &&
            !CodexDelivery.SameHistoryText("literal ", "literal") && !CodexDelivery.SameHistoryText(" literal\n", "literal") &&
            !CodexDelivery.SameHistoryText("literal\n\n", "literal") && !CodexDelivery.SameText("literal\n", "literal"),
            "History receipts allow one appended paragraph newline without relaxing editor checks or trimming user whitespace");
    }

    private static async Task CheckOriginsAsync(Action<bool, string> check)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "isolated-codex", Guid.NewGuid().ToString("N"));
        var paths = AppDataPaths.Isolated(root);
        var origins = new CodexChatOrigins(paths.CodexChatsPath);
        check(!origins.Contains(A) && !origins.Contains(B), "A missing Codex origin registry never adopts existing conversations");
        origins.Register(A);
        var reopened = new CodexChatOrigins(paths.CodexChatsPath);
        var reader = new Reader();
        await using var service = new CodexChatService(new Desktop(), reader,
            (_, _) => throw new InvalidOperationException("No new delivery expected"), reopened);
        check((await service.GetVisibleAsync(default)).Target == A && (await service.ReadAsync(A, default)).Turns.Count == 1 && !reopened.Contains(B),
            "Restarting HABITAT restores only persisted chat origins from isolated storage");
        reader.Target = B with { Title = A.Title };
        check((await service.GetVisibleAsync(default)).Target is null && (await service.ReadAsync(B, default)).Turns.Count == 0,
            "An external chat with the same title as a HABITAT chat remains excluded after restart");
        reader.Target = A with { Title = "Renamed in Codex" };
        check((await service.GetVisibleAsync(default)).Target == reader.Target,
            "Renaming a HABITAT chat preserves its origin because provenance uses the exact chat ID");
        foreach (var invalid in new[] { "{broken", "null", "{}", "{\"Version\":2,\"ChatIds\":[\"" + B.Id + "\"]}",
            "{\"Version\":1,\"ChatIds\":[\"" + B.Id + "\",\"invalid\"]}" })
        {
            File.WriteAllText(paths.CodexChatsPath, invalid);
            var rejected = new CodexChatOrigins(paths.CodexChatsPath);
            check(!rejected.Contains(A) && !rejected.Contains(B), "An invalid Codex origin registry grants access to no conversations");
        }
        var blocked = new CodexChatOrigins(root); // A directory cannot be replaced by a registry file.
        try { blocked.Register(B); check(false, "Origin persistence must fail for a directory path"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        check(!blocked.Contains(B), "Failed origin persistence never authorizes a chat in memory");
    }

    private static async Task CheckReaderAsync(Action<bool, string> check)
    {
        await using var client = new CodexReadClient(Environment.ProcessPath!);
        var target = await client.ResolveAsync(["Home", "Chat A"], default);
        var history = await client.ReadAsync(target!, default);
        var listed = await client.ListAsync(default);
        var reset = await client.ReadLimitResetAsync(default);
        check(listed.Select(item => item.Id).SequenceEqual(new[] { A.Id, B.Id }) &&
            listed[0].Settings == new CodexChatSettings("test-model", "high") && listed[1].Settings is null &&
            reset > DateTimeOffset.UtcNow && reset < DateTimeOffset.UtcNow.AddHours(2),
            "Codex scheduling pages through local chat identities and reads account reset metadata using read-only RPC calls");
        check(target?.Id == A.Id && history.Turns.Single().Messages.Single().Text == "synthetic reply" &&
            target.Settings == new CodexChatSettings("test-model", "high"),
            "Codex reader completes the JSON-RPC handshake and reads only the exactly selected chat through a private process");
        try { await client.ResolveAsync(["Duplicate"], default); check(false, "Duplicate Codex title must be rejected"); }
        catch (InvalidDataException) { check(true, "Duplicate Codex titles fail closed instead of guessing a chat"); }
        try { await client.ResolveAsync(["Truncated"], default); check(false, "Truncated title search must be rejected"); }
        catch (InvalidDataException) { check(true, "A truncated Codex title search cannot claim an unambiguous chat identity"); }
        try { await client.ResolveAsync(["Disconnect"], default); check(false, "The synthetic reader must disconnect"); }
        catch (IOException) { }
        check((await client.ResolveAsync(["Chat A"], default))?.Id == A.Id, "The read-only Codex connection recovers after its private process disconnects");
        foreach (var malformed in new[] { "Array frame", "Null frame", "Invalid JSON" })
        {
            try { await client.ResolveAsync([malformed], default); check(false, "Malformed reader output must fail"); }
            catch (IOException) { }
            check((await client.ResolveAsync(["Chat A"], default))?.Id == A.Id,
                "The Codex reader recovers after malformed output: " + malformed);
        }
        check((await client.ResolveAsync(["Verbose diagnostics", "Chat A"], default))?.Id == A.Id,
            "Verbose helper diagnostics are drained without blocking read-only requests or shutdown");
    }

    internal static async Task ReaderServerAsync()
    {
        while (await Console.In.ReadLineAsync() is { } line)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var method = root.GetProperty("method").GetString();
            if (method == "initialized") continue;
            var id = root.GetProperty("id").GetInt64();
            var search = method == "thread/list" && root.GetProperty("params").TryGetProperty("searchTerm", out var term) ? term.GetString() : null;
            if (method == "thread/list" && search == "Verbose diagnostics")
            {
                for (var index = 0; index < 128; index++) await Console.Error.WriteLineAsync(new string('x', 4096));
                await Console.Error.FlushAsync();
            }
            if (method == "thread/list" && search == "Disconnect")
                Environment.Exit(0);
            if (method == "thread/list" && search is
                "Array frame" or "Null frame" or "Invalid JSON")
            {
                var malformed = search switch
                { "Array frame" => "[]", "Null frame" => "null", _ => "{broken" };
                await Console.Out.WriteLineAsync(malformed);
                await Console.Out.FlushAsync();
                continue;
            }
            object result = method switch
            {
                "initialize" => new { userAgent = "synthetic", codexHome = "isolated" },
                "thread/list" => search switch
                {
                    null => root.GetProperty("params").GetProperty("cursor").ValueKind == JsonValueKind.Null
                        ? (object)new { data = new[] { new { id = A.Id, name = A.Title, model = "test-model", reasoningEffort = "high" } }, nextCursor = "page2" }
                        : new { data = new[] { new { id = B.Id, name = B.Title } }, nextCursor = (string?)null },
                    "Chat A" => new { data = new[] { new { id = A.Id, name = "Chat A", model = "test-model", reasoningEffort = "high" },
                        new { id = B.Id, name = "Chat A extra", model = "other-model", reasoningEffort = "low" } } },
                    "Duplicate" => new { data = new[] { new { id = A.Id, name = "Duplicate" }, new { id = B.Id, name = "Duplicate" } } },
                    "Truncated" => (object)new { data = new[] { new { id = A.Id, name = "Truncated" } }, nextCursor = "more" },
                    _ => new { data = Array.Empty<object>() }
                },
                "thread/turns/list" => new { data = new[] { new { id = "one", status = "completed", items = new[] {
                    new { id = "reply", type = "agentMessage", phase = "final_answer", text = "synthetic reply" } } } } },
                "account/rateLimits/read" => new { rateLimits = new { primary = new { usedPercent = 100,
                    resetsAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() } } },
                _ => throw new InvalidOperationException("The reader attempted a non-read operation.")
            };
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { id, result }));
            await Console.Out.FlushAsync();
        }
    }

    private sealed class Desktop : ICodexChatDesktop
    {
        public static CodexVisibleChat Ready() => new(new(123, "editor", "send", "placeholder", false, false), ["Home", "Chat A"], true, true);
        public CodexVisibleChat? Visible = Ready();
        public int Sends;
        public CodexDeliveryResult Result = CodexDeliveryResult.Sent;
        public Task<CodexVisibleChat?> ReadVisibleAsync(CancellationToken token) => Task.FromResult(Visible);
        public Task<CodexDeliveryResult> SubmitVisibleAsync(CodexVisibleChat expected, string text, CancellationToken token) { Sends++; return Task.FromResult(Result); }
        public Task<bool> OpenChatAsync(CodexChatTarget? target, CancellationToken token) => Task.FromResult(true);
        public void ReturnFocus(nint ownerWindow) { }
    }
    private sealed class Reader : ICodexHistoryReader
    {
        public CodexChatTarget Target = A;
        public bool Running;
        public int Reads;
        public string InitialText = "new chat";
        public Exception? ResolveFailure;
        public List<string> ReadTargets = [];
        public Task<CodexChatTarget?> ResolveAsync(IReadOnlyList<string> titles, CancellationToken token) => ResolveFailure is { } error
            ? Task.FromException<CodexChatTarget?>(error) : Task.FromResult<CodexChatTarget?>(Target);
        public Task<CodexChatHistory> ReadAsync(CodexChatTarget target, CancellationToken token)
        {
            Reads++;
            ReadTargets.Add(target.Id);
            return Task.FromResult(new CodexChatHistory(target, [new("before", Running ? "inProgress" : "completed",
                [new("user", "YOU", InitialText), new("reply", "Codex", "old reply")])]));
        }
    }
}
