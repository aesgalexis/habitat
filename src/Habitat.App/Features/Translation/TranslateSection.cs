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
    private readonly Button _copy = new() { Content = "Copy", IsEnabled = false, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly Button _retry = new() { Content = "Retry", Visibility = Visibility.Collapsed };
    private readonly Button _cancel = new() { Content = "Cancel", Visibility = Visibility.Collapsed };
    private readonly Func<string, string, CancellationToken, Task<TranslationResult>> _translate;
    private readonly Func<string, bool> _copyText;
    private readonly Action<Exception> _reportError;
    private CancellationTokenSource? _request;
    private string _lastText = "";
    private int _revision;
    private bool _userCanceled;
    private bool _disposed;
    public ComposerDestination Destination => ComposerDestination.Translate;
    public FrameworkElement View => _view;
    public bool IsBusy => _request is not null;
    public bool HasContent { get; private set; }
    public event Action? StateChanged;
    internal string ResultText => _result.Text;
    internal string StatusText => _status.Text;
    internal bool CanCopy => _copy.IsEnabled;
    internal string TargetLanguage => ((TranslationLanguage)_target.SelectedItem).Code;

    public TranslateSection(Func<string, string, CancellationToken, Task<TranslationResult>> translate,
        Func<string, bool> copyText, Action<Exception> reportError)
    {
        _translate = translate;
        _copyText = copyText;
        _reportError = reportError;
        _target.ItemsSource = TranslationLanguages.All;
        _target.SelectedItem = TranslationLanguages.All.First(language => language.Code == TranslationLanguages.DefaultCode);
        _target.SelectionChanged += (_, _) =>
        {
            if (_disposed) return;
            _revision++;
            _request?.Cancel();
            _result.Text = "";
            _copy.IsEnabled = false;
            ShowStatus("Press Translate to use the selected language.");
            _retry.Visibility = Visibility.Collapsed;
        };
        AutomationProperties.SetName(_target, "Translate to");
        AutomationProperties.SetName(_result, "Translated text");
        AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        for (var row = 0; row < 3; row++) _view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var toolbar = new Grid { ColumnSpacing = 12 };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.Children.Add(_target);
        Grid.SetColumn(_copy, 1);
        toolbar.Children.Add(_copy);
        _view.Children.Add(toolbar);
        Grid.SetRow(_status, 1);
        _view.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(_retry);
        actions.Children.Add(_cancel);
        Grid.SetRow(actions, 2);
        _view.Children.Add(actions);
        var scroll = new ScrollViewer { Content = _result, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 3);
        _view.Children.Add(scroll);
        _copy.Click += (_, _) => CopyResult();
        _cancel.Click += (_, _) => Cancel();
        _retry.Click += async (_, _) => await RetryAsync();
    }

    public async Task SubmitAsync(string text)
    {
        if (_disposed || IsBusy || string.IsNullOrWhiteSpace(text)) return;
        using var request = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _request = request;
        HasContent = true;
        _userCanceled = false;
        var revision = ++_revision;
        var targetLanguage = TargetLanguage;
        _lastText = text;
        _result.Text = "";
        _copy.IsEnabled = false;
        _retry.Visibility = Visibility.Collapsed;
        _cancel.Visibility = Visibility.Visible;
        ShowStatus("Translating…");
        StateChanged?.Invoke();
        try
        {
            var result = await _translate(text, targetLanguage, request.Token).WaitAsync(request.Token);
            if (_disposed || revision != _revision) return;
            request.Token.ThrowIfCancellationRequested();
            _result.Text = result.Text;
            _copy.IsEnabled = result.Text.Length > 0;
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
            _request = null;
            if (!_disposed)
            {
                _cancel.Visibility = Visibility.Collapsed;
                StateChanged?.Invoke();
            }
        }
    }

    internal void SelectTarget(string code) => _target.SelectedItem = TranslationLanguages.All.First(language => language.Code == code);
    internal Task RetryAsync() => SubmitAsync(_lastText);
    internal void Cancel() { _userCanceled = true; _request?.Cancel(); }

    internal bool CopyResult()
    {
        if (_disposed || !_copy.IsEnabled) return false;
        try
        {
            var copied = _copyText(_result.Text);
            ShowStatus(copied ? "Copied." : "Could not copy. Select the translation and copy it manually.");
            return copied;
        }
        catch (Exception exception)
        {
            _reportError(exception);
            ShowStatus("Could not copy. Select the translation and copy it manually.");
            return false;
        }
    }

    private void ShowStatus(string text) { _status.Text = text; _status.Visibility = Visibility.Visible; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _request?.Cancel();
        StateChanged = null;
    }
}
