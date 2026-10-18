using Habitat.Features.Codex;
using Habitat.Services;

internal static class CodexRetentionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var a = new CodexChatTarget(Guid.NewGuid().ToString(), "Pending chat");
        var b = new CodexChatTarget(Guid.NewGuid().ToString(), "Selected chat");
        var session = new CodexChatSession(maxCachedChats: 3);
        static CodexChatTurn Turn(string id, string text) => new(id, "completed", [new(id, "YOU", text), new(id + "a", "Codex", "reply")]);
        session.Select(a);
        session.Observe(new(a, [Turn("baseline", "old")]));
        session.Accept(new(CodexDeliveryResult.Sent, a, "baseline"), "literal pending ☕");
        session.Select(b);
        session.Observe(new(b, [Turn("selected", "keep selected")]));
        for (var index = 0; index < 100; index++)
        {
            var inactive = new CodexChatTarget(Guid.NewGuid().ToString(), "Inactive " + index);
            session.Observe(new(inactive, [Turn("inactive", "discardable")]));
            session.SetIssue(inactive, "Synthetic issue");
        }
        check(session.CachedChatCount == 3 && session.IsWaiting && session.Messages[0].Text == "keep selected",
            "Codex cache pressure preserves both the selected chat and an outstanding response in another chat");
        session.Observe(new(a, [Turn("baseline", "old"), Turn("ours", "serialized input")]));
        session.Select(a);
        check(!session.IsWaiting && session.Messages[^2].Text == "literal pending ☕",
            "A protected pending chat still correlates its late response and restores the literal instruction after cache eviction");
        var turns = new List<CodexChatTurn> { Turn("ours", "serialized input") };
        for (var index = 0; index < 60; index++)
        {
            session.Accept(new(CodexDeliveryResult.Sent, a, turns[^1].Id), "original " + index);
            turns.Add(Turn("turn-" + index, "serialized input"));
            session.Observe(new(a, turns));
        }
        check(session.Current!.Turns.Count == 20 && session.OriginalTextCount == 20 && session.Messages[^2].Text == "original 59",
            "Refreshing a long Codex chat retires original texts outside its twenty-turn history without truncating current messages");
        session.SetIssue(a, "retire this issue too");
        session.Select(b);
        for (var index = 0; index < 10; index++)
            session.Observe(new(new(Guid.NewGuid().ToString(), "Another chat"), [Turn("other", "value")]));
        session.Select(a);
        check(session.Current is null && session.Issue.Length == 0 && session.OriginalTextCount == 0,
            "Evicting an inactive Codex chat removes its history, issues and original-text cache together");
    }
}
