using System.Text.Json;
using Habitat.Features.Codex;
using Habitat.Services;

internal static class CodexLinkedChatChecks
{
    private static readonly CodexChatTarget A = new("11111111-1111-1111-1111-111111111111", "Linked chat");
    private static readonly CodexChatTarget B = new("22222222-2222-2222-2222-222222222222", "Other chat");

    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "isolated-codex", Guid.NewGuid().ToString("N"));
        var paths = AppDataPaths.Isolated(root);
        var origins = new CodexChatOrigins(paths.CodexChatsPath);
        origins.Register(A, link: true);
        origins.Register(B);
        var desktop = new Desktop { Current = B };
        var reader = new Reader();
        var starts = 0;
        await using var service = new CodexChatService(desktop, reader, (_, _) =>
        {
            starts++; desktop.Current = reader.Created = new(Guid.NewGuid().ToString(), "New chat", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return Task.FromResult(CodexDeliveryResult.Sent);
        }, origins);
        foreach (var visible in new CodexChatTarget?[] { B, null, A, B })
        {
            desktop.Current = visible;
            var linked = await service.GetLinkedAsync(default);
            check(linked.Target?.Id == A.Id && !linked.CanStartNew && desktop.Reads == 0,
                "HABITAT reads its pinned chat by ID without inspecting Codex navigation or its new-chat page");
            check((await service.ReadAsync(linked.Target!, default)).Target.Id == A.Id && reader.Reads.All(id => id == A.Id),
                "Only the linked chat's response is read when another conversation is visible");
        }
        reader.Metadata = A with { Title = "Renamed chat", Settings = new("model-changed", "high") };
        check((await service.GetLinkedAsync(default)).Target == reader.Metadata,
            "Pinned chat metadata refreshes by stable ID after renaming or changing model settings");
        var reopenedOrigins = new CodexChatOrigins(paths.CodexChatsPath);
        await using var restarted = new CodexChatService(desktop, reader, (_, _) => throw new Exception("No new chat expected"), reopenedOrigins);
        check((await restarted.GetLinkedAsync(default)).Target?.Id == A.Id && desktop.Reads == 0,
            "Restart restores the linked chat even with a different Codex chat open");

        var delivered = await service.SendAsync(A, "follow up", default);
        check(delivered.Result == CodexDeliveryResult.Sent && delivered.Target == A && delivered.PreviousTurnId == "before" &&
            desktop.Opened == A && desktop.Sends == 1 && starts == 0,
            "A follow-up reopens and verifies HABITAT's chat instead of sending into the visible conversation");
        desktop.Current = B; desktop.Draft = "Private Codex draft";
        check((await service.SendAsync(A, "keep draft", default)).Result == CodexDeliveryResult.NotSent && desktop.Sends == 1,
            "Reopening the pinned chat never overwrites a draft in its editor");
        desktop.Draft = ""; desktop.Current = B; desktop.Navigate = false;
        using (var cancellation = new CancellationTokenSource(80))
        {
            try { await service.SendAsync(A, "wrong navigation", cancellation.Token); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
        check(desktop.Sends == 1 && starts == 0,
            "Cancelled navigation to a mismatched identity cannot submit or create a replacement conversation");
        desktop.Navigate = true; reader.Running = true;
        check((await service.SendAsync(A, "busy", default)).Result == CodexDeliveryResult.NotSent && desktop.Sends == 1,
            "Navigating back to a running pinned chat cannot interrupt its turn");
        reader.Running = false;

        await service.ClearLinkedAsync(default);
        check((await service.GetLinkedAsync(default)) is { Target: null, CanStartNew: true } && origins.Contains(A),
            "Explicit New chat clears the selected link without deleting the previous Codex conversation or its origin");
        await using (var cleared = new CodexChatService(desktop, reader, (_, _) => throw new Exception("No send expected"), new(paths.CodexChatsPath)))
            check((await cleared.GetLinkedAsync(default)).Target is null,
                "An explicitly cleared selection stays empty after restart, even when the old chat is still visible");
        check((await service.SendAsync(null, "first instruction", default)) is { Result: CodexDeliveryResult.Sent, Target: not null } &&
            starts == 1 && desktop.Sends == 1 && origins.Linked == reader.Created,
            "Sending after New chat creates and persists a separate conversation rather than reusing the old visible one");

        var legacyPath = Path.Combine(root, "legacy.json");
        File.WriteAllText(legacyPath, JsonSerializer.Serialize(new { Version = 1, ChatIds = new[] { A.Id, B.Id } }));
        var legacy = new CodexChatOrigins(legacyPath);
        desktop.Current = A;
        await using (var upgraded = new CodexChatService(desktop, reader, (_, _) => throw new Exception("No send expected"), legacy))
        {
            check((await upgraded.GetLinkedAsync(default)).Target?.Id == A.Id,
                "An older origin registry upgrades its registered visible chat to a persistent link once");
            desktop.Current = B;
            check((await upgraded.GetLinkedAsync(default)).Target?.Id == A.Id,
                "After migration Codex navigation no longer changes the selected HABITAT conversation");
        }
        var blocked = new CodexChatOrigins(root);
        try { blocked.Select(null); check(false, "Saving a selection to a directory must fail"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        check(blocked.Linked is null && !blocked.Contains(A), "A failed selection save cannot authorize a chat");
        File.WriteAllText(legacyPath, JsonSerializer.Serialize(new { Version = 1, ChatIds = new[] { A.Id } }));
        desktop.Current = B;
        await using (var upgraded = new CodexChatService(desktop, reader, (_, _) => throw new Exception("No send expected"), new(legacyPath)))
            check((await upgraded.GetLinkedAsync(default)).Target?.Id == A.Id,
                "An older registry recovers its last available HABITAT chat even when Codex shows a foreign conversation");
        var session = new CodexChatSession();
        session.Select(A);
        session.Accept(new(CodexDeliveryResult.Sent, A), "pending");
        session.Reset();
        session.Observe(new(A, [new("late", "completed", [new("reply", "Codex", "late reply")])]));
        check(session.Selected is null && !session.IsWaiting && session.Messages.Count == 0,
            "A late response after New chat cannot restore the previous conversation or its pending wait");
    }

    private sealed class Reader : ICodexHistoryReader
    {
        public CodexChatTarget Metadata = A;
        public CodexChatTarget? Created;
        public bool Running;
        public List<string> Reads = [];
        public Task<IReadOnlyList<CodexChatTarget>> ListAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<CodexChatTarget>>([Metadata, B]);
        public Task<CodexChatTarget?> ResolveAsync(IReadOnlyList<string> titles, CancellationToken token) =>
            Task.FromResult(new[] { A, B, Created }.FirstOrDefault(target => target is not null && titles.Contains(target.Title)));
        public Task<CodexChatHistory> ReadAsync(CodexChatTarget target, CancellationToken token)
        {
            Reads.Add(target.Id);
            return Task.FromResult(new CodexChatHistory(target, [new("before", Running ? "inProgress" : "completed",
                [new("user", "YOU", target == Created ? "first instruction" : "old instruction"), new("reply", "Codex", "response")])]));
        }
    }

    private sealed class Desktop : ICodexChatDesktop
    {
        public CodexChatTarget? Current, Opened;
        public bool Navigate = true;
        public string Draft = "";
        public int Reads, Sends;
        public Task<CodexVisibleChat?> ReadVisibleAsync(CancellationToken token)
        {
            Reads++;
            return Task.FromResult<CodexVisibleChat?>(new(new(123, "editor", "send", Draft, Current is null, true),
                Current is null ? [] : [Current.Title], Draft.Length == 0, true));
        }
        public Task<bool> OpenChatAsync(CodexChatTarget? target, CancellationToken token)
        { Opened = target; if (Navigate) Current = target; return Task.FromResult(true); }
        public Task<CodexDeliveryResult> SubmitVisibleAsync(CodexVisibleChat expected, string text, CancellationToken token)
        { Sends++; return Task.FromResult(CodexDeliveryResult.Sent); }
        public void ReturnFocus(nint ownerWindow) { }
    }
}
