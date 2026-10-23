using Habitat.Features.Markets;
using Habitat.Models;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Habitat;

public sealed partial class MainPage
{
    private MarketsSession _marketsSession;
    private readonly Button MarketsButton = new();
    private readonly TextBlock MarketsCompactChange = new() { FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel MarketsDetails = new() { Spacing = 2 };
    private readonly TextBlock MarketsName = new() { FontSize = 11, Opacity = 0.72, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock MarketsPrice = new() { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock MarketsChange = new() { FontSize = 12, Opacity = 0.8, TextTrimming = TextTrimming.CharacterEllipsis };
    private bool _marketsHovered;
    private bool _marketsFocused;
    private bool _marketsPanelOpen;
    private MarketsView? _marketsView;
    private CancellationTokenSource? _marketNewsCancellation;
    private readonly Dictionary<(string Symbol, string Language), (DateTimeOffset At, IReadOnlyList<MarketNews> Items)> _marketNews = new();

    private void BuildMarketsInterface()
    {
        var content = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = new StackPanel { Spacing = 3 };
        icon.Children.Add(new FontIcon { Glyph = "\uE9D2", FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center });
        // The compact percentage fits the 40-DIP rail without moving its icon.
        icon.Children.Add(new Border { Height = 18, Child = new Viewbox { Child = MarketsCompactChange, MaxWidth = 32, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly } });
        content.Children.Add(icon);
        MarketsDetails.Children.Add(MarketsName);
        MarketsDetails.Children.Add(MarketsPrice);
        MarketsDetails.Children.Add(MarketsChange);
        Grid.SetColumn(MarketsDetails, 1);
        content.Children.Add(MarketsDetails);
        MarketsButton.Content = content;
        MarketsButton.Height = 78;
        MarketsButton.Padding = new Thickness(8);
        MarketsButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        MarketsButton.Click += async (_, _) => await RunOperationAsync(ShowMarketsAsync);
        MarketsButton.PointerEntered += (_, _) => _marketsHovered = true;
        MarketsButton.PointerExited += (_, _) => { _marketsHovered = false; _marketsSession.Tick(true); };
        MarketsButton.GotFocus += (_, _) => _marketsFocused = true;
        MarketsButton.LostFocus += (_, _) => { _marketsFocused = false; _marketsSession.Tick(true); };
        _widgets.Children.Add(MarketsButton);
        RenderMarkets();
    }

    private void TickMarkets()
    {
        if (_operations.IsStopping || !_sidebarCatalog.Enabled.Contains(SidebarCatalog.Markets)) return;
        _marketsSession.Tick(_marketsHovered || _marketsFocused || _marketsPanelOpen || HasAppearanceOverlay || _marketsSession.IsRefreshing);
        if (!_marketsSession.IsRefreshing && DateTimeOffset.UtcNow >= _marketsSession.NextRefresh)
            _ = RunOperationAsync(() => _marketsSession.RefreshAsync(_operations.Token));
    }

    private void RenderMarkets()
    {
        var symbol = _marketsSession.CurrentSymbol;
        var quote = symbol is null ? null : _marketsSession.Quotes.GetValueOrDefault(symbol);
        MarketsName.Text = quote?.Name ?? UiText.Get("Markets");
        MarketsPrice.Text = quote is not null ? MarketPresentation.Price(quote) : symbol ?? UiText.Get("MarketsManage");
        MarketsChange.Text = symbol is null ? UiText.Get("MarketsEmpty") :
            _marketsSession.Failed(symbol) ? UiText.Get(quote is null ? "MarketsUnavailable" : "MarketsStale") :
            quote is null ? UiText.Get("MarketsLoading") : MarketPresentation.Change(quote.DayChangePercent) + " · " + quote.QuotedAt.ToLocalTime().ToString("dd MMM HH:mm", UiText.Culture);
        MarketsCompactChange.Text = quote is null ? "—" : MarketPresentation.Change(quote.DayChangePercent, true);
        var accessible = UiText.Get("Markets") + ": " + MarketsName.Text + ", " + MarketsPrice.Text + ", " + MarketsChange.Text;
        if (quote is not null) accessible += ", " + MarketPresentation.Timestamp(quote);
        AutomationProperties.SetName(MarketsButton, accessible);
        ToolTipService.SetToolTip(MarketsButton, accessible);
        _marketsView?.Render(_marketsSession, MarketChatDestination is not null && !_composer.IsExclusiveBusy);
    }

    private void PaintMarkets()
    {
        var text = TextBrush();
        var transparent = new SolidColorBrush(Color(0, 0, 0, 0));
        UseStableWidgetButton(MarketsButton);
        MarketsButton.Foreground = text;
        MarketsButton.Background = transparent;
        MarketsButton.BorderBrush = transparent;
        foreach (var key in new[] { "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBackgroundDisabled" }) MarketsButton.Resources[key] = transparent;
        foreach (var key in new[] { "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed", "ButtonForegroundDisabled" }) MarketsButton.Resources[key] = text;
        foreach (var label in new[] { MarketsName, MarketsPrice, MarketsChange, MarketsCompactChange }) label.Foreground = text;
    }

    private string? MarketChatDestination => _destination is ComposerDestination.OpenAI or ComposerDestination.Codex && _destinationCatalog.Enabled.Contains(_destination)
        ? _destination : _destinationCatalog.Enabled.Contains(ComposerDestination.OpenAI) ? ComposerDestination.OpenAI :
        _destinationCatalog.Enabled.Contains(ComposerDestination.Codex) ? ComposerDestination.Codex : null;

    private async Task ShowMarketsAsync()
    {
        if (HasAppearanceOverlay || _activeDialog is not null || _operations.IsStopping) return;
        var view = new MarketsView(_marketsSession.Settings, _marketsSession.CurrentSymbol)
        {
            Width = Math.Max(220, Math.Min(620, XamlRoot.Size.Width - 96)),
            Height = Math.Max(160, Math.Min(600, XamlRoot.Size.Height - 200))
        };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = UiText.Get("Markets"), Content = view, CloseButtonText = UiText.Get("Close") };
        dialog.Resources["ContentDialogMaxWidth"] = 720d;
        StyleDialog(dialog);
        var manage = false;
        MarketSnapshot? explain = null;
        view.Manage.Click += (_, _) => { manage = true; dialog.Hide(); };
        view.Explain.Click += (_, _) =>
        {
            explain = view.SelectedSymbol is { } selected ? _marketsSession.Quotes.GetValueOrDefault(selected) : null;
            dialog.Hide();
        };
        view.Refresh.Click += async (_, _) => await RunOperationAsync(async () =>
        {
            await _marketsSession.RefreshAsync(_operations.Token, true);
            if (ReferenceEquals(_marketsView, view) && view.SelectedSymbol is { } symbol) await LoadMarketNewsAsync(symbol, true);
        });
        view.Selected += symbol =>
        {
            RenderMarkets();
            _ = RunOperationAsync(() => LoadMarketNewsAsync(symbol));
        };
        view.OpenLink += uri => _ = RunOperationAsync(async () =>
        {
            try { if (!await _services.LaunchBrowser(uri)) view.LinkFailed(); }
            catch (Exception exception) { _services.ReportError("Open market source", exception); view.LinkFailed(); }
        });
        _marketsView = view;
        _marketsPanelOpen = true;
        RenderMarkets();
        _ = RunOperationAsync(() => _marketsSession.RefreshAsync(_operations.Token));
        if (view.SelectedSymbol is { } initial) _ = RunOperationAsync(() => LoadMarketNewsAsync(initial));
        else view.RenderNews([]);
        try { await ShowDialogAsync(dialog); }
        finally
        {
            _marketNewsCancellation?.Cancel();
            _marketsPanelOpen = false;
            _marketsView = null;
            _marketsSession.Tick(true);
        }
        if (_operations.IsStopping) return;
        if (manage) await ShowPreferencesAsync("Markets");
        else if (explain is not null) PrepareMarketQuestion(explain);
        FocusComposer();
    }

    private void PrepareMarketQuestion(MarketSnapshot quote)
    {
        if (MarketChatDestination is not { } destination || _composer.IsExclusiveBusy) return;
        SelectDestination(destination);
        var question = MarketPresentation.Question(quote);
        // Keep any existing chat draft verbatim; only append the requested context.
        Composer.Text = Composer.Text.Length == 0 ? question : Composer.Text + Environment.NewLine + Environment.NewLine + question;
        Composer.SelectionStart = Composer.Text.Length;
        Composer.SelectionLength = 0;
        FocusComposer();
    }

    private async Task LoadMarketNewsAsync(string symbol, bool force = false)
    {
        _marketNewsCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_operations.Token);
        _marketNewsCancellation = cancellation;
        var view = _marketsView;
        if (view is null) { _marketNewsCancellation = null; return; }
        var key = (symbol, UiText.Language);
        try
        {
            if (!force && _marketNews.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromMinutes(15))
            { view.RenderNews(cached.Items); return; }
            view.RenderNews(null);
            var news = await _services.Markets.GetNewsAsync(symbol, key.Language, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_marketNews.Count >= 10) _marketNews.Clear();
            _marketNews[key] = (DateTimeOffset.UtcNow, news);
            if (ReferenceEquals(view, _marketsView) && view.SelectedSymbol == symbol) view.RenderNews(news);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is HttpRequestException or IOException or System.Xml.XmlException or OperationCanceledException)
        {
            if (ReferenceEquals(view, _marketsView) && view.SelectedSymbol == symbol) view.RenderNews([], true);
        }
        finally { if (ReferenceEquals(_marketNewsCancellation, cancellation)) _marketNewsCancellation = null; }
    }
}
