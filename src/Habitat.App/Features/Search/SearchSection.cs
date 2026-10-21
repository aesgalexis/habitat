using Habitat.Features.Destinations;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Habitat.Features.Search;

internal sealed class SearchSection : IDestinationSection
{
    private readonly Grid _view = new() { RowSpacing = 10 };
    private readonly TextBlock _query = new() { FontSize = 20, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _status = new() { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _results = new() { Spacing = 22 };
    private readonly Button _retry = UiLanguage.Watch(new Button() { Content = UiText.Get("Retry"), Visibility = Visibility.Collapsed }, ("Content", () => (object?)(UiText.Get("Retry"))));
    private readonly Button _cancel = UiLanguage.Watch(new Button() { Content = UiText.Get("Cancel"), Visibility = Visibility.Collapsed }, ("Content", () => (object?)(UiText.Get("Cancel"))));
    private readonly Button _previous = UiLanguage.Watch(new Button() { Content = UiText.Get("PreviousPage"), IsEnabled = false }, ("Content", () => (object?)(UiText.Get("PreviousPage"))));
    private readonly Button _next = UiLanguage.Watch(new Button() { Content = UiText.Get("NextPage"), IsEnabled = false }, ("Content", () => (object?)(UiText.Get("NextPage"))));
    private readonly Button _clear = UiLanguage.Watch(new Button() { Content = UiText.Get("ClearSearch"), VerticalAlignment = VerticalAlignment.Top }, ("Content", () => (object?)(UiText.Get("ClearSearch"))));
    private readonly Func<string, int, CancellationToken, Task<SearchPage>> _search;
    private readonly Func<Uri, Task<bool>> _open;
    private readonly Action<Exception> _reportError;
    private readonly AsyncOperationScope _operations = new();
    private readonly Func<Uri, ImageSource?>? _thumbnailSource;
    private readonly List<int> _pages = [];
    private readonly ScrollViewer _scroll;
    private CancellationTokenSource? _request;
    private string _lastQuery = "";
    private int _requestedStart;
    private int? _nextStart;
    private int _revision;
    private bool _userCanceled, _disposed;
    public string Destination { get; }
    private string Provider => Destination == ComposerDestination.YouTube ? "YouTube" : "Google";
    public FrameworkElement View => _view;
    public bool IsBusy => _request is not null;
    public bool HasContent { get; private set; }
    public event Action? StateChanged;
    internal event Action? ClearRequested;
    internal IReadOnlyList<SearchResult> Results { get; private set; } = [];
    internal string StatusText => _status.Text;
    internal bool CanGoNext => _next.IsEnabled;
    internal bool CanGoPrevious => _previous.IsEnabled;

    public SearchSection(Func<string, int, CancellationToken, Task<SearchPage>> search,
        Func<Uri, Task<bool>> open, Action<Exception> reportError, string destination = ComposerDestination.Google,
        Func<Uri, ImageSource?>? thumbnailSource = null)
    {
        Destination = destination;
        _search = search;
        _open = open;
        _reportError = reportError;
        _thumbnailSource = thumbnailSource;
        _view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid { ColumnSpacing = 12 };
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(_query);
        var searchActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        var browser = UiLanguage.Watch(new Button { Content = UiText.Get("OpenInBrowser") }, ("Content", () => (object?)(UiText.Get("OpenInBrowser"))));
        browser.Click += async (_, _) => await OpenSearchAsync();
        searchActions.Children.Add(browser);
        searchActions.Children.Add(_clear);
        Grid.SetColumn(searchActions, 1);
        header.Children.Add(searchActions);
        header.SizeChanged += (_, args) =>
        {
            var stacked = args.NewSize.Width < searchActions.DesiredSize.Width + 180;
            header.RowSpacing = stacked ? 8 : 0;
            Grid.SetColumnSpan(_query, stacked ? 2 : 1);
            Grid.SetRow(searchActions, stacked ? 1 : 0);
            Grid.SetColumn(searchActions, stacked ? 0 : 1);
            Grid.SetColumnSpan(searchActions, stacked ? 2 : 1);
        };
        _view.Children.Add(header);
        _clear.Click += (_, _) => RequestClear();
        UiLanguage.SetName(_clear, () => UiText.Get("ClearSearch"));
        UiLanguage.SetToolTip(_clear, () => UiText.Get("ClearSearchAndCloseItsBrowserTab"));
        var status = new Grid { ColumnSpacing = 12 };
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(_retry);
        actions.Children.Add(_cancel);
        Grid.SetColumn(actions, 1);
        status.Children.Add(actions);
        Grid.SetRow(status, 1);
        _view.Children.Add(status);
        _scroll = new ScrollViewer { Content = _results, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 8, 12, 8) };
        Grid.SetRow(_scroll, 2);
        _view.Children.Add(_scroll);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        navigation.Children.Add(_previous);
        navigation.Children.Add(_next);
        Grid.SetRow(navigation, 3);
        _view.Children.Add(navigation);
        _retry.Click += async (_, _) => await RetryAsync();
        _cancel.Click += (_, _) => Cancel();
        _previous.Click += async (_, _) => await PreviousAsync();
        _next.Click += async (_, _) => await NextAsync();
        AutomationProperties.SetName(_query, Provider + " search query");
        AutomationProperties.SetName(_results, Provider + " search results");
        AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
    }

    public Task SubmitAsync(string text)
    {
        if (_disposed || IsBusy || string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        _pages.Clear();
        return LoadAsync(text, 0);
    }

    private Task LoadAsync(string query, int start) => _operations.RunAsync(() => LoadCoreAsync(query, start));

    private async Task LoadCoreAsync(string query, int start)
    {
        if (_disposed || IsBusy) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_operations.Token);
        request.CancelAfter(TimeSpan.FromSeconds(60));
        _request = request;
        var revision = ++_revision;
        HasContent = true;
        _userCanceled = false;
        _lastQuery = query;
        _requestedStart = start;
        _query.Text = query;
        _results.Children.Clear();
        Results = [];
        _nextStart = null;
        _retry.Visibility = Visibility.Collapsed;
        _cancel.Visibility = Visibility.Visible;
        UpdateNavigation();
        _status.Text = UiLanguage.Value(_status, "Text", () => UiText.Format("Searching", Provider));
        StateChanged?.Invoke();
        try
        {
            var provider = _operations.RunAsync(() => _search(query, start, request.Token), default(SearchPage)!);
            var page = await provider.WaitAsync(request.Token);
            if (_disposed || revision != _revision) return;
            request.Token.ThrowIfCancellationRequested();
            Results = page.Results;
            _nextStart = page.NextStart;
            var pageIndex = _pages.IndexOf(start);
            if (pageIndex >= 0) _pages.RemoveRange(pageIndex, _pages.Count - pageIndex);
            _pages.Add(start);
            foreach (var item in page.Results) AddResult(item);
            _status.Text = UiLanguage.Value(_status, "Text", () => page.Results.Count == 0 ? UiText.Get("NoResultsFoundTryAnotherSearch")
                : page.Results.Count == 1 ? UiText.Get("OneSearchResult") : UiText.Format("ResultsOnThisPage", page.Results.Count));
            _scroll.ChangeView(null, 0, null, true);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            if (!_disposed && revision == _revision)
            {
                _status.Text = UiLanguage.Value(_status, "Text", () => _userCanceled ? UiText.Get("SearchCanceled") : UiText.Get("SearchTimedOutTryAgain"));
                _retry.Visibility = Visibility.Visible;
            }
        }
        catch (Exception exception)
        {
            if (!_disposed && revision == _revision)
            {
                _reportError(exception);
                _status.Text = BrowserSearchService.ErrorMessage(exception, Destination);
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
                    UpdateNavigation();
                    StateChanged?.Invoke();
                }
            }
        }
    }

    private void AddResult(SearchResult result)
    {
        var row = new StackPanel { Spacing = 4 };
        var isChannel = Destination == ComposerDestination.YouTube && BrowserSearchService.IsChannelLink(result.Url);
        if (isChannel) row.Children.Add(UiLanguage.Watch(new TextBlock { Text = UiText.Get("Channel"), FontSize = 12, Opacity = 0.8 }, ("Text", () => (object?)(UiText.Get("Channel")))));
        var address = new TextBlock { Text = result.Url, FontSize = 12, Opacity = 0.7,
            TextTrimming = TextTrimming.CharacterEllipsis, IsTextSelectionEnabled = true };
        row.Children.Add(address);
        var title = new HyperlinkButton { Content = new TextBlock { Text = result.Title, TextWrapping = TextWrapping.Wrap, FontSize = 18 },
            Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(title, result.Title);
        UiLanguage.SetHelpText(title, () => (isChannel ? UiText.Get("OpenChannelInYourBrowser") : UiText.Get("OpenResultInYourBrowser")) + result.Url);
        title.Click += async (_, _) => await OpenResultAsync(result);
        row.Children.Add(title);
        if (result.Snippet.Length > 0) row.Children.Add(new TextBlock { Text = result.Snippet, FontSize = 14,
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        if (Destination == ComposerDestination.YouTube && BrowserSearchService.ThumbnailUri(result.Url) is { } thumbnailUri)
        {
            var video = new Grid { ColumnSpacing = 14 };
            video.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            video.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var thumbnail = CreateThumbnail(result, thumbnailUri);
            video.Children.Add(thumbnail);
            Grid.SetColumn(row, 1);
            video.Children.Add(row);
            video.SizeChanged += (_, _) =>
            {
                thumbnail.Width = Math.Clamp(video.ActualWidth * 0.34, 96, 160);
                thumbnail.Height = thumbnail.Width * 9 / 16;
            };
            _results.Children.Add(video);
        }
        else _results.Children.Add(row);
    }

    private Button CreateThumbnail(SearchResult result, Uri uri)
    {
        var image = new Image { Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
        var placeholder = new FontIcon { Glyph = "\uE714", FontSize = 28, Opacity = 0.35 };
        var content = new Grid();
        content.Children.Add(placeholder);
        content.Children.Add(image);
        var button = new Button
        {
            Content = content, Width = 160, Height = 90, Padding = new Thickness(0), CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch
        };
        UiLanguage.SetName(button, () => UiText.Get("OpenVideo") + result.Title);
        UiLanguage.SetHelpText(button, () => UiText.Get("OpenVideoInYourBrowser"));
        AutomationProperties.SetAccessibilityView(image, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(placeholder, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        button.Click += async (_, _) => await OpenResultAsync(result);
        var requested = false;
        button.EffectiveViewportChanged += (_, args) =>
        {
            var viewport = args.EffectiveViewport;
            if (_disposed || requested || _thumbnailSource is null || viewport.Width <= 0 || viewport.Height <= 0 ||
                viewport.Right <= 0 || viewport.Bottom <= 0 || viewport.X >= button.ActualWidth || viewport.Y >= button.ActualHeight) return;
            requested = true;
            try { image.Source = _thumbnailSource(uri); }
            catch (Exception exception) { _reportError(exception); }
        };
        image.ImageOpened += (_, _) => placeholder.Visibility = Visibility.Collapsed;
        image.ImageFailed += (_, _) => { image.Source = null; placeholder.Visibility = Visibility.Visible; };
        button.Unloaded += (_, _) =>
        {
            image.Source = null;
            placeholder.Visibility = Visibility.Visible;
            requested = false;
        };
        return button;
    }

    internal Task RetryAsync() => LoadAsync(_lastQuery, _requestedStart);
    internal Task NextAsync() => _next.IsEnabled && _nextStart is { } start ? LoadAsync(_lastQuery, start) : Task.CompletedTask;
    internal Task PreviousAsync() => _previous.IsEnabled ? LoadAsync(_lastQuery, _pages.Last(page => page < _requestedStart)) : Task.CompletedTask;
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
        _lastQuery = "";
        _query.Text = "";
        _requestedStart = 0;
        _nextStart = null;
        _pages.Clear();
        Results = [];
        _results.Children.Clear();
        _status.Text = "";
        _retry.Visibility = _cancel.Visibility = Visibility.Collapsed;
        UpdateNavigation();
        StateChanged?.Invoke();
    }
    internal Task OpenSearchAsync() => OpenAsync(BrowserSearchService.PageUri(Destination, _lastQuery, _requestedStart));
    internal Task OpenResultAsync(SearchResult result) => BrowserSearchService.IsSafeLink(result.Url)
        ? OpenAsync(new Uri(result.Url)) : Task.CompletedTask;

    private Task OpenAsync(Uri uri) => _operations.RunAsync(() => OpenCoreAsync(uri));

    private async Task OpenCoreAsync(Uri uri)
    {
        if (_disposed) return;
        try
        {
            if (!await _open(uri) && !_disposed) _status.Text = UiLanguage.Value(_status, "Text", () => UiText.Get("CouldNotOpenTheBrowserTryAgain"));
        }
        catch (Exception exception)
        {
            if (_disposed) return;
            _reportError(exception);
            _status.Text = UiLanguage.Value(_status, "Text", () => UiText.Get("CouldNotOpenTheBrowserTryAgain"));
        }
    }

    private void UpdateNavigation()
    {
        _next.IsEnabled = !IsBusy && _nextStart is not null;
        _previous.IsEnabled = !IsBusy && _pages.Any(page => page < _requestedStart);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _revision++;
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
