using Habitat.Services;
using System.Runtime.InteropServices;

internal static class CodexDeliveryChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        const string text = "  café ☕ 雪 & #? + %\r\nsecond line\n  ";
        var uri = CodexDelivery.NewChatUri(text);
        check(uri.Scheme == "codex" && uri.Host == "threads" && uri.AbsolutePath == "/new" && uri.Fragment == "" &&
            Uri.UnescapeDataString(uri.Query["?prompt=".Length..]) == text.Replace("\r\n", "\n"),
            "Codex targets a new chat and transfers Unicode, spaces and complete logical lines using LF separators");
        var winUiText = "  pasted first line\rsecond line\r\rfinal line  ";
        check(Uri.UnescapeDataString(CodexDelivery.NewChatUri(winUiText).Query["?prompt=".Length..]) ==
            "  pasted first line\nsecond line\n\nfinal line  ",
            "WinUI CR paragraph separators become LF while preserving indentation and blank lines");

        var ready = new CodexComposer(123, "new editor", "send", text, true, true);
        var success = new Desktop { Next = [ready], ConfirmAfter = 2 };
        check(await Send(success, text) == CodexDeliveryResult.Sent && success.Invocations == 1 && success.Confirmations == 2 &&
            success.Events.SequenceEqual(new[] { "open", "read", "set", "read", "enter", "confirm", "confirm" }) &&
            success.WrittenText == text && success.Uri is not null && success.Uri != uri &&
            Uri.UnescapeDataString(success.Uri.Query).StartsWith("?prompt=HABITAT_TRANSFER_"),
            "Codex identifies a unique new editor, inserts literal text, verifies it and confirms a single submission");

        var openingFailed = new Desktop { Opened = false, Next = [ready] };
        check(await Send(openingFailed, text) == CodexDeliveryResult.NotSent && openingFailed.Invocations == 0,
            "Failure to open a new Codex chat never invokes Send");

        foreach (var candidate in new[]
        {
            ready with { IsNewChat = false },
            ready with { CanSend = false },
            ready with { Text = "different instruction" },
            ready with { Text = text.Trim() }
        })
        {
            var blocked = new Desktop { Next = [candidate] };
            check(await Send(blocked, text) == CodexDeliveryResult.NotSent && blocked.Invocations == 0,
                "Codex rejects an existing chat, a disabled Send button, changed text or lost whitespace");
        }
        var reusedHome = new Desktop { Next = [ready] };
        check(await Send(reusedHome, text) == CodexDeliveryResult.Sent &&
            await Send(reusedHome, text + " revised") == CodexDeliveryResult.NotSent && reusedHome.Invocations == 1,
            "A reused new-chat editor can send, while an outdated instruction never receives Enter");
        var revisedHome = new Desktop { Next = [ready with { Text = text + " revised" }] };
        check(await Send(revisedHome, text + " revised") == CodexDeliveryResult.Sent,
            "An edited draft can be sent immediately even when Codex reuses the same new-chat editor ID");

        var ambiguous = new Desktop { Next = [ready, ready with { Window = 456, EditorId = "other editor" }] };
        check(await Send(ambiguous, text) == CodexDeliveryResult.NotSent && ambiguous.Invocations == 0,
            "Two matching Codex windows never trigger an ambiguous send");

        var changedBeforeSend = new Desktop { Next = [ready], Invoked = false };
        check(await Send(changedBeforeSend, text) == CodexDeliveryResult.NotSent && changedBeforeSend.Invocations == 1 &&
            changedBeforeSend.Confirmations == 0,
            "Codex retains the draft when the native adapter rejects a changed composer before Send");

        var failedTransfer = new Desktop { Next = [ready], Prepared = false };
        check(await Send(failedTransfer, text) == CodexDeliveryResult.NotSent && failedTransfer.Invocations == 0,
            "Failed literal Codex transfer retains the draft and never submits the temporary marker");
        var changedTransfer = new Desktop { Next = [ready], ChangedText = "modified instruction" };
        check(await Send(changedTransfer, text) == CodexDeliveryResult.NotSent && changedTransfer.Invocations == 0,
            "A modified literal instruction never receives Enter even after the new editor was identified");

        var lostConfirmation = new Desktop { Next = [ready], ConfirmAfter = int.MaxValue };
        check(await Send(lostConfirmation, text) == CodexDeliveryResult.Unconfirmed && lostConfirmation.Invocations == 1,
            "A missing Codex acknowledgement retains the draft and never repeats submission");
        var lostInvocation = new Desktop { Next = [ready], ThrowOnInvoke = true };
        check(await Send(lostInvocation, text) == CodexDeliveryResult.Unconfirmed && lostInvocation.Invocations == 1,
            "An exception during Codex submission is uncertain and never triggers a duplicate execution");
        var unavailable = new Desktop { ThrowOnRead = true };
        check(await Send(unavailable, text) == CodexDeliveryResult.NotSent && unavailable.Invocations == 0,
            "Unavailable Codex accessibility never presses Enter");

        var navigating = new Desktop { Next = [ready], StaleReads = 2 };
        check(await Send(navigating, text) == CodexDeliveryResult.Sent && navigating.Reads == 4 && navigating.Invocations == 1,
            "Replaced Codex accessibility elements during navigation are retried before a single submission");
        var stillNavigating = new Desktop { Next = [ready], StaleReads = int.MaxValue };
        check(await Send(stillNavigating, text) == CodexDeliveryResult.NotSent && stillNavigating.Reads == 5 && stillNavigating.Invocations == 0,
            "A Codex window that keeps replacing its controls times out without submitting");
        var transitioning = new Desktop { Next = [ready], StaleConfirmations = 2 };
        check(await Send(transitioning, text) == CodexDeliveryResult.Sent && transitioning.Confirmations == 3 && transitioning.Invocations == 1,
            "Replaced Codex controls after submission delay acknowledgement without repeating Enter");

        using var cancel = new CancellationTokenSource();
        var readGate = new TaskCompletionSource<IReadOnlyList<CodexComposer>>();
        var slowRead = new Desktop { ReadGate = readGate.Task, Instruction = text };
        var cancelledSend = new CodexDelivery(slowRead).SendAsync(text, cancel.Token);
        cancel.Cancel();
        check(await cancelledSend.WaitAsync(TimeSpan.FromSeconds(1)) == CodexDeliveryResult.Cancelled && slowRead.Invocations == 0,
            "Editing cancels an unresponsive old Codex attempt immediately without pressing Enter");
        readGate.SetResult([ready]);
        check(slowRead.Invocations == 0, "A late old editor response cannot start the cancelled instruction");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var notOpened = new Desktop();
        check(await new CodexDelivery(notOpened).SendAsync(text, cancelled.Token) == CodexDeliveryResult.Cancelled &&
            notOpened.Events.Count == 0, "An already cancelled Codex send never opens a chat");

        using var cancelAfterEnter = new CancellationTokenSource();
        var confirmationGate = new TaskCompletionSource<bool>();
        var awaitingConfirmation = new Desktop { Next = [ready], ConfirmationGate = confirmationGate.Task, Instruction = text };
        var sentBeforeCancel = new CodexDelivery(awaitingConfirmation).SendAsync(text, cancelAfterEnter.Token);
        cancelAfterEnter.Cancel();
        check(await sentBeforeCancel.WaitAsync(TimeSpan.FromSeconds(1)) == CodexDeliveryResult.Unconfirmed &&
            awaitingConfirmation.Invocations == 1,
            "Cancellation after Enter reports uncertainty immediately and never repeats Enter");
        confirmationGate.SetResult(true);

        foreach (var invalid in new[] { "", " \r\n ", "one\0two" })
        {
            var desktop = new Desktop();
            check(await Send(desktop, invalid) == CodexDeliveryResult.NotSent && desktop.Events.Count == 0,
                "Empty or unrepresentable Codex input never opens the app or sends partial text");
        }
        var lineEndings = new Desktop { Next = [ready with { Text = text.Replace("\r\n", "\n") }] };
        check(await Send(lineEndings, text) == CodexDeliveryResult.Sent,
            "Codex verification accepts native paragraph line endings while preserving all other content");
    }

    private static Task<CodexDeliveryResult> Send(Desktop desktop, string text)
    {
        desktop.Instruction = text;
        return new CodexDelivery(desktop, attempts: 5, interval: TimeSpan.Zero).SendAsync(text);
    }

    private sealed class Desktop : ICodexDesktop
    {
        public IReadOnlyList<CodexComposer> Next { get; init; } = [];
        public Task<IReadOnlyList<CodexComposer>>? ReadGate { get; init; }
        public Task<bool>? ConfirmationGate { get; init; }
        public bool Opened { get; init; } = true;
        public bool Prepared { get; init; } = true;
        public string? ChangedText { get; init; }
        public string Instruction { get; set; } = "";
        public string? WrittenText { get; private set; }
        private string _marker = "";
        private bool _filled;
        public bool Invoked { get; init; } = true;
        public bool ThrowOnInvoke { get; init; }
        public bool ThrowOnRead { get; init; }
        public int StaleReads { get; init; }
        public int StaleConfirmations { get; init; }
        public int Reads { get; private set; }
        public int ConfirmAfter { get; init; } = 1;
        public int Invocations { get; private set; }
        public int Confirmations { get; private set; }
        public Uri? Uri { get; private set; }
        public List<string> Events { get; } = [];
        public Task<IReadOnlyList<CodexComposer>> ReadComposersAsync(CancellationToken cancellationToken)
        {
            Events.Add("read");
            Reads++;
            if (Reads <= StaleReads) throw new COMException("Synthetic replaced accessibility element.", unchecked((int)0x80040201));
            if (ThrowOnRead) throw new InvalidOperationException("Synthetic accessibility failure.");
            return ReadGate ?? Task.FromResult<IReadOnlyList<CodexComposer>>(Next.Select(item =>
                CodexDelivery.SameText(item.Text, Instruction) ? item with { Text = _filled ? ChangedText ?? item.Text : _marker } : item).ToArray());
        }
        public Task<bool> OpenNewChatAsync(Uri uri, CancellationToken cancellationToken)
        {
            Events.Add("open"); Uri = uri; _filled = false;
            _marker = System.Uri.UnescapeDataString(uri.Query["?prompt=".Length..]);
            return Task.FromResult(Opened);
        }
        public Task<bool> SetTextAsync(CodexComposer composer, string text, CancellationToken cancellationToken)
        {
            Events.Add("set"); WrittenText = text; _filled = true;
            return Task.FromResult(Prepared);
        }
        public Task<bool> PressEnterAsync(CodexComposer composer, CancellationToken cancellationToken)
        {
            Events.Add("enter"); Invocations++;
            if (ThrowOnInvoke) throw new InvalidOperationException("Synthetic lost invocation response.");
            return Task.FromResult(Invoked);
        }
        public Task<bool> WasSubmittedAsync(CodexComposer composer, CancellationToken cancellationToken)
        {
            Events.Add("confirm");
            Confirmations++;
            if (Confirmations <= StaleConfirmations) throw new COMException("Synthetic replaced acknowledgement element.", unchecked((int)0x80040201));
            return ConfirmationGate ?? Task.FromResult(Confirmations >= ConfirmAfter);
        }
    }
}
