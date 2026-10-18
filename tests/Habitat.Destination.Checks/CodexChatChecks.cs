using System.Text.Json;
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

        var desktop = new Desktop(); var reader = new Reader(); var newChats = 0;
        var newResult = CodexDeliveryResult.NotSent;
        await using var service = new CodexChatService(desktop, reader, (_, _) =>
        { newChats++; if (newResult == CodexDeliveryResult.Sent) desktop.Visible = Desktop.Ready(); return Task.FromResult(newResult); });
        check((await service.GetVisibleAsync(default)).Target == A, "Codex links only the visible sidebar selection to its exact local chat identity");
        var sent = await service.SendAsync(A, "follow up", default);
        check(sent.Result == CodexDeliveryResult.Sent && sent.Target == A && sent.PreviousTurnId == "before" && desktop.Sends == 1 && newChats == 0,
            "Codex follow-up uses the visible existing chat and records its previous turn without opening a new chat");
        reader.Target = B;
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
        reader.Target = A with { CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        check((await service.SendAsync(null, "new chat", default)).Target == reader.Target && newChats == 1,
            "A confirmed new Codex chat links its response only after resolving a recently created local chat");
        desktop.Visible = null; reader.Target = B with { CreatedAt = 1 };
        check((await service.SendAsync(null, "changed after send", default)).Result == CodexDeliveryResult.Unconfirmed && newChats == 2,
            "Switching to an older chat during new-chat confirmation cannot bind the pending response to that chat or resend");
        await CheckReaderAsync(check);
    }

    private static async Task CheckReaderAsync(Action<bool, string> check)
    {
        await using var client = new CodexReadClient(Environment.ProcessPath!);
        var target = await client.ResolveAsync(["Home", "Chat A"], default);
        var history = await client.ReadAsync(target!, default);
        check(target == A && history.Turns.Single().Messages.Single().Text == "synthetic reply",
            "Codex reader completes the JSON-RPC handshake and reads only the exactly selected chat through a private process");
        try { await client.ResolveAsync(["Duplicate"], default); check(false, "Duplicate Codex title must be rejected"); }
        catch (InvalidDataException) { check(true, "Duplicate Codex titles fail closed instead of guessing a chat"); }
        try { await client.ResolveAsync(["Truncated"], default); check(false, "Truncated title search must be rejected"); }
        catch (InvalidDataException) { check(true, "A truncated Codex title search cannot claim an unambiguous chat identity"); }
        try { await client.ResolveAsync(["Disconnect"], default); check(false, "The synthetic reader must disconnect"); }
        catch (IOException) { }
        check(await client.ResolveAsync(["Chat A"], default) == A, "The read-only Codex connection recovers after its private process disconnects");
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
            if (method == "thread/list" && root.GetProperty("params").GetProperty("searchTerm").GetString() == "Disconnect")
                Environment.Exit(0);
            object result = method switch
            {
                "initialize" => new { userAgent = "synthetic", codexHome = "isolated" },
                "thread/list" => root.GetProperty("params").GetProperty("searchTerm").GetString() switch
                {
                    "Chat A" => new { data = new[] { new { id = A.Id, name = "Chat A" }, new { id = B.Id, name = "Chat A extra" } } },
                    "Duplicate" => new { data = new[] { new { id = A.Id, name = "Duplicate" }, new { id = B.Id, name = "Duplicate" } } },
                    "Truncated" => (object)new { data = new[] { new { id = A.Id, name = "Truncated" } }, nextCursor = "more" },
                    _ => new { data = Array.Empty<object>() }
                },
                "thread/turns/list" => new { data = new[] { new { id = "one", status = "completed", items = new[] {
                    new { id = "reply", type = "agentMessage", phase = "final_answer", text = "synthetic reply" } } } } },
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
        public Task<CodexChatTarget?> ResolveAsync(IReadOnlyList<string> titles, CancellationToken token) => Task.FromResult<CodexChatTarget?>(Target);
        public Task<CodexChatHistory> ReadAsync(CodexChatTarget target, CancellationToken token) =>
            Task.FromResult(new CodexChatHistory(target, [Turn("before", Running ? "inProgress" : "completed", "old reply")]));
    }
}
