#if HABITAT_UI_CHECKS
using Habitat.Services;
using Habitat.Features.Codex;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Habitat;

public sealed partial class MainPage
{
    // Explicit opt-in only: isolated HABITAT data, but the actual Windows
    // launcher and accessibility adapter, through the normal composer path.
    private async Task CheckCodexDesktopAsync(string logPath)
    {
        var marker = "HABITAT_DESKTOP_" + Guid.NewGuid().ToString("N")[..8];
        SelectDestination(ComposerDestination.Codex);
        Composer.Text = $"Integration check {marker}. Reply only {marker}_OK. Do not use tools, edit files, or change settings.";
        if (Environment.GetCommandLineArgs().Contains("--codex-multiline"))
            Composer.Text = $"  Integration check {marker}: café ☕ 雪 & #?\r\n# Literal **Markdown** and `code`\n\n  - Preserve indentation and [link](https://example.test).\nReply only {marker}_OK. Do not use tools, edit files, or change settings.  ";
        CodexDeliveryResult? result = null;
        var launcher = new CodexLauncher(message => File.AppendAllText(logPath, message + "\n"), _services.Paths);
        _launchCodex = async (text, token) =>
        {
            var delivery = await launcher.OpenAsync(text, token);
            result = delivery;
            return delivery;
        };
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await LaunchDestinationAsync(ComposerDestination.Codex, Composer.Text);
        File.AppendAllText(logPath, $"Codex desktop result: {result}; elapsed={stopwatch.ElapsedMilliseconds} ms; marker={marker}\n");
        if (result != CodexDeliveryResult.Sent || Composer.Text.Length != 0)
            throw new InvalidOperationException("The actual HABITAT composer must clear only after Codex confirms submission.");
        File.AppendAllText(logPath, "PASS: actual HABITAT composer, Windows launcher and Codex submission confirmed\n");
    }

    private async Task CheckCodexUiAsync(string logPath)
    {
        var launcher = _launchCodex;
        var saved = _destination;
        const string fullText = "  Note ☕\r\nwith line breaks\n  ";
        try
        {
            SelectDestination(ComposerDestination.Codex);
            Composer.Text = fullText;
            var editorText = Composer.Text;
            string? codexText = null;
            _launchCodex = (text, _) => { codexText = text; return Task.FromResult(CodexDeliveryResult.Sent); };
            SendCurrentMessage();
            if (codexText != editorText || Composer.Text != "" || _bridge!.HasMessageInFlight)
                throw new InvalidOperationException("Codex handoff must transfer the full instruction without sending through OpenAI.");
            Composer.Text = fullText;
            _launchCodex = (_, _) => Task.FromResult(CodexDeliveryResult.NotSent);
            SendCurrentMessage();
            if (Composer.Text != editorText) throw new InvalidOperationException("Failed Codex submission must preserve the instruction.");
            _launchCodex = (_, _) => Task.FromResult(CodexDeliveryResult.Unconfirmed);
            SendCurrentMessage();
            if (Composer.Text != editorText || (string)SendButton.Content != "Send to Codex" ||
                !AutomationProperties.GetHelpText(_destinationButtons[ComposerDestination.Codex]).Contains("started from HABITAT"))
                throw new InvalidOperationException("Unconfirmed Codex submission must preserve the instruction and use consistent send labels.");
            var codexPending = new TaskCompletionSource<CodexDeliveryResult>();
            var codexSubmissions = 0;
            CancellationToken oldCodexToken = default;
            _launchCodex = (_, token) => { oldCodexToken = token; codexSubmissions++; return codexPending.Task; };
            var codexLaunch = LaunchDestinationAsync(ComposerDestination.Codex, editorText);
            SendCurrentMessage();
            if (codexSubmissions != 1 || SendButton.IsEnabled || Composer.Text != editorText ||
                (string)SendButton.Content != "Sending to Codex…")
                throw new InvalidOperationException("Codex must wait for confirmation, retain the draft and reject duplicate sends.");
            Composer.Text = "New Codex instruction";
            if (!oldCodexToken.IsCancellationRequested || !SendButton.IsEnabled || (string)SendButton.Content != "Send to Codex")
                throw new InvalidOperationException("Editing must cancel the old Codex attempt and immediately enable a new send.");
            var latestCodex = new TaskCompletionSource<CodexDeliveryResult>();
            _launchCodex = (text, _) => { codexText = text; codexSubmissions++; return latestCodex.Task; };
            var latestCodexLaunch = LaunchDestinationAsync(ComposerDestination.Codex, Composer.Text);
            codexPending.SetResult(CodexDeliveryResult.Sent);
            await codexLaunch;
            if (Composer.Text != "New Codex instruction" || SendButton.IsEnabled || codexSubmissions != 2 || codexText != Composer.Text)
                throw new InvalidOperationException("A cancelled old Codex completion must neither clear nor unlock the newer send.");
            latestCodex.SetResult(CodexDeliveryResult.Sent);
            await latestCodexLaunch;
            if (Composer.Text != "" || !SendButton.IsEnabled)
                throw new InvalidOperationException("The revised Codex instruction must clear only after its own confirmed send.");
            File.AppendAllText(logPath, "PASS: Codex sends once, shows progress, cancels on edit, immediately accepts revised text and ignores old completions\n");
        }
        finally
        {
            CancelPendingCodexSend();
            _launchCodex = launcher;
            SelectDestination(saved);
            Composer.Text = "";
        }
        await CheckCodexConversationUiAsync(logPath);
        await CheckCodexSettingsUiAsync(logPath);
        var previousService = _services.Codex;
        var stoppingService = new CheckCodexService { WaitForOpenCancellation = true };
        try
        {
            _services.Codex = stoppingService;
            using var section = new Habitat.Features.Codex.CodexSection(() => _services.Codex, _ => { }, ToneBrush);
            var opening = section.OpenAsync();
            section.Dispose();
            await opening;
            await section.OpenAsync();
            if (stoppingService.Opens != 1)
                throw new InvalidOperationException("A disposed Codex section must not open a new operation.");
            File.AppendAllText(logPath, "PASS: closing during Codex navigation cancels safely and rejects new navigation\n");
        }
        finally { _services.Codex = previousService; }
    }

    private async Task CheckCodexConversationUiAsync(string logPath)
    {
        var previous = _services.Codex;
        var fake = new CheckCodexService();
        var openAiMessages = Conversation.Children.Count;
        try
        {
            _services.Codex = fake;
            SelectDestination(ComposerDestination.Codex);
            await CodexChat.RefreshAsync();
            if (CodexChat.Target != fake.A || CodexChat.Messages.Last().Text != "A previous reply")
                throw new InvalidOperationException("Codex must show a linked HABITAT chat without using OpenAI history.");
            Composer.Text = "  Follow up **literal** ☕\rnext line  ";
            var original = Composer.Text;
            await LaunchDestinationAsync(ComposerDestination.Codex, original);
            await CodexChat.RefreshAsync();
            if (Composer.Text.Length != 0 || fake.Sends != 1 || !CodexChat.IsBusy || SendButton.IsEnabled ||
                (string)SendButton.Content != "Waiting for Codex…")
                throw new InvalidOperationException("A confirmed Codex send must clear its draft and wait for its own reply.");
            Composer.Text = "Next instruction";
            SendCurrentMessage();
            if (fake.Sends != 1 || !CodexChat.IsBusy)
                throw new InvalidOperationException("Editing while Codex responds must preserve response tracking and prevent duplicate sends.");
            fake.Interrupted = true;
            await CodexChat.RefreshAsync();
            if (CodexChat.IsBusy || CodexChat.StatusText != UiText.Get("TheCodexTurnDidNotCompleteOpenThe"))
                throw new InvalidOperationException("An interrupted snapshot must end the local wait with a notice.");
            fake.Visible = fake.B;
            await CodexChat.RefreshAsync();
            if (CodexChat.Target != fake.B || CodexChat.Messages.Last().Text != "B reply")
                throw new InvalidOperationException("HABITAT must follow another linked HABITAT chat while the old chat responds.");
            SelectDestination(ComposerDestination.OpenAI);
            fake.Complete();
            await CodexChat.RefreshAsync();
            if (CodexChat.IsBusy || Conversation.Children.Count != openAiMessages)
                throw new InvalidOperationException("A background Codex completion must leave OpenAI history separate.");
            fake.Visible = fake.A;
            SelectDestination(ComposerDestination.Codex);
            await CodexChat.RefreshAsync();
            if (CodexChat.Messages[^2].Text != original || CodexChat.Messages[^1].Text != "A recovered reply" ||
                Composer.Text != "Next instruction" || !SendButton.IsEnabled ||
                CodexChat.StatusText != UiText.Get("FollowingACodexChatStartedFromHABITAT"))
                throw new InvalidOperationException("Returning to Codex must recover the final response and preserve the next draft.");
            CheckCodexMessageAppearance();
            fake.Result = CodexDeliveryResult.Unconfirmed;
            await LaunchDestinationAsync(ComposerDestination.Codex, Composer.Text);
            if (Composer.Text != "Next instruction" || CodexChat.IsBusy || !CodexChat.StatusText.Contains("could not be confirmed"))
                throw new InvalidOperationException("An uncertain Codex send must retain its instruction without waiting for an invented turn.");
            File.AppendAllText(logPath, "PASS: Codex shows linked HABITAT chats, sends a contextual follow-up, recovers its reply across chat and destination changes, preserves original text and keeps OpenAI history separate\n");
        }
        finally
        {
            SelectDestination(ComposerDestination.OpenAI);
            _services.Codex = previous;
        }
    }

    private void CheckCodexMessageAppearance()
    {
        var theme = RequestedTheme;
        var language = UiText.Language;
        try
        {
            foreach (var currentLanguage in new[] { "en", "es" })
            foreach (var currentTheme in new[] { ElementTheme.Light, ElementTheme.Dark })
            {
                UiText.SetLanguage(currentLanguage);
                CodexChat.RefreshLanguage();
                RequestedTheme = currentTheme;
                CodexChat.RefreshAppearance();
                var scroll = ((Grid)CodexChat.View).Children.OfType<ScrollViewer>().Single();
                foreach (var group in ((StackPanel)scroll.Content).Children.OfType<StackPanel>())
                {
                    var tone = (MessageTone)group.Tag;
                    var author = (TextBlock)group.Children[0];
                    if (((SolidColorBrush)author.Foreground).Color != ((SolidColorBrush)ToneBrush(tone)).Color ||
                        author.Opacity != 1 || author.Text != (tone == MessageTone.User ? UiText.Get("YOU") : "Codex"))
                        throw new InvalidOperationException("Codex authors must match OpenAI colors in both themes and interface languages.");
                }
            }
        }
        finally
        {
            RequestedTheme = theme;
            UiText.SetLanguage(language);
            CodexChat.RefreshLanguage();
            CodexChat.RefreshAppearance();
        }
    }

    private sealed class CheckCodexService : ICodexChatService
    {
        public readonly CodexChatTarget A = new("11111111-1111-1111-1111-111111111111", "Check A");
        public readonly CodexChatTarget B = new("22222222-2222-2222-2222-222222222222", "Check B");
        public CodexChatTarget Visible;
        public int Sends;
        public int Opens;
        public bool WaitForOpenCancellation;
        public bool Interrupted;
        public CodexDeliveryResult Result = CodexDeliveryResult.Sent;
        private bool _complete;
        public CheckCodexService() => Visible = A;
        public Task<CodexChatSelection> GetVisibleAsync(CancellationToken token) => Task.FromResult(new CodexChatSelection(Visible, false));
        public Task<CodexChatHistory> ReadAsync(CodexChatTarget target, CancellationToken token)
        {
            List<CodexChatTurn> turns = [new("before", "completed", [new("old", "Codex", target == A ? "A previous reply" : "B reply")])];
            if (target == A && Sends > 0) turns.Add(new("ours", _complete ? "completed" : Interrupted ? "interrupted" : "inProgress",
                [new("user", "YOU", "serialized input"), .. _complete ? new[] { new CodexChatMessage("reply", "Codex", "A recovered reply") } : []]));
            return Task.FromResult(new CodexChatHistory(target, turns));
        }
        public Task<CodexChatSubmission> SendAsync(CodexChatTarget? expected, string text, CancellationToken token)
        { Sends++; return Task.FromResult(new CodexChatSubmission(Result, expected, "before")); }
        public void Complete() => _complete = true;
        public async Task<bool> OpenChatAsync(CodexChatTarget? target, CancellationToken token)
        {
            Opens++;
            if (WaitForOpenCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return true;
        }
        public void ReturnFocus(nint ownerWindow) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private async Task CheckCodexConversationDesktopAsync(string logPath)
    {
        var marker = "HABITAT_CONTEXT_" + Guid.NewGuid().ToString("N")[..8];
        await using var launcher = new CodexLauncher(message => File.AppendAllText(logPath, message + "\n"), _services.Paths);
        _services.Codex = new CodexSmokeService(launcher);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(80));
        if (!await launcher.OpenChatAsync(null, timeout.Token)) throw new IOException("Could not open a new synthetic Codex chat.");
        CodexChatSelection selection;
        do { await Task.Delay(250, timeout.Token); selection = await launcher.GetVisibleAsync(timeout.Token); }
        while (!selection.CanStartNew || selection.Target is not null);
        SelectDestination(ComposerDestination.Codex);
        await CodexChat.RefreshAsync();
        Composer.Text = $"Integration check. Remember the token {marker} for this chat. Reply only {marker}_OK. Do not use tools, edit files, or change settings.";
        await LaunchDestinationAsync(ComposerDestination.Codex, Composer.Text);
        if (Composer.Text.Length > 0 || CodexChat.Target is not { } target)
            throw new InvalidOperationException("The actual Codex send was not confirmed or linked to its new chat.");
        File.AppendAllText(logPath, $"Synthetic Codex chat: {target.Id}; marker={marker}\n");
        while (CodexChat.IsBusy || !CodexChat.Messages.Any(message => message.Author == "Codex" && message.Text.Contains(marker + "_OK")))
        { await Task.Delay(500, timeout.Token); await CodexChat.RefreshAsync(); }
        File.AppendAllText(logPath, "PASS: HABITAT recovered the actual Codex response\n");
        if (!await launcher.OpenChatAsync(target, timeout.Token)) throw new IOException("Could not reopen the synthetic chat.");
        do { await Task.Delay(250, timeout.Token); selection = await launcher.GetVisibleAsync(timeout.Token); }
        while (selection.Target?.Id != target.Id);
        Composer.Text = "What token did I ask you to remember? Reply with only that token, followed by _AGAIN. Do not use tools, edit files, or change settings.";
        await LaunchDestinationAsync(ComposerDestination.Codex, Composer.Text);
        if (Composer.Text.Length > 0 || CodexChat.Target?.Id != target.Id)
            throw new InvalidOperationException("The follow-up must remain in the same Codex chat.");
        while (CodexChat.IsBusy || !CodexChat.Messages.Any(message => message.Author == "Codex" && message.Text.Contains(marker + "_AGAIN")))
        { await Task.Delay(500, timeout.Token); await CodexChat.RefreshAsync(); }
        File.AppendAllText(logPath, "PASS: actual Codex follow-up preserved context and returned its response to HABITAT\n");
    }

    // This opt-in check may only send into the chat it created, even if the
    // user changes the visible Codex chat while the integration test runs.
    private sealed class CodexSmokeService(ICodexChatService service) : ICodexChatService
    {
        private string? _created;
        public Task<CodexChatSelection> GetVisibleAsync(CancellationToken token) => service.GetVisibleAsync(token);
        public Task<CodexChatHistory> ReadAsync(CodexChatTarget target, CancellationToken token) => service.ReadAsync(target, token);
        public async Task<CodexChatSubmission> SendAsync(CodexChatTarget? expected, string text, CancellationToken token)
        {
            if (expected?.Id != _created) return new(CodexDeliveryResult.NotSent);
            var sent = await service.SendAsync(expected, text, token);
            if (sent.Result == CodexDeliveryResult.Sent && sent.Target is { } target) _created = target.Id;
            return sent;
        }
        public Task<bool> OpenChatAsync(CodexChatTarget? target, CancellationToken token) => service.OpenChatAsync(target, token);
        public void ReturnFocus(nint ownerWindow) => service.ReturnFocus(ownerWindow);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
#endif
