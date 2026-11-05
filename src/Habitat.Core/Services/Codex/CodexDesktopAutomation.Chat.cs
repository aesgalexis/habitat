using System.Runtime.InteropServices;
using Habitat.Features.Codex;

namespace Habitat.Services;

internal sealed partial class CodexDesktopAutomation
{
    private nint _lastChatWindow, _submittedWindow;

    public async Task<CodexVisibleChat?> ReadVisibleAsync(CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await ReadVisibleCoreAsync(token).WaitAsync(token); }
            catch (COMException exception) when (exception.HResult == unchecked((int)0x80040201) && attempt < 20)
            { await Task.Delay(100, token); }
        }
    }

    private Task<CodexVisibleChat?> ReadVisibleCoreAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var foreground = GetForegroundWindow();
        if (IsCodexWindow(foreground)) _lastChatWindow = foreground;
        if (!IsCodexWindow(_lastChatWindow))
        {
            var windows = FindWindows();
            if (windows.Length == 0) return null;
            if (windows.Length != 1) return null; // Existing-chat sends resolve all windows by their chat identity.
            _lastChatWindow = windows[0];
        }
        using var automation = new Automation();
        return ReadChatWindow(automation, _lastChatWindow, foreground);
    }, token);

    public async Task<IReadOnlyList<CodexVisibleChat>> ReadChatsAsync(CancellationToken token) => await Task.Run<IReadOnlyList<CodexVisibleChat>>(() =>
    {
        token.ThrowIfCancellationRequested();
        var foreground = GetForegroundWindow();
        using var automation = new Automation();
        var chats = new List<CodexVisibleChat>();
        foreach (var window in FindWindows())
        {
            token.ThrowIfCancellationRequested();
            try { if (ReadChatWindow(automation, window, foreground) is { } chat) chats.Add(chat); }
            catch (COMException exception) when (exception.HResult == unchecked((int)0x80040201))
            { trace?.Invoke("Codex window controls are changing; waiting before selecting its editor."); }
        }
        return chats;
    }, token).WaitAsync(token);

    private CodexVisibleChat? ReadChatWindow(Automation automation, nint window, nint foreground)
    {
        var state = ReadWindow(automation, window, trace);
        // Codex home/Work views may have no chat editor. They are not an
        // existing conversation and do not prevent starting a separate chat.
        if (state.Composers.Count == 0) return null;
        if (state.Composers.Count != 1) throw new InvalidDataException("Open one unambiguous chat editor in the Codex window, then reload.");
        var composer = state.Composers[0];
        var root = automation.Root(window);
        var editor = automation.Elements(root, 30003, 50004).Single(element => Id(element, window) == composer.EditorId);
        return new CodexVisibleChat(composer, SelectedTitles(automation, root), IsEmptyEditor(editor, composer.Text),
            !state.Blocked && editor.GetCurrentPropertyValue(30010) is true &&
            editor.GetCurrentPropertyValue(30043) is true && editor.GetCurrentPropertyValue(30046) is false, window == foreground);
    }

    private Task<CodexVisibleChat?> ReadPinnedAsync(nint window, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!IsCodexWindow(window)) return null;
        using var automation = new Automation();
        return ReadChatWindow(automation, window, GetForegroundWindow());
    }, token);

    private static string[] SelectedTitles(Automation automation, IElement root) =>
        new[] { 50000, 50005, 50007 }.SelectMany(type => automation.Elements(root, 30003, type))
            .Where(element => element.GetCurrentPropertyValue(30022) is not true &&
                (element.GetCurrentPropertyValue(30102) is string aria && aria.Split(';').Any(value => value.Trim() == "current=page") ||
                 element.GetCurrentPropertyValue(30003) is 50007 && element.GetCurrentPropertyValue(30079) is true))
            .Select(element => element.GetCurrentPropertyValue(30005) as string ?? "")
            .Where(title => title.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    public async Task<CodexDeliveryResult> SubmitVisibleAsync(CodexVisibleChat expected, string text, CancellationToken token)
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
            var current = await ReadPinnedAsync(expected.Composer.Window, cancellation).WaitAsync(cancellation);
            if (current is null || current.Composer.Window != expected.Composer.Window ||
                current.Composer.EditorId != expected.Composer.EditorId || !current.CanAccept ||
                !current.SelectedTitles.SequenceEqual(expected.SelectedTitles) ||
                !CodexDelivery.SameText(current.Composer.Text, expected.Composer.Text)) return Reject("editor-changed");
            if (!await SetTextCoreAsync(current.Composer, text, cancellation, allowEmpty: true, expected.SelectedTitles).WaitAsync(cancellation))
                return Reject("literal-transfer");
            for (var attempt = 0; attempt < 40; attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                current = await ReadPinnedAsync(expected.Composer.Window, cancellation).WaitAsync(cancellation);
                if (current is null || current.Composer.Window != expected.Composer.Window ||
                    current.Composer.EditorId != expected.Composer.EditorId || !current.SelectedTitles.SequenceEqual(expected.SelectedTitles))
                    return Reject("selection-changed");
                if (current.Composer.CanSend && CodexDelivery.SameText(current.Composer.Text, text))
                {
                    invoked = true;
                    if (!await PressEnterCoreAsync(current.Composer, cancellation, expected.SelectedTitles).WaitAsync(cancellation))
                        return Reject("send-control-or-focus-unavailable");
                    _submittedWindow = current.Composer.Window;
                    timeout.CancelAfter(TimeSpan.FromSeconds(8));
                    for (var confirmation = 0; confirmation < 40; confirmation++)
                    {
                        try
                        {
                            if (await WasSubmittedAsync(current.Composer, cancellation).WaitAsync(cancellation)) return CodexDeliveryResult.Sent;
                        }
                        catch (COMException exception) when (exception.HResult == unchecked((int)0x80040201)) { }
                        await Task.Delay(150, cancellation);
                    }
                    return CodexDeliveryResult.Unconfirmed;
                }
                await Task.Delay(100, cancellation);
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

    public Task<bool> OpenChatAsync(CodexChatTarget? target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (target is not null && !CodexReadClient.ValidId(target.Id)) return Task.FromResult(false);
        return openNewChat(new Uri(target is null ? "codex://threads/new" : "codex://threads/" + target.Id));
    }

    public void ReturnFocus(nint ownerWindow)
    {
        if (_submittedWindow != 0 && ownerWindow != 0 && GetForegroundWindow() == _submittedWindow)
            SetForegroundWindow(ownerWindow);
    }
}
