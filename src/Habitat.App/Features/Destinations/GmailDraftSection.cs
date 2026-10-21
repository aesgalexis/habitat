using Habitat.Models;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat.Features.Destinations;

internal sealed class GmailDraftSection : IDestinationSection
{
    private readonly StackPanel _view = new() { Spacing = 12 };
    private readonly TextBlock _status = new() { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
    private readonly Button _browser = UiLanguage.Watch(new Button() { Content = UiText.Get("OpenInBrowser") }, ("Content", () => (object?)(UiText.Get("OpenInBrowser"))));
    private readonly Button _clear = UiLanguage.Watch(new Button() { Content = UiText.Get("Clear") }, ("Content", () => (object?)(UiText.Get("Clear"))));
    private readonly Button _authorize = UiLanguage.Watch(new Button() { Content = UiText.Get("AuthorizeGmailDrafts"), Visibility = Visibility.Collapsed }, ("Content", () => (object?)(UiText.Get("AuthorizeGmailDrafts"))));
    private readonly Func<string, CancellationToken, Task<GmailDraft>> _save;
    private readonly Func<Task> _authorizeDrafts;
    private readonly Func<string?> _email;
    private readonly Func<Uri, Task<bool>> _open;
    private readonly Action<Exception> _reportError;
    private readonly AsyncOperationScope _operations = new();
    private GmailDraft? _saved;
    private bool _disposed;
    public string Destination => ComposerDestination.Gmail;
    public FrameworkElement View => _view;
    public bool IsBusy { get; private set; }
    public bool HasContent { get; private set; }
    public event Action? StateChanged;
    internal event Action? ClearRequested;

    public GmailDraftSection(Func<string, CancellationToken, Task<GmailDraft>> save, Func<Task> authorizeDrafts,
        Func<string?> email, Func<Uri, Task<bool>> open, Action<Exception> reportError)
    {
        _save = save; _authorizeDrafts = authorizeDrafts; _email = email; _open = open; _reportError = reportError;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(_browser);
        actions.Children.Add(_clear);
        _view.Children.Add(actions);
        _view.Children.Add(_status);
        _view.Children.Add(_authorize);
        UiLanguage.SetName(_browser, () => UiText.Get("OpenInBrowser"));
        UiLanguage.SetToolTip(_browser, () => UiText.Get("OpenTheSavedDraftOrGmailDraftsIn"));
        UiLanguage.SetName(_clear, () => UiText.Get("Clear"));
        UiLanguage.SetToolTip(_clear, () => UiText.Get("ClearTheEditorSavedGmailDraftsAreKept"));
        UiLanguage.SetName(_authorize, () => UiText.Get("AuthorizeGmailDrafts"));
        AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        _browser.Click += async (_, _) => await OpenBrowserAsync();
        _clear.Click += (_, _) => RequestClear();
        _authorize.Click += async (_, _) => await _operations.RunAsync(AuthorizeAsync);
    }

    public Task SubmitAsync(string text) => _operations.RunAsync(async () =>
    {
        if (_disposed || IsBusy || string.IsNullOrWhiteSpace(text)) return;
        HasContent = true;
        SetBusy(true);
        _saved = null;
        _status.Text = UiLanguage.Value(_status, "Text", () => UiText.Get("SavingDraft"));
        try
        {
            var saved = await _save(text, _operations.Token);
            if (_disposed) return;
            _saved = saved;
            _status.Text = UiLanguage.Value(_status, "Text", () => UiText.Get("DraftSavedSubject") + saved.Subject);
            _authorize.Visibility = Visibility.Collapsed;
        }
        catch (GmailDraftAuthorizationException exception)
        { if (!_disposed) { _status.Text = exception.Message; _authorize.Visibility = Visibility.Visible; } }
        catch (GmailSignInRequiredException)
        { if (!_disposed) { _status.Text = UiLanguage.Value(_status, "Text", () => UiText.Get("AuthorizeGmailDraftsThenPressDraftEmailAgain")); _authorize.Visibility = Visibility.Visible; } }
        catch (OperationCanceledException) when (_operations.Token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _reportError(exception);
            if (!_disposed) _status.Text = UiLanguage.Value(_status, "Text", () => UiText.Get("CouldNotConfirmTheSavedDraftCheckGmail"));
        }
        finally { if (!_disposed) SetBusy(false); }
    });

    internal Task OpenBrowserAsync() => _operations.RunAsync(async () =>
    {
        if (_disposed || IsBusy) return;
        SetBusy(true);
        try
        {
            if (!await _open(_saved?.BrowserUri(_email()) ?? GmailDraft.DraftsUri(_email())) && !_disposed)
                ShowBrowserError();
        }
        catch (Exception exception)
        { _reportError(exception); if (!_disposed) ShowBrowserError(); }
        finally { if (!_disposed) SetBusy(false); }
    });

    private async Task AuthorizeAsync()
    {
        if (_disposed || IsBusy) return;
        SetBusy(true);
        try { await _authorizeDrafts(); if (!_disposed) _status.Text = UiLanguage.Value(_status, "Text", () => UiText.Get("AfterAuthorizingGmailDraftsPressDraftEmailAgain")); }
        catch (Exception exception)
        { _reportError(exception); if (!_disposed) _status.Text = UiLanguage.Value(_status, "Text", () => UiText.Get("CouldNotAuthorizeGmailDraftsOpenGmailSetup")); }
        finally { if (!_disposed) SetBusy(false); }
    }

    private void ShowBrowserError() { HasContent = true; _status.Text = UiLanguage.Value(_status, "Text", () => UiText.Get("CouldNotOpenGmailInYourBrowserTry")); }
    private void Reset()
    {
        _saved = null;
        HasContent = false;
        _status.Text = "";
        _authorize.Visibility = Visibility.Collapsed;
    }
    internal string StatusText => _status.Text;
    internal void RequestClear()
    {
        if (_disposed || IsBusy) return;
        Reset();
        ClearRequested?.Invoke();
        StateChanged?.Invoke();
    }
    private void SetBusy(bool busy)
    { IsBusy = busy; _browser.IsEnabled = _clear.IsEnabled = _authorize.IsEnabled = !busy; StateChanged?.Invoke(); }
    public void Dispose() { if (_disposed) return; _disposed = true; StateChanged = null; ClearRequested = null; _ = _operations.StopAsync(); }
    public Task StopAsync() { Dispose(); return _operations.StopAsync(); }
}
