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
            if (windows.Length != 1) throw new InvalidDataException("Activate the Codex window you want to use, then return to HABITAT.");
            _lastChatWindow = windows[0];
        }
        using var automation = new Automation();
        var state = ReadWindow(automation, _lastChatWindow);
        // Codex home/Work views may have no chat editor. They are not an
        // existing conversation and do not prevent starting a separate chat.
        if (state.Composers.Count == 0) return null;
        if (state.Composers.Count != 1) throw new InvalidDataException("Open a chat in Codex, then reload.");
        var composer = state.Composers[0];
        var root = automation.Root(_lastChatWindow);
        var editor = automation.Elements(root, 30003, 50004).Single(element => Id(element, _lastChatWindow) == composer.EditorId);
        return new CodexVisibleChat(composer, SelectedTitles(automation, root), IsEmptyEditor(editor, composer.Text),
            !state.Blocked && editor.GetCurrentPropertyValue(30010) is true &&
            editor.GetCurrentPropertyValue(30043) is true && editor.GetCurrentPropertyValue(30046) is false);
    }, token);

    private static string[] SelectedTitles(Automation automation, IElement root) =>
        new[] { 50000, 50005 }.SelectMany(type => automation.Elements(root, 30003, type))
            .Where(element => element.GetCurrentPropertyValue(30022) is not true &&
                element.GetCurrentPropertyValue(30102) is string aria && aria.Split(';').Contains("current=page"))
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
            var current = await ReadVisibleAsync(cancellation).WaitAsync(cancellation);
            if (current is null || current.Composer.Window != expected.Composer.Window ||
                current.Composer.EditorId != expected.Composer.EditorId || !current.CanAccept ||
                !current.SelectedTitles.SequenceEqual(expected.SelectedTitles) ||
                !CodexDelivery.SameText(current.Composer.Text, expected.Composer.Text)) return CodexDeliveryResult.NotSent;
            if (!await SetTextCoreAsync(current.Composer, text, cancellation, allowEmpty: true, expected.SelectedTitles).WaitAsync(cancellation))
                return CodexDeliveryResult.NotSent;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                current = await ReadVisibleAsync(cancellation).WaitAsync(cancellation);
                if (current is null || current.Composer.Window != expected.Composer.Window ||
                    current.Composer.EditorId != expected.Composer.EditorId || !current.SelectedTitles.SequenceEqual(expected.SelectedTitles))
                    return CodexDeliveryResult.NotSent;
                if (current.Composer.CanSend && CodexDelivery.SameText(current.Composer.Text, text))
                {
                    invoked = true;
                    if (!await PressEnterCoreAsync(current.Composer, cancellation, expected.SelectedTitles).WaitAsync(cancellation))
                        return CodexDeliveryResult.NotSent;
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
            return CodexDeliveryResult.NotSent;
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
