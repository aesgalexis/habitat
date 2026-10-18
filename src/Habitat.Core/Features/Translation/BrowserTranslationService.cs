using System.Text;

namespace Habitat.Features.Translation;

internal sealed record BrowserTranslationRequest(string Id, string Text, string Target);
internal sealed class BrowserTranslationException(string message) : Exception(message);

// Only the authenticated Chrome extension can complete a browser-page job.
// No translation provider HTTP requests are made by the desktop app.
internal sealed class BrowserTranslationService(TimeProvider? clock = null) : ITranslationService
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private object? _owner;
    private DateTimeOffset _seen;
    private BrowserTranslationRequest? _request;
    private TaskCompletionSource<TranslationResult>? _completion;
    internal string? LastDiagnostic { get; private set; }
    internal bool IsConnected { get { lock (_gate) return _owner is not null && _clock.GetUtcNow() - _seen < TimeSpan.FromSeconds(10); } }

    public async Task<TranslationResult> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TranslationLanguages.IsSupported(targetLanguage)) throw new ArgumentException("Unsupported translation language.");
        if (text.EnumerateRunes().Count() > 5000) throw new BrowserTranslationException("Use 5,000 characters or fewer in Google Translate, then try again.");
        if (string.IsNullOrWhiteSpace(text)) return new TranslationResult(text, null);
        TaskCompletionSource<TranslationResult> completion;
        lock (_gate)
        {
            if (!IsConnected) throw new BrowserTranslationException("Open your selected browser and reload the HABITAT extension to connect Google Translate, then retry.");
            if (_request is not null) throw new BrowserTranslationException("Wait for the current translation to finish.");
            LastDiagnostic = null;
            _request = new BrowserTranslationRequest(Guid.NewGuid().ToString("N"), text, targetLanguage);
            _completion = completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try { return await completion.Task.WaitAsync(TimeSpan.FromSeconds(55), _clock, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            lock (_gate) LastDiagnostic = "desktop-timeout";
            throw new BrowserTranslationException(PageError("timeout"));
        }
        finally
        {
            lock (_gate)
                if (ReferenceEquals(_completion, completion)) { _request = null; _completion = null; }
        }
    }

    internal void Attach(object owner) { lock (_gate) { _owner = owner; _seen = _clock.GetUtcNow(); } }
    internal void Touch(object owner) { lock (_gate) if (ReferenceEquals(_owner, owner)) _seen = _clock.GetUtcNow(); }
    internal BrowserTranslationRequest? Pending(object owner) { lock (_gate) return ReferenceEquals(_owner, owner) ? _request : null; }
    internal void Complete(object owner, string? id, string? text, string? error, string? diagnostic = null)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_owner, owner) || _request?.Id != id || _completion is null) return;
            LastDiagnostic = diagnostic?[..Math.Min(diagnostic.Length, 160)];
            if (!string.IsNullOrEmpty(error)) _completion.TrySetException(new BrowserTranslationException(PageError(error)));
            else if (!string.IsNullOrEmpty(text) && text.Length <= 30000) _completion.TrySetResult(new TranslationResult(text, null));
            else _completion.TrySetException(new BrowserTranslationException(PageError("result")));
        }
    }

    internal void Detach(object owner)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_owner, owner)) return;
            _owner = null;
            _completion?.TrySetException(new BrowserTranslationException("The selected browser disconnected. Reconnect the HABITAT extension, then retry."));
        }
    }

    private static string PageError(string error) => error switch
    {
        "consent" => "Open Google Translate in your selected browser and complete its consent screen, then retry.",
        "permission" => "Reload the HABITAT extension and allow access to translate.google.com, then retry.",
        "background-permission" => "Reload the HABITAT extension and allow its background translation permission, then retry.",
        "background-unavailable" => "Could not keep Google Translate active in the background. Close its DevTools or reload the HABITAT extension, then retry.",
        "page-changed" => "The Google Translate tab changed. Retry to reopen the translator.",
        "timeout" => "Google Translate did not return a result. Check its tab in your selected browser, then retry.",
        _ => "Could not read the translation from Google Translate. Check its tab in your selected browser, then retry."
    };

    internal static string ErrorMessage(Exception exception) => exception switch
    {
        BrowserTranslationException => exception.Message,
        OperationCanceledException => "Translation timed out. Try again.",
        _ => "Could not translate in your selected browser. Check Google Translate and try again."
    };
}
