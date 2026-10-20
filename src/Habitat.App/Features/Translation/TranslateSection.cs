using Habitat.Features.Destinations;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Habitat.Features.Translation;

internal sealed class TranslateSection : IDestinationSection
{
    private readonly Grid _view = new() { RowSpacing = 12 };
    private readonly ComboBox _target = new() { Header = "Translate to", DisplayMemberPath = "Name", HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 0, MaxWidth = 220 };
    private readonly TextBlock _result = new() { FontSize = 15, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly TextBlock _status = new() { Text = "Write text below and press Translate.", FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
    private readonly Button _copy = new() { Content = new FontIcon { Glyph = "\uE8C8", FontSize = 16 }, IsEnabled = false,
        HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Grid _copyActions = new() { ColumnSpacing = 8, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _copyStatus = new() { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly Button _browser = new() { Content = "Open in browser", IsEnabled = false };
    private readonly Button _clear = new() { Content = "Clear translation" };
    private readonly Button _retry = new() { Content = "Retry", Visibility = Visibility.Collapsed };
    private readonly Button _cancel = new() { Content = "Cancel", Visibility = Visibility.Collapsed };
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
            if (_disposed) return;
            _revision++;
            _request?.Cancel();
            _result.Text = "";
            _copy.IsEnabled = false;
            _copyActions.Visibility = Visibility.Collapsed;
            ClearCopyStatus();
            ShowStatus("Press Translate to use the selected language.");
            _retry.Visibility = Visibility.Collapsed;
        };
        AutomationProperties.SetName(_target, "Translate to");
        AutomationProperties.SetName(_result, "Translated text");
        AutomationProperties.SetName(_copy, "Copy translation");
        ToolTipService.SetToolTip(_copy, "Copy translation");
        AutomationProperties.SetName(_clear, "Clear translation");
        ToolTipService.SetToolTip(_clear, "Clear the source text and translation");
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
        ShowStatus("Translating…");
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
                ShowStatus(_userCanceled ? "Translation canceled." : "Translation timed out. Try again.");
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
                ShowStatus("Could not open the browser. Try again.");
        }
        catch (Exception exception)
        {
            if (!_disposed && revision == _revision)
            {
                _reportError(exception);
                ShowStatus("Could not open the browser. Try again.");
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
            ShowCopyStatus(copied ? "Copied." : "Could not copy. Select the translation and copy it manually.");
            return copied;
        }
        catch (Exception exception)
        {
            _reportError(exception);
            ShowCopyStatus("Could not copy. Select the translation and copy it manually.");
            return false;
        }
    }

    private void ShowStatus(string text) { _status.Text = text; _status.Visibility = Visibility.Visible; }
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
