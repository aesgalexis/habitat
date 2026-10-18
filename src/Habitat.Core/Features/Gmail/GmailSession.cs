using Habitat.Models;
using Habitat.Services;

namespace Habitat;

// Mailbox state and operations are independent of WinUI. Notifications stay on
// the caller's context so the view can keep its existing rendering and focus.
internal sealed class GmailSession(Func<GmailAuthService> createAuth, Func<GmailAuthService, GmailService> createGmail,
    Func<Exception, string> describeError, TimeProvider? clock = null) : IDisposable
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private GmailService? _gmail;
    public GmailAuthService? Auth { get; private set; }
    public List<GmailMessage> Messages { get; } = [];
    public GmailInbox? Inbox { get; set; }
    public GmailMessage? Selected { get; set; }
    public string? Error { get; set; }
    public bool IsBusy { get; private set; }
    public CancellationTokenSource? SignInCancellation { get; private set; }
    public DateTimeOffset NextRefresh { get; set; } = DateTimeOffset.MinValue;
    public int PreviewIndex { get; set; }
    public bool ResetRotation { get; set; }
    public event Action? Changed;

    public void Initialize()
    {
        Auth ??= createAuth();
        _gmail ??= createGmail(Auth);
    }

    public async Task RestoreAsync(Func<bool> inboxOnly, CancellationToken token)
    {
        if (IsBusy || token.IsCancellationRequested) return;
        IsBusy = true;
        var restored = false;
        try
        {
            Initialize();
            restored = await Auth!.RestoreSavedSessionAsync(token);
            if (restored) { Error = null; NextRefresh = DateTimeOffset.MinValue; }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { Error = describeError(exception); }
        finally { IsBusy = false; Notify(token); }
        if (restored && !token.IsCancellationRequested) await RefreshAsync(inboxOnly, false, token);
    }

    public async Task RefreshAsync(Func<bool> inboxOnly, bool loadMore, CancellationToken token)
    {
        if (IsBusy || Auth?.IsSignedIn != true || _gmail is null || token.IsCancellationRequested) return;
        if (loadMore && Inbox?.NextPageToken is null) return;
        IsBusy = true;
        Error = null;
        NextRefresh = _clock.GetUtcNow().AddMinutes(1);
        Notify(token);
        var filter = inboxOnly();
        try
        {
            var inbox = await _gmail.GetUnreadAsync(filter, loadMore ? Inbox?.NextPageToken : null, token);
            if (token.IsCancellationRequested || filter != inboxOnly()) return;
            var previewId = Messages.ElementAtOrDefault(PreviewIndex)?.Id;
            if (!loadMore) Messages.Clear();
            var ids = Messages.Select(message => message.Id).ToHashSet();
            Messages.AddRange(inbox.Messages.Where(message => ids.Add(message.Id)));
            Inbox = inbox;
            PreviewIndex = Math.Max(0, Messages.FindIndex(message => message.Id == previewId));
            ResetRotation |= previewId is null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { Error = describeError(exception); }
        finally { IsBusy = false; Notify(token); }
    }

    public async Task ReadAsync(GmailMessage message, CancellationToken token)
    {
        if (IsBusy || _gmail is null || token.IsCancellationRequested) return;
        IsBusy = true; Selected = message; Error = null; Notify(token);
        try { Selected = await _gmail.GetMessageAsync(message.Id, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { Error = describeError(exception); }
        finally { IsBusy = false; Notify(token); }
    }

    public async Task<GmailDraft> CreateDraftAsync(string text, CancellationToken token)
    {
        Initialize();
        await Auth!.RestoreSavedSessionAsync(token);
        return await _gmail!.CreateDraftAsync(text, token);
    }

    public async Task SignInAsync(Func<bool> inboxOnly, CancellationToken token, bool authorizeDrafts = false)
    {
        if (IsBusy || Auth is null || token.IsCancellationRequested) return;
        IsBusy = true; Error = null;
        SignInCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        Notify(token);
        try { await Auth.SignInAsync(SignInCancellation.Token, authorizeDrafts); NextRefresh = DateTimeOffset.MinValue; }
        catch (OperationCanceledException) { Error = "Sign-in cancelled or timed out. You can try again."; }
        catch (Exception exception) { Error = describeError(exception); }
        finally
        {
            SignInCancellation.Dispose(); SignInCancellation = null; IsBusy = false; Notify(token);
        }
        if (!token.IsCancellationRequested && Auth.IsSignedIn && Error is null) await RefreshAsync(inboxOnly, false, token);
    }

    public async Task DisconnectAsync(CancellationToken token)
    {
        if (IsBusy || Auth is null || token.IsCancellationRequested) return;
        IsBusy = true;
        try
        {
            await Auth.DisconnectAsync(token);
            Inbox = null; Messages.Clear(); Selected = null; Error = null;
        }
        catch (Exception exception) { Error = describeError(exception); }
        finally { IsBusy = false; Notify(token); }
    }

    private void Notify(CancellationToken token) { if (!token.IsCancellationRequested) Changed?.Invoke(); }
    public void Dispose()
    {
        Changed = null;
        Messages.Clear(); Selected = null; Inbox = null;
        _gmail?.Dispose(); Auth?.Dispose();
    }
}
