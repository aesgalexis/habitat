using System.Runtime.InteropServices;
using Habitat.Features.Codex;

namespace Habitat.Services;

// Keep the delivery protocol independent of COM so replaced desktop controls
// can be exercised without opening Codex or sending a real instruction.
internal sealed class CodexVisibleChatDelivery(
    Func<nint, CancellationToken, Task<CodexVisibleChat?>> read,
    Func<CodexVisibleChat, string, CancellationToken, Task<bool>> setText,
    Func<CodexVisibleChat, CancellationToken, Task<bool>> submit,
    Func<CodexComposer, CancellationToken, Task<bool>> confirm,
    Action<string>? trace = null, int attempts = 40, int readAttempts = 21, TimeSpan? interval = null)
{
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromMilliseconds(100);
    private readonly TimeSpan _confirmationInterval = interval ?? TimeSpan.FromMilliseconds(150);

    private async Task<CodexVisibleChat?> ReadAsync(nint window, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var current = await read(window, token).WaitAsync(token);
                if (current is not null || attempt + 1 >= readAttempts) return current;
            }
            catch (COMException exception) when (exception.HResult == unchecked((int)0x80040201) && attempt + 1 < readAttempts)
            {
                // Reacquire the entire pinned window, never reuse a stale COM
                // element and never repeat a write or submission here.
                trace?.Invoke("Codex editor controls changed; reading the same window again before submission.");
            }
            await Task.Delay(_interval, token);
        }
    }

    private static bool SameChat(CodexVisibleChat current, CodexVisibleChat expected) =>
        current.Composer.Window == expected.Composer.Window &&
        current.Composer.IsNewChat == expected.Composer.IsNewChat &&
        current.SelectedTitles.SequenceEqual(expected.SelectedTitles);

    public async Task<CodexDeliveryResult> SendAsync(CodexVisibleChat expected, string text, CancellationToken token)
    {
        var invoked = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(16));
        var cancellation = timeout.Token;
        try
        {
            CodexDeliveryResult Reject(string stage)
            {
                trace?.Invoke($"Codex visible-chat delivery blocked: {stage}; no submission invoked.");
                return CodexDeliveryResult.NotSent;
            }
            var current = await ReadAsync(expected.Composer.Window, cancellation);
            if (current is null || !SameChat(current, expected) || !current.CanAccept ||
                current.Composer.EditorId != expected.Composer.EditorId ||
                !CodexDelivery.SameText(current.Composer.Text, expected.Composer.Text)) return Reject("editor-changed");
            if (!await setText(current, text, cancellation).WaitAsync(cancellation)) return Reject("literal-transfer");

            for (var attempt = 0; attempt < attempts; attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                current = await ReadAsync(expected.Composer.Window, cancellation);
                if (current is null || !SameChat(current, expected)) return Reject("selection-changed");
                if (!current.CanAccept) return Reject("editor-unavailable");
                var instructionReady = CodexDelivery.SameText(current.Composer.Text, text);
                if (!instructionReady && !CodexDelivery.SameText(current.Composer.Text, expected.Composer.Text))
                    return Reject("instruction-changed");
                // Electron may replace the editor and Send button after the
                // write. The pinned chat and exact instruction authorize the
                // freshly read controls; the native adapter verifies them again.
                if (instructionReady && current.Composer.CanSend)
                {
                    // From this point an exception is uncertain. Never retry
                    // submit, even if it reports the same transient COM error.
                    invoked = true;
                    if (!await submit(current, cancellation).WaitAsync(cancellation))
                        return Reject("send-control-or-focus-unavailable");
                    timeout.CancelAfter(TimeSpan.FromSeconds(8));
                    for (var confirmation = 0; confirmation < attempts; confirmation++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        try
                        {
                            if (await confirm(current.Composer, cancellation).WaitAsync(cancellation)) return CodexDeliveryResult.Sent;
                        }
                        catch (COMException exception) when (exception.HResult == unchecked((int)0x80040201)) { }
                        await Task.Delay(_confirmationInterval, cancellation);
                    }
                    return CodexDeliveryResult.Unconfirmed;
                }
                await Task.Delay(_interval, cancellation);
            }
            return Reject("send-control-not-ready");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return invoked ? CodexDeliveryResult.Unconfirmed : CodexDeliveryResult.Cancelled; }
        catch (Exception exception)
        {
            trace?.Invoke($"Codex visible-chat delivery failed: {exception.GetType().Name} {exception.HResult:X8}; invoked={invoked}.");
            return invoked ? CodexDeliveryResult.Unconfirmed : CodexDeliveryResult.NotSent;
        }
    }
}
