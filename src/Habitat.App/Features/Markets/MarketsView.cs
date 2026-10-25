using Habitat.Models;
using Habitat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Habitat.Features.Markets;

internal sealed class MarketsView : Grid
{
    private readonly StackPanel _favorites = new() { Spacing = 4 };
    private readonly Dictionary<string, Button> _rows = new();
    private readonly TextBlock _name = new() { FontSize = 19, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _price = new() { FontSize = 26, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _change = new() { FontSize = 14 };
    private readonly TextBlock _status = new() { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _range = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Canvas _chart = new() { Width = 640, Height = 140 };
    private readonly StackPanel _news = new() { Spacing = 10 };
    internal Button Explain { get; } = new();
    internal Button Refresh { get; } = new();
    internal Button Manage { get; } = new();
    private readonly Button _source = new();
    public string? SelectedSymbol { get; private set; }
    public event Action<string>? Selected;
    public event Action<Uri>? OpenLink;

    public MarketsView(MarketsSettings settings, string? selected)
    {
        var body = new StackPanel { Spacing = 12, Padding = new Thickness(0, 0, 12, 8) };
        Children.Add(ScrollEdgeFade.Enable(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }));
        SelectedSymbol = settings.Symbols.Contains(selected) ? selected : settings.Symbols.FirstOrDefault();
        body.Children.Add(_favorites);
        foreach (var symbol in settings.Symbols)
        {
            var row = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8), Content = symbol };
            row.Click += (_, _) => { SelectedSymbol = symbol; Selected?.Invoke(symbol); };
            _rows.Add(symbol, row);
            _favorites.Children.Add(row);
        }
        if (_rows.Count == 0) body.Children.Add(new TextBlock { Text = UiText.Get("MarketsEmpty"), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(_name);
        body.Children.Add(_price);
        body.Children.Add(_change);
        body.Children.Add(_status);
        body.Children.Add(new Viewbox { Child = _chart, Height = 140, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Stretch });
        body.Children.Add(_range);
        _source.Content = UiText.Get("MarketsOpenSource");
        _source.HorizontalAlignment = HorizontalAlignment.Left;
        _source.Click += (_, _) => { if (SelectedSymbol is { } symbol) OpenLink?.Invoke(new Uri("https://finance.yahoo.com/quote/" + Uri.EscapeDataString(symbol) + "/")); };
        body.Children.Add(_source);
        Explain.Content = UiText.Get("MarketsExplain");
        Explain.HorizontalAlignment = HorizontalAlignment.Left;
        body.Children.Add(Explain);
        body.Children.Add(new TextBlock { Text = UiText.Get("MarketsNews"), FontSize = 16 });
        body.Children.Add(_news);
        Refresh.Content = UiText.Get("MarketsRefresh");
        Manage.Content = UiText.Get("MarketsManage");
        var actions = new StackPanel { Spacing = 8 };
        actions.Children.Add(Refresh);
        actions.Children.Add(Manage);
        body.Children.Add(actions);
        body.Children.Add(new TextBlock { Text = UiText.Get("MarketsSourceNote"), TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 12 });
    }

    public void Render(MarketsSession session, bool canExplain)
    {
        foreach (var (symbol, row) in _rows)
        {
            var text = session.Quotes.TryGetValue(symbol, out var item)
                ? symbol + "   " + MarketPresentation.Price(item) + "   " + MarketPresentation.Change(item.DayChangePercent) + (session.Failed(symbol) ? " · " + UiText.Get("MarketsStale") : "")
                : symbol + " · " + UiText.Get(session.IsRefreshing ? "MarketsLoading" : "MarketsUnavailable");
            row.Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            row.FontWeight = symbol == SelectedSymbol ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            AutomationProperties.SetName(row, text);
            AutomationProperties.SetItemStatus(row, UiText.Get(symbol == SelectedSymbol ? "Selected" : "NotSelected"));
        }
        var quote = SelectedSymbol is { } selected ? session.Quotes.GetValueOrDefault(selected) : null;
        _name.Text = quote?.Name ?? SelectedSymbol ?? UiText.Get("Markets");
        _price.Text = quote is null ? "—" : MarketPresentation.Price(quote);
        _change.Text = UiText.Format("MarketsDailyChange", MarketPresentation.Change(quote?.DayChangePercent));
        _status.Text = quote is not null ? MarketPresentation.Timestamp(quote) + (session.Failed(quote.Symbol) ? " · " + UiText.Get("MarketsStale") : "")
            : UiText.Get(session.IsRefreshing ? "MarketsLoading" : "MarketsUnavailable");
        Explain.IsEnabled = quote is not null && canExplain;
        ToolTipService.SetToolTip(Explain, UiText.Get(canExplain ? "MarketsExplainHelp" : "MarketsEnableChat"));
        _source.IsEnabled = SelectedSymbol is not null;
        Refresh.IsEnabled = !session.IsRefreshing && _rows.Count > 0;
        DrawChart(quote);
    }

    private void DrawChart(MarketSnapshot? quote)
    {
        _chart.Children.Clear();
        if (quote is null || quote.History.Count < 2)
        {
            _range.Text = UiText.Get("MarketsNoHistory");
            AutomationProperties.SetName(_chart, _range.Text);
            return;
        }
        var points = quote.History;
        var low = points.Min(point => point.Close);
        var high = points.Max(point => point.Close);
        var span = high - low;
        var line = new Polyline { StrokeThickness = 2, Stroke = new SolidColorBrush(ActualTheme == ElementTheme.Dark
            ? Windows.UI.Color.FromArgb(255, 160, 172, 255) : Windows.UI.Color.FromArgb(255, 77, 90, 205)) };
        var elapsed = (points[^1].Time - points[0].Time).TotalSeconds;
        foreach (var point in points)
            line.Points.Add(new Windows.Foundation.Point(8 + (point.Time - points[0].Time).TotalSeconds / elapsed * 624,
                span == 0 ? 70 : 132 - (double)((point.Close - low) / span) * 124));
        _chart.Children.Add(line);
        _range.Text = UiText.Format("MarketsChartRange", points[0].Time.ToString("d", UiText.Culture), points[^1].Time.ToString("d", UiText.Culture),
            low.ToString("N2", UiText.Culture), high.ToString("N2", UiText.Culture), quote.Currency);
        AutomationProperties.SetName(_chart, _range.Text);
    }

    public void RenderNews(IReadOnlyList<MarketNews>? news, bool failed = false)
    {
        _news.Children.Clear();
        if (news is null || news.Count == 0)
        {
            _news.Children.Add(new TextBlock { Text = UiText.Get(failed ? "MarketsNewsUnavailable" : news is null ? "MarketsNewsLoading" : "MarketsNoNews"), TextWrapping = TextWrapping.Wrap });
            return;
        }
        foreach (var item in news)
        {
            var body = new StackPanel { Spacing = 3 };
            body.Children.Add(new TextBlock { Text = item.Title, TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = item.Source + " · " + item.PublishedAt.ToLocalTime().ToString("g", UiText.Culture), FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
            var link = new Button { Content = body, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8) };
            AutomationProperties.SetName(link, UiText.Format("MarketsReadNews", item.Title, item.Source));
            link.Click += (_, _) => OpenLink?.Invoke(item.Link);
            _news.Children.Add(link);
        }
    }

    public void LinkFailed() => _status.Text = UiText.Get("MarketsLinkFailed");
}
