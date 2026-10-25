#if HABITAT_UI_CHECKS
using Habitat.Features.Search;
using Habitat.Features.Translation;
using Habitat.Features.Destinations;
using Habitat.Features.Conversation;
using Habitat.Features.Codex;
using Habitat.Services;
using Microsoft.UI.Xaml.Controls;

namespace Habitat;

public sealed partial class MainPage
{
    private async Task CheckSectionShutdownAsync(string logPath)
    {
        var translationReply = new TaskCompletionSource<TranslationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var translation = new TranslateSection((_, _, _) => translationReply.Task, _ => true, _ => { });
        var translating = translation.SubmitAsync("synthetic original");
        var translationStop = translation.StopAsync();
        await translating;
        if (translationStop.IsCompleted || !ReferenceEquals(translationStop, translation.StopAsync()))
            throw new InvalidOperationException("Translation shutdown must wait for its provider even after its cancelled UI wait finishes.");
        translationReply.SetResult(new("late result", null));
        await translationStop;
        if (translation.ResultText.Length != 0 || translation.IsBusy)
            throw new InvalidOperationException("A late translation must not update a stopped section.");

        var searchReply = new TaskCompletionSource<SearchPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var search = new SearchSection((_, _, _) => searchReply.Task, _ => navigation.Task, _ => { });
        var searching = search.SubmitAsync("synthetic query");
        var opening = search.OpenSearchAsync();
        var searchStop = search.StopAsync();
        await searching;
        searchReply.SetResult(new([]));
        await Task.Yield();
        if (searchStop.IsCompleted) throw new InvalidOperationException("Search shutdown must also drain in-flight browser navigation.");
        navigation.SetResult(false);
        await opening;
        await searchStop;
        if (search.Results.Count != 0 || search.StatusText.Contains("Could not open"))
            throw new InvalidOperationException("Stopped search must not render provider or navigation completions.");

        var discover = new TaskCompletionSource<IReadOnlyList<VSCodeProject>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var vscode = new VSCodeSection(_ => discover.Task, (_, _, _) => Task.FromResult(new VSCodeRunResult(false, "")), _ => { });
        var refreshing = vscode.RefreshAsync();
        var vscodeStop = vscode.StopAsync();
        if (vscodeStop.IsCompleted) throw new InvalidOperationException("VS Code shutdown must drain discovery before its HTTP client closes.");
        discover.SetResult([]);
        await refreshing;
        await vscodeStop;
        await vscode.RefreshAsync();
        if (vscode.SelectedProject is not null) throw new InvalidOperationException("Stopped VS Code must not bind projects.");

        var reader = new ShutdownCodexService { DelayRead = true, DelayOpen = true };
        using var readingCodex = new CodexSection(() => reader, _ => { }, ToneBrush);
        readingCodex.SetActive(true);
        var reading = readingCodex.RefreshAsync();
        var openingCodex = readingCodex.OpenAsync();
        var readingStop = readingCodex.StopAsync();
        await reading;
        if (readingStop.IsCompleted) throw new InvalidOperationException("Codex shutdown must drain its underlying reader and navigation.");
        reader.ReadReply.SetResult(new(reader.Target, []));
        reader.OpenReply.SetResult(false);
        await openingCodex;
        await readingStop;
        if (readingCodex.Messages.Count != 0) throw new InvalidOperationException("Stopped Codex must ignore late history.");

        var sender = new ShutdownCodexService();
        using var sendingCodex = new CodexSection(() => sender, _ => { }, ToneBrush);
        sendingCodex.SetActive(true);
        await sendingCodex.RefreshAsync();
        var sending = sendingCodex.SendAsync("synthetic instruction", default);
        if (sender.Sends != 1) throw new InvalidOperationException("Synthetic Codex send did not start.");
        var sendingStop = sendingCodex.StopAsync();
        try { await sending; } catch (OperationCanceledException) { }
        if (sendingStop.IsCompleted) throw new InvalidOperationException("Codex shutdown must drain an in-flight delivery before releasing its service.");
        sender.SendReply.SetResult(new(CodexDeliveryResult.Sent, sender.Target));
        await sendingStop;
        if (sendingCodex.IsBusy) throw new InvalidOperationException("A late delivery must not start response polling in a stopped section.");
        File.AppendAllText(logPath, "PASS: section shutdown is idempotent, drains providers and navigation, rejects new work and ignores late results\n");
    }

    private sealed class ShutdownCodexService : ICodexChatService
    {
        internal readonly CodexChatTarget Target = new(Guid.NewGuid().ToString(), "Synthetic shutdown chat");
        internal readonly TaskCompletionSource<CodexChatHistory> ReadReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> OpenReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<CodexChatSubmission> SendReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool DelayRead, DelayOpen;
        internal int Sends;
        public Task<CodexChatSelection> GetVisibleAsync(CancellationToken token) => Task.FromResult(new CodexChatSelection(Target, false));
        public Task<CodexChatSelection> GetLinkedAsync(CancellationToken token) => GetVisibleAsync(token);
        public Task<CodexChatHistory> ReadAsync(CodexChatTarget target, CancellationToken token) =>
            DelayRead ? ReadReply.Task : Task.FromResult(new CodexChatHistory(target, []));
        public Task<CodexChatSubmission> SendAsync(CodexChatTarget? target, string text, CancellationToken token)
        { Sends++; return SendReply.Task; }
        public Task<bool> OpenChatAsync(CodexChatTarget? target, CancellationToken token) => DelayOpen ? OpenReply.Task : Task.FromResult(true);
        public void ReturnFocus(nint window) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private void CheckConversationRetentionUi(string logPath)
    {
        var session = new ConversationSession(maxMessages: 3, maxCharacters: 1000);
        var view = new ConversationView(session, ToneBrush, TextBrush);
        for (var index = 0; index < 10; index++) view.Add("YOU", "message " + index, MessageTone.User);
        if (view.Messages.Children.Count != 4 || view.Messages.Children[0] is not TextBlock
            { Text: "Earlier messages remain available in ChatGPT." } ||
            ((TextBlock)((StackPanel)view.Messages.Children[1]).Children[1]).Text != "message 7")
            throw new InvalidOperationException("Retired messages must release their controls and expose the English history notice.");
        foreach (var theme in new[] { Microsoft.UI.Xaml.ElementTheme.Light, Microsoft.UI.Xaml.ElementTheme.Dark })
        {
            view.Messages.RequestedTheme = theme;
            view.RefreshAppearance();
        }
        File.AppendAllText(logPath, "PASS: bounded conversation controls retain latest messages and an English recovery notice in both themes\n");
    }
}
#endif
