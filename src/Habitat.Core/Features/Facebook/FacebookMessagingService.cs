namespace Habitat.Features.Facebook;

internal sealed record FacebookMessageRequest(string Id, string TargetLink, string TargetName, string Text, bool InspectOnly = false);
internal sealed record FacebookMessageResult(string Status, string? Error = null);
internal interface IFacebookMessagingService
{
    bool IsAvailable { get; }
    Task<FacebookMessageResult> SendAsync(string targetLink, string targetName, string text, CancellationToken token);
}

// One user submission, one operation ID. Never retry a send after a timeout or
// connection loss: Messenger may already have accepted it.
internal sealed class FacebookMessagingService(TimeProvider? clock = null, string? tracePath = null) : IFacebookMessagingService
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly FacebookMessageTrace _trace = new(clock ?? TimeProvider.System, tracePath);
    internal FacebookMessageTraceEntry? LastDiagnostic => _trace.Last;
    private object? _owner;
    private DateTimeOffset _seen;
    private bool _supported;
    private FacebookMessageRequest? _request;
    private TaskCompletionSource<FacebookMessageResult>? _completion;
    internal bool IsBusy { get { lock (_gate) return _request is not null; } }
    public bool IsAvailable { get { lock (_gate) return _supported && _owner is not null && _clock.GetUtcNow() - _seen < TimeSpan.FromSeconds(10); } }

    public Task<FacebookMessageResult> SendAsync(string targetLink, string targetName, string text, CancellationToken token) =>
        RunAsync(targetLink, targetName, text, false, token);
    internal Task<FacebookMessageResult> InspectAsync(string targetLink, string targetName, CancellationToken token) =>
        RunAsync(targetLink, targetName, "inspection", true, token);
    private async Task<FacebookMessageResult> RunAsync(string targetLink, string targetName, string text, bool inspectOnly, CancellationToken token)
    {
        var operation = Guid.NewGuid().ToString("N");
        token.ThrowIfCancellationRequested();
        if (!FacebookContactsService.SafeLink(targetLink) || string.IsNullOrWhiteSpace(targetName) || targetName.Length > 200 || string.IsNullOrWhiteSpace(text) || text.Length > 5000)
            throw new InvalidOperationException("Choose a contact with a verified Facebook link and enter a message of up to 5,000 characters.");
        TaskCompletionSource<FacebookMessageResult> completion;
        lock (_gate)
        {
            if (_owner is null || _clock.GetUtcNow() - _seen >= TimeSpan.FromSeconds(10))
            {
                _trace.Write(operation, "rejected", "not-sent", "disconnected");
                throw new InvalidOperationException("Facebook is disconnected. Reconnect the HABITAT extension before sending.");
            }
            if (!_supported)
            {
                _trace.Write(operation, "rejected", "not-sent", "extension");
                throw new InvalidOperationException("Reload HABITAT Browser Bridge 0.10.7 or later in Chrome to enable Messenger messaging and diagnostics.");
            }
            if (_request is not null) throw new InvalidOperationException("Wait for the current Messenger submission to finish.");
            _request = new(operation, targetLink, targetName, text, inspectOnly);
            _trace.Write(operation, "queued");
            _completion = completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try
        {
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(45), _clock, token).ConfigureAwait(false);
            _trace.Write(operation, "completed", result.Status, result.Error);
            return result;
        }
        catch (TimeoutException) { _trace.Write(operation, "completed", "uncertain", "timeout"); return new("uncertain", "timeout"); }
        catch (OperationCanceledException) { _trace.Write(operation, "completed", "uncertain", "cancelled"); throw; }
        finally
        {
            lock (_gate) if (ReferenceEquals(_completion, completion)) { _request = null; _completion = null; }
        }
    }

    internal void Progress(object owner, string? id, string? stage, FacebookMessageDiagnostic? diagnostic)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(owner, _owner) || _request?.Id != id || _completion?.Task.IsCompleted != false) return;
            if (stage is not ("received" or "permission" or "claimed" or "opening-profile" or "waiting-page" or
                "open" or "resolve" or "opening-chat" or "navigating-thread" or "ready" or "prepare" or "send" or "confirm" or "failed")) return;
            static int? Count(int? value) => value is >= 0 and <= 20000 ? value : null;
            var safe = diagnostic is null ? null : new FacebookMessageDiagnostic(
                diagnostic.Page is "profile" or "thread" or "login" or "checkpoint" or "other" ? diagnostic.Page : null,
                Count(diagnostic.Mains), Count(diagnostic.Headings), Count(diagnostic.NameMatches), Count(diagnostic.MessageButtons),
                Count(diagnostic.MessageLinks), Count(diagnostic.Popups), Count(diagnostic.IdentityMatches), Count(diagnostic.Editors), Count(diagnostic.SendButtons),
                Count(diagnostic.ProfileAliases), Count(diagnostic.ChatHeadings), Count(diagnostic.MenuItems));
            _trace.Write(id!, stage, details: safe);
        }
    }

    internal void Attach(object owner, bool supported)
    {
        lock (_gate)
        {
            if (_owner is not null && !ReferenceEquals(owner, _owner)) _completion?.TrySetResult(new("uncertain", "disconnected"));
            _owner = owner; _supported = supported; _seen = _clock.GetUtcNow();
        }
    }
    internal void Touch(object owner) { lock (_gate) if (ReferenceEquals(owner, _owner)) _seen = _clock.GetUtcNow(); }
    internal FacebookMessageRequest? Pending(object owner) { lock (_gate) return ReferenceEquals(owner, _owner) ? _request : null; }
    internal void Detach(object owner)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(owner, _owner)) return;
            _owner = null; _supported = false;
            _completion?.TrySetResult(new("uncertain", "disconnected"));
        }
    }
    internal void Complete(object owner, string? id, string? status, string? error)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(owner, _owner) || _request is null || _request.Id != id) return;
            _completion?.TrySetResult(new(status is "submitted" or "not-sent" ? status : "uncertain",
                error is "permission" or "login" or "blocked" or "recipient" or "composer" or "draft" or "unsupported" ? error :
                    _request.InspectOnly && error == "inspected" ? "inspected" : null));
        }
    }
}
