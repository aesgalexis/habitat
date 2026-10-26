namespace Habitat.Features.Facebook;

internal sealed record FacebookContact(string Name, string PhotoUrl, string? Link = null, bool? IsOnline = null);
internal sealed record FacebookContactsRequest(string Id);
internal sealed record FacebookReadDiagnostic(string Stage, int? Sections = null, int? Photos = null, int? Rows = null);
internal sealed record FacebookStatus(bool Connected, string? ExtensionVersion, double? HeartbeatAgeSeconds,
    bool Reading, string Stage, string? Error, int? ContactCount, FacebookReadDiagnostic? Reader);
internal interface IFacebookContactsService
{
    bool IsConnected { get; }
    Task<IReadOnlyList<FacebookContact>> ReadAsync(CancellationToken token);
}

// Snapshots are read from the user's Facebook page by the Chrome extension.
// They live in memory only; this service never authenticates with Facebook.
internal sealed class FacebookContactsService(TimeProvider? clock = null) : IFacebookContactsService
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private object? _owner;
    private DateTimeOffset _seen;
    private FacebookContactsRequest? _request;
    private TaskCompletionSource<IReadOnlyList<FacebookContact>>? _completion;
    private string? _extensionVersion, _error;
    private string _stage = "waiting-for-extension";
    private int? _contactCount;
    private FacebookReadDiagnostic? _reader;
    public bool IsConnected { get { lock (_gate) return _owner is not null && _clock.GetUtcNow() - _seen < TimeSpan.FromSeconds(10); } }
    // Diagnostic metadata only. Never retain names, URLs, page text or snapshots here.
    internal FacebookStatus Status
    {
        get { lock (_gate) return new(IsConnected, _extensionVersion,
            _seen == default ? null : (_clock.GetUtcNow() - _seen).TotalSeconds,
            _request is not null, _stage, _error, _contactCount, _reader); }
    }

    public async Task<IReadOnlyList<FacebookContact>> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        TaskCompletionSource<IReadOnlyList<FacebookContact>> completion;
        lock (_gate)
        {
            if (!IsConnected)
            {
                _stage = "waiting-for-extension"; _error = "disconnected";
                throw new InvalidOperationException("Facebook is not connected. In Chrome, open the HABITAT extension popup and choose Reconnect Facebook. If it offers Reload extension for Facebook, use it first.");
            }
            if (_request is not null) throw new InvalidOperationException("Wait for the current Facebook refresh to finish.");
            _request = new(Guid.NewGuid().ToString("N"));
            _completion = completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _stage = "queued"; _error = null; _contactCount = null; _reader = null;
        }
        try { return await completion.Task.WaitAsync(TimeSpan.FromSeconds(25), _clock, token).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            lock (_gate) if (ReferenceEquals(_completion, completion)) { _stage = "failed"; _error = "timeout"; }
            throw new InvalidOperationException("Facebook did not respond. Open its tab in Chrome, then retry.");
        }
        catch (OperationCanceledException)
        {
            lock (_gate) if (ReferenceEquals(_completion, completion)) _stage = "cancelled";
            throw;
        }
        finally
        {
            lock (_gate) if (ReferenceEquals(_completion, completion)) { _request = null; _completion = null; }
        }
    }

    internal void Attach(object owner, string? extensionVersion = null)
    {
        lock (_gate)
        {
            _owner = owner; _seen = _clock.GetUtcNow();
            _extensionVersion = extensionVersion is { Length: <= 32 } &&
                System.Text.RegularExpressions.Regex.IsMatch(extensionVersion, @"^\d+(\.\d+){1,3}$") ? extensionVersion : null;
            _stage = _request is null ? "connected" : "queued"; _error = null;
        }
    }
    internal void Touch(object owner) { lock (_gate) if (ReferenceEquals(owner, _owner)) _seen = _clock.GetUtcNow(); }
    internal FacebookContactsRequest? Pending(object owner) { lock (_gate) return ReferenceEquals(owner, _owner) ? _request : null; }
    internal void Detach(object owner)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(owner, _owner)) return;
            _owner = null;
            _stage = "disconnected"; _error = "disconnected";
            _completion?.TrySetException(new InvalidOperationException("Chrome disconnected. Reconnect the HABITAT extension to load Facebook contacts."));
        }
    }

    internal void Progress(object owner, string? id, FacebookReadDiagnostic diagnostic)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(owner, _owner) || _request?.Id != id || _completion is null || _completion.Task.IsCompleted) return;
            if (diagnostic.Stage is not ("permission" or "tab-selection" or "page-loading" or "page-read" or "section-missing" or "section-read")) return;
            static int? Count(int? value) => value is >= 0 and <= 10000 ? value : null;
            _reader = new(diagnostic.Stage, Count(diagnostic.Sections), Count(diagnostic.Photos), Count(diagnostic.Rows));
            _stage = "reading";
        }
    }

    internal void Complete(object owner, string? id, FacebookContact[]? contacts, string? error)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(owner, _owner) || _request?.Id != id || _completion is null || _completion.Task.IsCompleted) return;
            if (!string.IsNullOrEmpty(error))
            {
                _error = error is "permission" or "login" or "blocked" or "page-changed" or "contacts-unavailable" ? error : "result";
                _stage = "failed";
                _completion.TrySetException(new InvalidOperationException(Error(_error)));
            }
            else if (contacts is { Length: <= 200 } && contacts.All(contact => contact is not null &&
                !string.IsNullOrWhiteSpace(contact.Name) && contact.Name.Length <= 200 && SafePhoto(contact.PhotoUrl) &&
                (contact.Link is null || SafeLink(contact.Link))))
            {
                var snapshot = contacts.DistinctBy(contact => (contact.Name, contact.PhotoUrl)).ToArray();
                _stage = snapshot.Length == 0 ? "empty" : "loaded"; _error = null; _contactCount = snapshot.Length;
                _completion.TrySetResult(snapshot);
            }
            else
            {
                _stage = "failed"; _error = "result";
                _completion.TrySetException(new InvalidOperationException(Error("result")));
            }
        }
    }

    private static bool Https(string? value, out Uri uri)
    {
        uri = null!;
        return value is { Length: <= 8192 } && Uri.TryCreate(value, UriKind.Absolute, out uri!) &&
            uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0;
    }
    internal static bool SafePhoto(string? value) => Https(value, out var uri) &&
        (uri.Host.EndsWith(".fbcdn.net", StringComparison.OrdinalIgnoreCase) || uri.Host == "fbcdn.net");
    internal static bool SafeLink(string? value)
    {
        if (!Https(value, out var uri) || uri.Host is not ("www.facebook.com" or "facebook.com")) return false;
        var path = uri.AbsolutePath.Trim('/');
        if (path == "profile.php") return System.Text.RegularExpressions.Regex.IsMatch(uri.Query, @"^\?id=\d+$");
        if (System.Text.RegularExpressions.Regex.IsMatch(path, @"^messages/(?:e2ee/)?t/[A-Za-z0-9._-]+$")) return uri.Query.Length == 0;
        return uri.Query.Length == 0 && System.Text.RegularExpressions.Regex.IsMatch(path, @"^[A-Za-z0-9.]+$") &&
            path is not ("login" or "checkpoint" or "recover" or "friends" or "groups" or "marketplace" or "watch" or "reel" or "stories" or "settings" or "logout.php" or "l.php" or "help" or "notifications");
    }
    private static string Error(string code) => code switch
    {
        "permission" => "In the HABITAT extension popup, choose Enable Facebook contacts and allow access to Facebook, then retry.",
        "login" => "Sign in to Facebook in Chrome, then retry.",
        "blocked" => "Complete Facebook's verification in Chrome, then retry.",
        "page-changed" => "Open Facebook All friends in Chrome, then retry.",
        "contacts-unavailable" => "Open Friends → All friends in Facebook in Chrome, wait for the profile photos, then retry.",
        _ => "Could not read Facebook contacts. Open Facebook in Chrome, then retry."
    };
}
