using System.Runtime.InteropServices;
using Habitat.Features.Codex;
using Habitat.Services;

internal static class CodexVisibleChatDeliveryChecks
{
    private const string Instruction = "  Continue café ☕ 雪\r\nsecond line  ";
    private static readonly CodexVisibleChat Expected = new(
        new CodexComposer(123, "editor", "send", "", false, false), ["Scheduled chat"], true, true);

    internal static async Task RunAsync(Action<bool, string> check)
    {
        var replacedAfterWrite = new Desktop { StaleAfterWrite = 2 };
        check(await replacedAfterWrite.SendAsync() == CodexDeliveryResult.Sent &&
            replacedAfterWrite.Writes == 1 && replacedAfterWrite.Submissions == 1 && replacedAfterWrite.PostWriteReads == 3 &&
            replacedAfterWrite.Windows.All(window => window == Expected.Composer.Window),
            "The scheduled-delivery failure after writing retries stale controls in the pinned window and submits exactly once");
        check(replacedAfterWrite.Trace.Any(line => line.Contains("controls changed")) &&
            replacedAfterWrite.Trace.All(line => !line.Contains("Scheduled chat") && !line.Contains("café")),
            "Transient delivery diagnostics identify the recovery without recording chat titles or instructions");

        var replacedBeforeWrite = new Desktop { StaleBeforeWrite = 2 };
        check(await replacedBeforeWrite.SendAsync() == CodexDeliveryResult.Sent && replacedBeforeWrite.Writes == 1 &&
            replacedBeforeWrite.Submissions == 1,
            "Stale accessibility before the transfer is read again without repeating the write");

        var recreatedEditor = new Desktop
        {
            AfterWrite = chat => chat with { Composer = chat.Composer with { EditorId = "replacement editor", SendId = "replacement send" } }
        };
        check(await recreatedEditor.SendAsync() == CodexDeliveryResult.Sent &&
            recreatedEditor.Submitted?.Composer is { EditorId: "replacement editor", SendId: "replacement send" } &&
            recreatedEditor.Submissions == 1,
            "Recreated editor and Send controls are accepted only after rereading the same chat and exact instruction");

        var settling = new Desktop { TextReadyAfter = 2, SendReadyAfter = 3 };
        check(await settling.SendAsync() == CodexDeliveryResult.Sent && settling.PostWriteReads == 4 && settling.Writes == 1 &&
            settling.Submissions == 1,
            "Delayed editor text and Send readiness settle before the single submission without rewriting the draft");
        var missingSnapshot = new Desktop { MissingAfterWrite = 2 };
        check(await missingSnapshot.SendAsync() == CodexDeliveryResult.Sent && missingSnapshot.Writes == 1 && missingSnapshot.Submissions == 1,
            "A temporarily missing editor snapshot is reacquired after the transfer");

        foreach (var changed in new Func<CodexVisibleChat, CodexVisibleChat>[]
        {
            chat => chat with { Composer = chat.Composer with { Window = 456 } },
            chat => chat with { SelectedTitles = ["Another chat"] },
            chat => chat with { Composer = chat.Composer with { IsNewChat = true } },
            chat => chat with { Composer = chat.Composer with { Text = "A user draft" } },
            chat => chat with { Composer = chat.Composer with { Text = Instruction.Trim() } },
            chat => chat with { CanAccept = false }
        })
        {
            var changedAfterWrite = new Desktop { StaleAfterWrite = 1, AfterWrite = changed };
            check(await changedAfterWrite.SendAsync() == CodexDeliveryResult.NotSent && changedAfterWrite.Writes == 1 &&
                changedAfterWrite.Submissions == 0,
                "Recovering stale controls still rejects a changed window, chat, editor state or exact instruction before submission");
        }
        var changedBeforeWrite = new Desktop { BeforeWrite = Expected with { Composer = Expected.Composer with { Text = "Existing draft" } } };
        check(await changedBeforeWrite.SendAsync() == CodexDeliveryResult.NotSent && changedBeforeWrite.Writes == 0 &&
            changedBeforeWrite.Submissions == 0, "A draft changed before the transfer is preserved");

        var permanentlyStale = new Desktop { StaleAfterWrite = int.MaxValue };
        check(await permanentlyStale.SendAsync() == CodexDeliveryResult.NotSent && permanentlyStale.PostWriteReads == 5 &&
            permanentlyStale.Submissions == 0, "Persistent stale controls stop after bounded reads without sending");
        var unrelatedError = new Desktop { ReadError = new COMException("Synthetic unrelated COM failure.", unchecked((int)0x80004005)) };
        check(await unrelatedError.SendAsync() == CodexDeliveryResult.NotSent && unrelatedError.Windows.Count == 1 &&
            unrelatedError.Writes == 0, "Only the unavailable-element COM error receives automatic read retries");

        var lostSubmission = new Desktop { ThrowOnSubmit = true };
        check(await lostSubmission.SendAsync() == CodexDeliveryResult.Unconfirmed && lostSubmission.Submissions == 1 &&
            lostSubmission.Confirmations == 0, "An unavailable element during submission is uncertain and never retries Send");
        var staleAcknowledgement = new Desktop { StaleConfirmations = 2 };
        check(await staleAcknowledgement.SendAsync() == CodexDeliveryResult.Sent && staleAcknowledgement.Submissions == 1 &&
            staleAcknowledgement.Confirmations == 3, "Stale acknowledgement controls are retried without resubmitting");
        var missingAcknowledgement = new Desktop { Confirmed = false };
        check(await missingAcknowledgement.SendAsync() == CodexDeliveryResult.Unconfirmed && missingAcknowledgement.Submissions == 1,
            "A missing acknowledgement preserves uncertainty after one submission");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var notStarted = new Desktop();
        check(await notStarted.SendAsync(cancelled.Token) == CodexDeliveryResult.Cancelled && notStarted.Windows.Count == 0 &&
            notStarted.Writes == 0, "An already cancelled visible-chat delivery never reads or writes the editor");
        using var cancelAfterWrite = new CancellationTokenSource();
        var interrupted = new Desktop { OnWrite = cancelAfterWrite.Cancel };
        check(await interrupted.SendAsync(cancelAfterWrite.Token) == CodexDeliveryResult.Cancelled && interrupted.Submissions == 0,
            "Cancellation while reacquiring a written editor never starts submission");
        using var cancelAfterSubmit = new CancellationTokenSource();
        var submitted = new Desktop { OnSubmit = cancelAfterSubmit.Cancel };
        check(await submitted.SendAsync(cancelAfterSubmit.Token) == CodexDeliveryResult.Unconfirmed && submitted.Submissions == 1,
            "Cancellation after Send remains uncertain and never repeats the submission");

        var gate = new TaskCompletionSource<CodexVisibleChat?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancelRead = new CancellationTokenSource();
        var pendingRead = new Desktop { ReadGate = gate.Task };
        var pending = pendingRead.SendAsync(cancelRead.Token);
        cancelRead.Cancel();
        check(await pending.WaitAsync(TimeSpan.FromSeconds(1)) == CodexDeliveryResult.Cancelled && pendingRead.Writes == 0,
            "Cancellation releases an unresponsive pinned-window read promptly");
        gate.SetResult(Expected);
        check(pendingRead.Submissions == 0, "A late pinned-window response cannot send a cancelled instruction");
    }

    private sealed class Desktop
    {
        internal int StaleBeforeWrite { get; init; }
        internal int StaleAfterWrite { get; init; }
        internal int MissingAfterWrite { get; init; }
        internal int TextReadyAfter { get; init; }
        internal int SendReadyAfter { get; init; }
        internal int StaleConfirmations { get; init; }
        internal bool ThrowOnSubmit { get; init; }
        internal bool Confirmed { get; init; } = true;
        internal Exception? ReadError { get; init; }
        internal CodexVisibleChat BeforeWrite { get; init; } = Expected;
        internal Func<CodexVisibleChat, CodexVisibleChat>? AfterWrite { get; init; }
        internal Task<CodexVisibleChat?>? ReadGate { get; init; }
        internal Action? OnWrite { get; init; }
        internal Action? OnSubmit { get; init; }
        internal int Writes { get; private set; }
        internal int Submissions { get; private set; }
        internal int Confirmations { get; private set; }
        internal int PostWriteReads { get; private set; }
        internal CodexVisibleChat? Submitted { get; private set; }
        internal List<nint> Windows { get; } = [];
        internal List<string> Trace { get; } = [];

        internal Task<CodexDeliveryResult> SendAsync(CancellationToken token = default) =>
            new CodexVisibleChatDelivery(ReadAsync, SetTextAsync, SubmitAsync, ConfirmAsync, Trace.Add,
                attempts: 5, readAttempts: 5, interval: TimeSpan.Zero).SendAsync(Expected, Instruction, token);

        private Task<CodexVisibleChat?> ReadAsync(nint window, CancellationToken token)
        {
            Windows.Add(window);
            if (ReadError is not null) throw ReadError;
            if (ReadGate is not null) return ReadGate;
            if (Writes == 0)
            {
                if (Windows.Count <= StaleBeforeWrite) throw Stale();
                return Task.FromResult<CodexVisibleChat?>(BeforeWrite);
            }
            PostWriteReads++;
            if (PostWriteReads <= StaleAfterWrite) throw Stale();
            if (PostWriteReads <= MissingAfterWrite) return Task.FromResult<CodexVisibleChat?>(null);
            var chat = Expected with
            {
                Composer = Expected.Composer with
                {
                    Text = PostWriteReads > TextReadyAfter ? Instruction : Expected.Composer.Text,
                    CanSend = PostWriteReads > SendReadyAfter
                },
                IsEmpty = PostWriteReads <= TextReadyAfter
            };
            return Task.FromResult<CodexVisibleChat?>(AfterWrite?.Invoke(chat) ?? chat);
        }
        private Task<bool> SetTextAsync(CodexVisibleChat chat, string text, CancellationToken token)
        { Writes++; OnWrite?.Invoke(); return Task.FromResult(true); }
        private Task<bool> SubmitAsync(CodexVisibleChat chat, CancellationToken token)
        {
            Submissions++; Submitted = chat;
            if (ThrowOnSubmit) throw Stale();
            OnSubmit?.Invoke();
            return Task.FromResult(true);
        }
        private Task<bool> ConfirmAsync(CodexComposer composer, CancellationToken token)
        {
            Confirmations++;
            if (Confirmations <= StaleConfirmations) throw Stale();
            return Task.FromResult(Confirmed);
        }
        private static COMException Stale() => new("Synthetic replaced accessibility element.", unchecked((int)0x80040201));
    }
}
