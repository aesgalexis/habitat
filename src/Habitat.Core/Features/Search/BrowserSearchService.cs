using Habitat.Services;

namespace Habitat.Features.Search;

internal sealed record BrowserSearchRequest(string Id, string Query, int Start);
internal sealed class BrowserSearchException(string message) : Exception(message);

// Search pages are read in Chrome by the authenticated extension, without HTTP APIs.
internal sealed class BrowserSearchService(TimeProvider? clock = null, ComposerDestination destination = ComposerDestination.Google) : ISearchService
{
    internal ComposerDestination Destination { get; } = destination is ComposerDestination.Google or ComposerDestination.YouTube
        ? destination : throw new ArgumentOutOfRangeException(nameof(destination));
    private string Provider => Destination == ComposerDestination.YouTube ? "YouTube" : "Google";
    private string Host => Destination == ComposerDestination.YouTube ? "www.youtube.com" : "www.google.com";
    private readonly object _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private object? _owner;
    private DateTimeOffset _seen;
    private BrowserSearchRequest? _request;
    private TaskCompletionSource<SearchPage>? _completion;
    private string? _resetId;
    internal bool HasPendingReset { get { lock (_gate) return _resetId is not null; } }
    internal bool? LastResetClosedTab { get; private set; }
    internal string? LastDiagnostic { get; private set; }
    internal bool IsConnected { get { lock (_gate) return _owner is not null && _clock.GetUtcNow() - _seen < TimeSpan.FromSeconds(10); } }

    public async Task<SearchPage> SearchAsync(string query, int start, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (query.Length > 2048) throw new BrowserSearchException("Use 2,048 characters or fewer, then search again.");
        if (start is < 0 or > 990) throw new ArgumentOutOfRangeException(nameof(start));
        if (string.IsNullOrWhiteSpace(query)) return new SearchPage([]);
        TaskCompletionSource<SearchPage> completion;
        lock (_gate)
        {
            if (!IsConnected) throw new BrowserSearchException($"Open Chrome and reload the HABITAT extension to connect {Provider} Search, then retry.");
            if (_request is not null) throw new BrowserSearchException("Wait for the current search to finish.");
            LastDiagnostic = null;
            _request = new BrowserSearchRequest(Guid.NewGuid().ToString("N"), query, start);
            _completion = completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try { return await completion.Task.WaitAsync(TimeSpan.FromSeconds(55), _clock, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { throw new BrowserSearchException(PageError("timeout")); }
        finally
        {
            lock (_gate)
                if (ReferenceEquals(_completion, completion)) { _request = null; _completion = null; }
        }
    }

    internal void Attach(object owner) { lock (_gate) { _owner = owner; _seen = _clock.GetUtcNow(); } }
    internal void Touch(object owner) { lock (_gate) if (ReferenceEquals(_owner, owner)) _seen = _clock.GetUtcNow(); }
    internal BrowserSearchRequest? Pending(object owner) { lock (_gate) return ReferenceEquals(_owner, owner) ? _request : null; }
    internal (BrowserSearchRequest? Request, string? Reset) PendingState(object owner)
    {
        lock (_gate) return ReferenceEquals(_owner, owner) ? (_resetId is null ? _request : null, _resetId) : (null, null);
    }

    public void Reset()
    {
        lock (_gate)
        {
            _request = null;
            _completion?.TrySetCanceled();
            _completion = null;
            _resetId ??= Guid.NewGuid().ToString("N");
            LastResetClosedTab = null;
        }
    }

    internal void AcknowledgeReset(object owner, string? id, bool closed)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_owner, owner) || _resetId is null || _resetId != id) return;
            _resetId = null;
            LastResetClosedTab = closed;
        }
    }
    internal static bool IsSafeLink(string? value) => value is { Length: <= 8192 } &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && uri.UserInfo.Length == 0;
    internal static Uri PageUri(string query, int start) => new("https://www.google.com/search?q=" +
        Uri.EscapeDataString(query) + "&udm=14&hl=en&start=" + start);
    internal static Uri PageUri(ComposerDestination destination, string query, int start) => destination == ComposerDestination.YouTube
        ? new Uri("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query) + "&hl=en") : PageUri(query, start);
    internal static bool IsVideoLink(string? value) => IsSafeLink(value) && Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.Host == "www.youtube.com" && uri.IsDefaultPort && uri.Fragment.Length == 0 && uri.AbsolutePath == "/watch" &&
        System.Text.RegularExpressions.Regex.IsMatch(uri.Query, @"^\?v=[A-Za-z0-9_-]{11}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    internal static Uri? ThumbnailUri(string? videoUrl) => IsVideoLink(videoUrl)
        ? new Uri("https://i.ytimg.com/vi/" + new Uri(videoUrl!).Query[3..] + "/mqdefault.jpg") : null;

    internal void Complete(object owner, string? id, SearchResult[]? results, int? nextStart, string? error, string? diagnostic = null)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_owner, owner) || _request is not { } request || request.Id != id || _completion is null) return;
            LastDiagnostic = diagnostic?[..Math.Min(400, diagnostic.Length)];
            if (!string.IsNullOrEmpty(error)) _completion.TrySetException(new BrowserSearchException(PageError(error)));
            else if (results is { Length: <= 30 } && results.All(item => item is not null &&
                !string.IsNullOrWhiteSpace(item.Title) && item.Title.Length <= 600 &&
                item.Snippet is { Length: <= 2000 } && (Destination == ComposerDestination.YouTube ? IsVideoLink(item.Url) : IsSafeLink(item.Url))) &&
                (nextStart is null || nextStart > request.Start && nextStart <= 990))
                _completion.TrySetResult(new SearchPage(results.ToArray(), nextStart));
            else _completion.TrySetException(new BrowserSearchException(PageError("result")));
        }
    }

    internal void Detach(object owner)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_owner, owner)) return;
            _owner = null;
            _completion?.TrySetException(new BrowserSearchException("Chrome disconnected. Reconnect the HABITAT extension, then retry."));
        }
    }

    private string PageError(string error) => error switch
    {
        "consent" => $"Open the {Provider} search tab in Chrome and complete its consent screen, then retry.",
        "blocked" => $"Open the {Provider} search tab in Chrome and complete its verification, then retry.",
        "permission" => $"Reload the HABITAT extension and allow access to {Host}, then retry.",
        "background-permission" => $"Reload the HABITAT extension and allow its background search permission, then retry.",
        "background-unavailable" => $"Could not keep {Provider} active in the background. Close its DevTools or reload the HABITAT extension, then retry.",
        "page-changed" => $"The {Provider} search tab changed. Retry to open a new search tab.",
        "timeout" => $"{Provider} did not return search results. Check its tab in Chrome, then retry.",
        _ => $"Could not read {Provider} search results. Open the search in Chrome or retry."
    };

    internal static string ErrorMessage(Exception exception, ComposerDestination destination = ComposerDestination.Google) => exception is BrowserSearchException
        ? exception.Message : $"Could not search {(destination == ComposerDestination.YouTube ? "YouTube" : "Google")}. Check Chrome and try again.";
}
