using Habitat.Features.Destinations;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat.Features.Translation;

internal sealed class TranslateSection : IDestinationSection
{
    private readonly Grid _view = new() { RowSpacing = 12 };
    private readonly ComboBox _target = UiLanguage.Watch(new ComboBox() { Header = UiText.Get("TranslateTo"), DisplayMemberPath = "Name", HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 0, MaxWidth = 220 }, ("Header", () => (object?)(UiText.Get("TranslateTo"))));
    private readonly TextBlock _result = new() { FontSize = 15, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly TextBlock _status = UiLanguage.Watch(new TextBlock() { Text = UiText.Get("WriteTextBelowAndPressTranslate"), FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap }, ("Text", () => (object?)(UiText.Get("WriteTextBelowAndPressTranslate"))));
    private readonly Button _copy = new() { Content = new FontIcon { Glyph = "\uE8C8", FontSize = 16 }, IsEnabled = false,
        HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Grid _copyActions = new() { ColumnSpacing = 8, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _copyStatus = new() { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly Button _browser = UiLanguage.Watch(new Button() { Content = UiText.Get("OpenInBrowser"), IsEnabled = false }, ("Content", () => (object?)(UiText.Get("OpenInBrowser"))));
    private readonly Button _clear = UiLanguage.Watch(new Button() { Content = UiText.Get("ClearTranslation") }, ("Content", () => (object?)(UiText.Get("ClearTranslation"))));
    private readonly Button _retry = UiLanguage.Watch(new Button() { Content = UiText.Get("Retry"), Visibility = Visibility.Collapsed }, ("Content", () => (object?)(UiText.Get("Retry"))));
    private readonly Button _cancel = UiLanguage.Watch(new Button() { Content = UiText.Get("Cancel"), Visibility = Visibility.Collapsed }, ("Content", () => (object?)(UiText.Get("Cancel"))));
    private readonly Func<string, string, CancellationToken, Task<TranslationResult>> _translate;
    private readonly Func<string, bool> _copyText;
    private readonly Func<Uri, Task<bool>> _open;
    private readonly Action<Exception> _reportError;
    private readonly AsyncOperationScope _operations = new();
    private CancellationTokenSource? _request;
    private string _lastText = "";
    private int _revision;
    private bool _userCanceled;
    private bool _disposed;
    private bool _opening;
    private bool _refreshingLanguage;
    public string Destination => ComposerDestination.Translate;
    public FrameworkElement View => _view;
    public bool IsBusy => _request is not null;
    public bool HasContent { get; private set; }
    public event Action? StateChanged;
    internal event Action? ClearRequested;
    internal string ResultText => _result.Text;
    internal string StatusText => _status.Text;
    internal string CopyStatusText => _copyStatus.Text;
    internal bool CanCopy => _copy.IsEnabled;
    internal string TargetLanguage => ((TranslationLanguage)_target.SelectedItem).Code;

    public TranslateSection(Func<string, string, CancellationToken, Task<TranslationResult>> translate,
        Func<string, bool> copyText, Action<Exception> reportError, Func<Uri, Task<bool>>? open = null)
    {
        _translate = translate;
        _copyText = copyText;
        _reportError = reportError;
        _open = open ?? (_ => Task.FromResult(false));
        _target.ItemsSource = TranslationLanguages.All;
        _target.SelectedItem = TranslationLanguages.All.First(language => language.Code == TranslationLanguages.DefaultCode);
        _target.SelectionChanged += (_, _) =>
        {
            if (_disposed || _refreshingLanguage) return;
            _revision++;
            _request?.Cancel();
            _result.Text = "";
            _copy.IsEnabled = false;
            _copyActions.Visibility = Visibility.Collapsed;
            ClearCopyStatus();
            ShowStatus(UiText.Get("PressTranslateToUseTheSelectedLanguage"));
            _retry.Visibility = Visibility.Collapsed;
        };
        UiLanguage.SetName(_target, () => UiText.Get("TranslateTo"));
        UiLanguage.SetName(_result, () => UiText.Get("TranslatedText"));
        UiLanguage.SetName(_copy, () => UiText.Get("CopyTranslation"));
        UiLanguage.SetToolTip(_copy, () => UiText.Get("CopyTranslation"));
        UiLanguage.SetName(_clear, () => UiText.Get("ClearTranslation"));
        UiLanguage.SetToolTip(_clear, () => UiText.Get("ClearTheSourceTextAndTranslation"));
        AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(_copyStatus, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        for (var row = 0; row < 3; row++) _view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var toolbar = new Grid { ColumnSpacing = 12 };
        toolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        toolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.Children.Add(_target);
        var translationActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        translationActions.Children.Add(_browser);
        translationActions.Children.Add(_clear);
        Grid.SetColumn(translationActions, 1);
        toolbar.Children.Add(translationActions);
        toolbar.SizeChanged += (_, args) =>
        {
            var stacked = args.NewSize.Width < translationActions.DesiredSize.Width + 180;
            toolbar.RowSpacing = stacked ? 8 : 0;
            Grid.SetColumnSpan(_target, stacked ? 2 : 1);
            Grid.SetRow(translationActions, stacked ? 1 : 0);
            Grid.SetColumn(translationActions, stacked ? 0 : 1);
            Grid.SetColumnSpan(translationActions, stacked ? 2 : 1);
        };
        _view.Children.Add(toolbar);
        Grid.SetRow(_status, 1);
        _view.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(_retry);
        actions.Children.Add(_cancel);
        Grid.SetRow(actions, 2);
        _view.Children.Add(actions);
        var translated = new StackPanel { Spacing = 4 };
        translated.Children.Add(_result);
        _copyActions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _copyActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _copyActions.Children.Add(_copy);
        Grid.SetColumn(_copyStatus, 1);
        _copyActions.Children.Add(_copyStatus);
        translated.Children.Add(_copyActions);
        var scroll = new ScrollViewer { Content = translated, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 3);
        _view.Children.Add(scroll);
        _copy.Click += (_, _) => CopyResult();
        _browser.Click += async (_, _) => await OpenTranslationAsync();
        _clear.Click += (_, _) => RequestClear();
        _cancel.Click += (_, _) => Cancel();
        _retry.Click += async (_, _) => await RetryAsync();
    }

    public Task SubmitAsync(string text) => _operations.RunAsync(() => SubmitCoreAsync(text));

    private async Task SubmitCoreAsync(string text)
    {
        if (_disposed || IsBusy || string.IsNullOrWhiteSpace(text)) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_operations.Token);
        request.CancelAfter(TimeSpan.FromSeconds(60));
        _request = request;
        HasContent = true;
        _userCanceled = false;
        var revision = ++_revision;
        var targetLanguage = TargetLanguage;
        _lastText = text;
        _browser.IsEnabled = !_opening;
        _result.Text = "";
        _copy.IsEnabled = false;
        _copyActions.Visibility = Visibility.Collapsed;
        ClearCopyStatus();
        _retry.Visibility = Visibility.Collapsed;
        _cancel.Visibility = Visibility.Visible;
        ShowStatus(UiText.Get("Translating"));
        StateChanged?.Invoke();
        try
        {
            var provider = _operations.RunAsync(() => _translate(text, targetLanguage, request.Token), default(TranslationResult)!);
            var result = await provider.WaitAsync(request.Token);
            if (_disposed || revision != _revision) return;
            request.Token.ThrowIfCancellationRequested();
            _result.Text = result.Text;
            _copy.IsEnabled = result.Text.Length > 0;
            _copyActions.Visibility = _copy.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            _status.Text = "";
            _status.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            if (!_disposed && revision == _revision)
            {
                ShowStatus(_userCanceled ? UiText.Get("TranslationCanceled") : UiText.Get("TranslationTimedOutTryAgain"));
                _retry.Visibility = Visibility.Visible;
            }
        }
        catch (Exception exception)
        {
            if (!_disposed && revision == _revision)
            {
                _reportError(exception);
                ShowStatus(BrowserTranslationService.ErrorMessage(exception));
                _retry.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (ReferenceEquals(_request, request))
            {
                _request = null;
                if (!_disposed)
                {
                    _cancel.Visibility = Visibility.Collapsed;
                    StateChanged?.Invoke();
                }
            }
        }
    }

    internal void SelectTarget(string code) => _target.SelectedItem = TranslationLanguages.All.First(language => language.Code == code);
    internal void RefreshLanguage()
    {
        var selected = TargetLanguage;
        _refreshingLanguage = true;
        try
        {
            _target.ItemsSource = null;
            _target.ItemsSource = TranslationLanguages.All;
            SelectTarget(selected);
        }
        finally { _refreshingLanguage = false; }
        _status.Text = UiLanguage.Value(_status, "Text", () => UiText.LocalizeMessage(_status.Text));
    }
    internal Task RetryAsync() => SubmitAsync(_lastText);
    internal void Cancel() { _userCanceled = true; _request?.Cancel(); }
    internal void RequestClear() { if (!_disposed) ClearRequested?.Invoke(); }

    internal void Reset()
    {
        if (_disposed) return;
        _revision++;
        var request = _request;
        _request = null;
        request?.Cancel();
        HasContent = false;
        _lastText = "";
        _result.Text = _status.Text = "";
        _copy.IsEnabled = _browser.IsEnabled = false;
        _copyActions.Visibility = _status.Visibility = _retry.Visibility = _cancel.Visibility = Visibility.Collapsed;
        ClearCopyStatus();
        StateChanged?.Invoke();
    }

    internal Task OpenTranslationAsync() => _operations.RunAsync(OpenTranslationCoreAsync);

    private async Task OpenTranslationCoreAsync()
    {
        if (_disposed || _opening || string.IsNullOrWhiteSpace(_lastText)) return;
        var revision = _revision;
        var uri = ComposerDestinations.ExternalUri(Destination, _lastText, targetLanguage: TargetLanguage)!;
        _opening = true;
        _browser.IsEnabled = false;
        try
        {
            if (!await _open(uri) && !_disposed && revision == _revision)
                ShowStatus(UiText.Get("CouldNotOpenTheBrowserTryAgain"));
        }
        catch (Exception exception)
        {
            if (!_disposed && revision == _revision)
            {
                _reportError(exception);
                ShowStatus(UiText.Get("CouldNotOpenTheBrowserTryAgain"));
            }
        }
        finally
        {
            _opening = false;
            if (!_disposed) _browser.IsEnabled = _lastText.Length > 0;
        }
    }

    internal bool CopyResult()
    {
        if (_disposed || !_copy.IsEnabled) return false;
        try
        {
            var copied = _copyText(_result.Text);
            ShowCopyStatus(copied ? UiText.Get("Copied") : UiText.Get("CouldNotCopySelectTheTranslationAndCopy"));
            return copied;
        }
        catch (Exception exception)
        {
            _reportError(exception);
            ShowCopyStatus(UiText.Get("CouldNotCopySelectTheTranslationAndCopy"));
            return false;
        }
    }

    private void ShowStatus(string text) { UiLanguage.Message(_status, text); _status.Visibility = Visibility.Visible; }
    private void ShowCopyStatus(string text) { _copyStatus.Text = text; _copyStatus.Visibility = Visibility.Visible; }
    private void ClearCopyStatus() { _copyStatus.Text = ""; _copyStatus.Visibility = Visibility.Collapsed; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StateChanged = null;
        ClearRequested = null;
        _ = _operations.StopAsync();
    }

    public Task StopAsync()
    {
        Dispose();
        return _operations.StopAsync();
    }
}
