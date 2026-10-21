using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Habitat.Models;

namespace Habitat.Services;

internal sealed class MarketsService : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public MarketsService(HttpClient? http = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 2_000_000 };
    }

    public async Task<MarketSnapshot> GetQuoteAsync(string symbol, CancellationToken token)
    {
        symbol = MarketsSettings.NormalizeSymbol(symbol) ?? throw new ArgumentException("Invalid market symbol.", nameof(symbol));
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://query1.finance.yahoo.com/v8/finance/chart/" + Uri.EscapeDataString(symbol) + "?interval=1d&range=1mo");
        request.Headers.UserAgent.ParseAdd("HABITAT/1.0");
        using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
        return ParseQuote(json.RootElement, symbol);
    }

    internal static MarketSnapshot ParseQuote(JsonElement root, string symbol)
    {
        try
        {
            var chart = root.GetProperty("chart");
            if (chart.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                throw new InvalidDataException("Quote unavailable.");
            var result = chart.GetProperty("result")[0];
            var meta = result.GetProperty("meta");
            var returnedSymbol = MarketsSettings.NormalizeSymbol(meta.GetProperty("symbol").GetString());
            if (returnedSymbol != symbol) throw new InvalidDataException("Unexpected quote symbol.");
            var price = Number(meta, "regularMarketPrice") ?? throw new InvalidDataException("Missing quote price.");
            var time = DateTimeOffset.FromUnixTimeSeconds(meta.GetProperty("regularMarketTime").GetInt64());
            var currency = Text(meta, "currency", 12) ?? throw new InvalidDataException("Missing quote currency.");
            var name = Text(meta, "longName", 120) ?? Text(meta, "shortName", 120) ?? symbol;
            var points = new List<MarketPoint>();
            if (result.TryGetProperty("timestamp", out var times) && times.ValueKind == JsonValueKind.Array &&
                result.TryGetProperty("indicators", out var indicators) && indicators.TryGetProperty("quote", out var quotes) &&
                quotes.ValueKind == JsonValueKind.Array && quotes.GetArrayLength() > 0 &&
                quotes[0].TryGetProperty("close", out var closes) && closes.ValueKind == JsonValueKind.Array)
            {
                for (var i = 0; i < Math.Min(100, Math.Min(times.GetArrayLength(), closes.GetArrayLength())); i++)
                    if (times[i].ValueKind == JsonValueKind.Number && times[i].TryGetInt64(out var stamp) && closes[i].ValueKind == JsonValueKind.Number &&
                        closes[i].TryGetDecimal(out var close) && close >= 0 && close <= 1_000_000_000_000m)
                    {
                        var pointTime = DateTimeOffset.FromUnixTimeSeconds(stamp);
                        if (pointTime <= time) points.Add(new(pointTime, close));
                    }
            }
            var history = points.OrderBy(point => point.Time).DistinctBy(point => point.Time).ToArray();
            decimal? change = SignedNumber(meta, "regularMarketChangePercent");
            if (change is null)
            {
                var previous = Number(meta, "previousClose");
                // chartPreviousClose is the start of the requested MONTH, not yesterday.
                // Use the last daily close before the quote's exchange-local date instead.
                if (previous is null)
                {
                    var offset = meta.TryGetProperty("gmtoffset", out var zone) && zone.TryGetInt32(out var seconds)
                        ? Math.Clamp(seconds, -50_400, 50_400) : 0;
                    var quoteDate = time.AddSeconds(offset).Date;
                    previous = history.LastOrDefault(point => point.Time.AddSeconds(offset).Date < quoteDate)?.Close;
                }
                if (previous > 0) change = (price - previous.Value) / previous.Value * 100;
            }
            return new(symbol, name, currency, price, change, time, history);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or
            ArgumentException or IndexOutOfRangeException or FormatException or OverflowException)
        {
            throw new InvalidDataException("Invalid quote response.", exception);
        }
    }

    public async Task<IReadOnlyList<MarketNews>> GetNewsAsync(string symbol, string language, CancellationToken token)
    {
        symbol = MarketsSettings.NormalizeSymbol(symbol) ?? throw new ArgumentException("Invalid market symbol.", nameof(symbol));
        var locale = language == "es" ? "&region=ES&lang=es-ES" : "&region=US&lang=en-US";
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://feeds.finance.yahoo.com/rss/2.0/headline?s=" + Uri.EscapeDataString(symbol) + locale);
        request.Headers.UserAgent.ParseAdd("HABITAT/1.0");
        using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        return ParseNews(xml);
    }

    internal static IReadOnlyList<MarketNews> ParseNews(string xml)
    {
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_000_000
        });
        var document = XDocument.Load(reader);
        var items = new List<MarketNews>();
        foreach (var item in document.Descendants("item").Take(30))
        {
            var title = WebUtility.HtmlDecode(item.Element("title")?.Value ?? "").Trim();
            if (title.Length == 0 || !Uri.TryCreate(item.Element("link")?.Value, UriKind.Absolute, out var link) ||
                link.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(link.UserInfo) || link.IsLoopback ||
                !DateTimeOffset.TryParse(item.Element("pubDate")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            items.Add(new(title[..Math.Min(240, title.Length)], link, date, link.Host));
        }
        return items.DistinctBy(item => item.Link).OrderByDescending(item => item.PublishedAt).Take(3).ToArray();
    }

    private static string? Text(JsonElement value, string key, int max) =>
        value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String &&
        property.GetString() is { Length: > 0 } text ? text[..Math.Min(max, text.Length)] : null;
    private static decimal? SignedNumber(JsonElement value, string key) =>
        value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.Number &&
        property.TryGetDecimal(out var number) && number is >= -1_000_000_000_000m and <= 1_000_000_000_000m ? number : null;
    private static decimal? Number(JsonElement value, string key) => SignedNumber(value, key) is >= 0 and var number ? number : null;
    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
