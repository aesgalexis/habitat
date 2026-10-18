using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Habitat.Services;

internal enum CodexDeliveryResult { Sent, NotSent, Unconfirmed, Cancelled }

internal sealed record CodexComposer(nint Window, string EditorId, string SendId, string Text,
    bool IsNewChat, bool CanSend);

internal interface ICodexDesktop
{
    Task<IReadOnlyList<CodexComposer>> ReadComposersAsync(CancellationToken cancellationToken);
    Task<bool> OpenNewChatAsync(Uri uri, CancellationToken cancellationToken);
    Task<bool> SetTextAsync(CodexComposer composer, string text, CancellationToken cancellationToken);
    Task<bool> PressEnterAsync(CodexComposer composer, CancellationToken cancellationToken);
    Task<bool> WasSubmittedAsync(CodexComposer composer, CancellationToken cancellationToken);
}

// Opening the app or filling its composer is not a successful send. Keep the
// draft until the desktop adapter observes the submitted instruction.
internal sealed class CodexDelivery(ICodexDesktop desktop, int attempts = 50, TimeSpan? interval = null,
    Action<string>? trace = null)
{
    // WinUI TextBox uses CR paragraph separators; web editors use LF.
    internal static Uri NewChatUri(string text) => new("codex://threads/new?prompt=" + Uri.EscapeDataString(Normalize(text)));

    public async Task<CodexDeliveryResult> SendAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('\0')) return CodexDeliveryResult.NotSent;
        var invoked = false;
        var marker = "HABITAT_TRANSFER_" + Guid.NewGuid().ToString("N");
        CodexComposer? target = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var token = timeout.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            // Codex parses deep-link prompts as Markdown. A unique plain marker
            // identifies our new editor before replacing it with literal text.
            if (!await desktop.OpenNewChatAsync(NewChatUri(marker), token).WaitAsync(token))
                return CodexDeliveryResult.NotSent;
            var deadline = Stopwatch.StartNew();
            for (var attempt = 0; attempt < attempts && deadline.Elapsed < TimeSpan.FromSeconds(8); attempt++)
            {
                // A new-chat home may reuse its editor, including a draft from
                // an earlier failed attempt. Its runtime ID is not a chat ID.
                IReadOnlyList<CodexComposer> editors;
                try { editors = await desktop.ReadComposersAsync(token).WaitAsync(token); }
                catch (COMException exception) when (exception.HResult == unchecked((int)0x80040201))
                {
                    // Chromium replaces accessibility elements while opening
                    // the new chat. UIA_E_ELEMENTNOTAVAILABLE means read again,
                    // not that navigation or prompt transfer failed.
                    trace?.Invoke("Codex accessibility changed during navigation; waiting for the new composer.");
                    await Task.Delay(interval ?? TimeSpan.FromMilliseconds(100), token);
                    continue;
                }
                var candidates = editors.Where(item => item.IsNewChat && item.CanSend &&
                    (target is null ? SameText(item.Text, marker) :
                    item.Window == target.Window && item.EditorId == target.EditorId && SameText(item.Text, text))).ToArray();
                trace?.Invoke($"Codex candidate check: stage={(target is null ? "identify" : "verify")}, ready={candidates.Length}.");
                if (candidates.Length > 1) return CodexDeliveryResult.NotSent;
                if (candidates.Length == 1)
                {
                    var composer = candidates[0];
                    if (target is null)
                    {
                        if (!await desktop.SetTextAsync(composer, text, token).WaitAsync(token))
                            return CodexDeliveryResult.NotSent;
                        target = composer;
                        continue;
                    }
                    // Never repeat Enter, even if the result or acknowledgement
                    // is lost: that could execute the same instruction twice.
                    token.ThrowIfCancellationRequested();
                    invoked = true;
                    if (!await desktop.PressEnterAsync(composer, token).WaitAsync(token))
                        return CodexDeliveryResult.NotSent;
                    deadline.Restart();
                    timeout.CancelAfter(TimeSpan.FromSeconds(8));
                    for (var confirmation = 0; confirmation < attempts && deadline.Elapsed < TimeSpan.FromSeconds(8); confirmation++)
                    {
                        try
                        {
                            if (await desktop.WasSubmittedAsync(composer, token).WaitAsync(token))
                                return CodexDeliveryResult.Sent;
                        }
                        catch (COMException exception) when (exception.HResult == unchecked((int)0x80040201))
                        {
                            trace?.Invoke("Codex accessibility changed after submission; waiting for acknowledgement without sending again.");
                        }
                        await Task.Delay(interval ?? TimeSpan.FromMilliseconds(100), token);
                    }
                    return CodexDeliveryResult.Unconfirmed;
                }
                await Task.Delay(interval ?? TimeSpan.FromMilliseconds(100), token);
            }
            return CodexDeliveryResult.NotSent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return invoked ? CodexDeliveryResult.Unconfirmed : CodexDeliveryResult.Cancelled;
        }
        catch (Exception exception)
        {
            trace?.Invoke($"Codex delivery failed: {exception.GetType().Name} {exception.HResult:X8}; invoked={invoked}.");
            return invoked ? CodexDeliveryResult.Unconfirmed : CodexDeliveryResult.NotSent;
        }
    }

    // UI Automation represents paragraph endings as CR or CRLF. Do not trim
    // spaces or otherwise change the user's instruction.
    internal static bool SameText(string actual, string expected) => Normalize(actual) == Normalize(expected);
    internal static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
}
