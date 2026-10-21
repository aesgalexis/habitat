using System.Net;
using System.Text.Json;
using Habitat;
using Habitat.Services;

internal static class MarketsChecks
{
    public static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var settings = new MarketsSettings { Symbols = [" aapl ", "AAPL", "^gspc", "EURUSD=X", "BTC-USD", "IBE.MC", "MSFT", "bad?url"], PinnedSymbol = "aapl" }.Normalize();
        check(settings.Symbols.SequenceEqual(["AAPL", "^GSPC", "EURUSD=X", "BTC-USD", "IBE.MC"]) && settings.PinnedSymbol == "AAPL", "Market favorites normalize stable symbols, deduplicate and enforce their bound.");
        check((settings with { PinnedSymbol = "GOOG" }).Normalize().PinnedSymbol is null && MarketsSettings.NormalizeSymbol("https://example.com") is null,
            "A removed pin returns to rotation and URLs cannot become symbols.");
        check(new MarketsSettings { Symbols = [] }.Normalize().Symbols.Length == 0, "An intentionally empty watchlist stays empty.");
        check(new MarketsSettings { Symbols = null! }.Normalize().Symbols.Length == 4, "Null legacy favorites restore defaults.");
        var file = Path.Combine(directory, "markets-preferences.json");
        check((new Preferences("night", 20) { Markets = settings }).Save(file) && Preferences.Load(file).Markets.Symbols.SequenceEqual(settings.Symbols), "Market favorites persist in isolated preferences.");
        File.WriteAllText(file, """{"Theme":"night","Opacity":20,"Markets":null}""");
        check(Preferences.Load(file).Markets.Symbols.Length == 4, "Null market preferences migrate safely.");

        using var document = JsonDocument.Parse(Chart("AAPL"));
        var quote = MarketsService.ParseQuote(document.RootElement, "AAPL");
        check(quote.Price == 105 && quote.DayChangePercent == 5 && quote.History.Count == 3,
            "Daily change uses the previous trading day's close, never chartPreviousClose from the start of the month.");
        using var percent = JsonDocument.Parse(Chart("AAPL", -2.5m));
        check(MarketsService.ParseQuote(percent.RootElement, "AAPL").DayChangePercent == -2.5m, "Provider daily change takes precedence over inferred history.");
        using var missing = JsonDocument.Parse(Chart("AAPL", null, history: false));
        var noHistory = MarketsService.ParseQuote(missing.RootElement, "AAPL");
        check(noHistory.DayChangePercent is null && noHistory.History.Count == 0, "Missing history produces no invented daily change.");
        using var nulls = JsonDocument.Parse(Chart("AAPL").Replace("[90,100,105]", "[90,null,105]"));
        check(MarketsService.ParseQuote(nulls.RootElement, "AAPL").History.Count == 2, "Null trading points are skipped.");
        foreach (var invalid in new[] { "{}", """{"chart":{"error":{"code":"Not Found"},"result":null}}""", Chart("MSFT"), Chart("AAPL").Replace("\"regularMarketPrice\":105", "\"regularMarketPrice\":-1") })
        {
            var rejected = false;
            using var bad = JsonDocument.Parse(invalid);
            try { MarketsService.ParseQuote(bad.RootElement, "AAPL"); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "Malformed, missing or mismatched market responses are rejected.");
        }
        try
        {
            UiText.SetLanguage("es");
            check(MarketPresentation.Price(quote).Contains("105,00") && MarketPresentation.Change(-2.5m) == "-2,50%", "Market numbers follow the Spanish interface culture.");
            check(MarketPresentation.Question(quote).StartsWith("Explica") && MarketPresentation.Question(quote).Contains(quote.SourceUri.AbsoluteUri), "Spanish explanation drafts include source and quote context.");
            UiText.SetLanguage("en");
            check(MarketPresentation.Question(quote).StartsWith("Explain") && MarketPresentation.Timestamp(quote).StartsWith("Quote as of"), "English market labels remain the default.");
        }
        finally { UiText.SetLanguage("en"); }

        var news = MarketsService.ParseNews("""
            <rss><channel>
            <item><title>First &amp; title</title><link>https://finance.yahoo.com/news/one</link><pubDate>Tue, 20 Oct 2026 10:00:00 +0000</pubDate></item>
            <item><title>Unsafe</title><link>file:///C:/test</link><pubDate>Tue, 20 Oct 2026 10:00:00 +0000</pubDate></item>
            <item><title>Undated</title><link>https://example.com/news</link></item>
            <item><title>Second</title><link>https://example.com/news</link><pubDate>Tue, 20 Oct 2026 11:00:00 +0000</pubDate></item>
            <item><title>Duplicate</title><link>https://example.com/news</link><pubDate>Tue, 20 Oct 2026 11:00:00 +0000</pubDate></item>
            </channel></rss>
            """);
        check(news.Count == 2 && news[0].Title == "Second" && news[1].Title == "First & title" && news[0].Source == "example.com", "Headlines keep source, date and original text; invalid links and duplicates are excluded.");
        var dtdRejected = false;
        try { MarketsService.ParseNews("<!DOCTYPE rss [<!ENTITY secret SYSTEM 'file:///C:/secret'>]><rss>&secret;</rss>"); } catch (System.Xml.XmlException) { dtdRejected = true; }
        check(dtdRejected, "RSS cannot resolve external entities.");

        var clock = new MarketClock();
        var handler = new MarketHandler();
        using var http = new HttpClient(handler);
        using var service = new MarketsService(http);
        var session = new MarketsSession(service, clock);
        session.Configure(new() { Symbols = ["AAPL", "MSFT"] });
        await session.RefreshAsync(CancellationToken.None);
        check(session.Quotes.Count == 2 && !session.IsRefreshing && session.NextRefresh == clock.GetUtcNow().AddMinutes(15), "Successful quote refresh is bounded to fifteen-minute scheduling.");
        var requests = handler.Requests;
        await session.RefreshAsync(CancellationToken.None);
        check(handler.Requests == requests, "Frequent timer ticks do not fetch quotes before they are due.");
        clock.Advance(TimeSpan.FromSeconds(9));
        session.Tick(false);
        check(session.CurrentSymbol == "MSFT", "Unpinned market favorites rotate.");
        clock.Advance(TimeSpan.FromSeconds(9));
        session.Tick(true);
        session.Tick(false);
        check(session.CurrentSymbol == "MSFT", "Hover, focus and open panels can pause rotation.");
        session.Configure(new() { Symbols = ["AAPL", "MSFT"], PinnedSymbol = "AAPL" });
        clock.Advance(TimeSpan.FromSeconds(10));
        session.Tick(false);
        check(session.CurrentSymbol == "AAPL", "Pinned favorites stay selected.");
        handler.FailedSymbol = "AAPL";
        var retained = session.Quotes["AAPL"];
        await session.RefreshAsync(CancellationToken.None, true);
        check(session.Failed("AAPL") && ReferenceEquals(session.Quotes["AAPL"], retained) && !session.Failed("MSFT"), "Partial refresh failure retains the last valid quote and marks only the failed asset.");
        check(session.NextRefresh == clock.GetUtcNow().AddMinutes(5), "Quote failures schedule a five-minute retry.");
        handler.FailedSymbol = null;
        await session.RefreshAsync(CancellationToken.None, true);
        check(!session.Failed("AAPL"), "A successful retry clears the stale status.");

        handler.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = session.RefreshAsync(CancellationToken.None, true);
        await session.RefreshAsync(CancellationToken.None, true);
        check(session.IsRefreshing, "Concurrent refresh calls do not start a second batch.");
        session.Configure(new() { Symbols = ["BTC-USD"] });
        handler.Gate.SetResult();
        await pending;
        check(session.Quotes.Count == 0 && session.NextRefresh == DateTimeOffset.MinValue, "A completed old watchlist request cannot overwrite changed favorites.");
        handler.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        pending = session.RefreshAsync(cancellation.Token, true);
        cancellation.Cancel();
        var canceled = false;
        try { await pending; } catch (OperationCanceledException) { canceled = true; }
        check(canceled && !session.IsRefreshing && session.Quotes.Count == 0, "Cancellation drains quote work without publishing partial data.");
        handler.Gate = null;
        await session.RefreshAsync(CancellationToken.None);
        check(session.Quotes.ContainsKey("BTC-USD"), "The changed watchlist loads on its next refresh.");
        check(handler.LastUri?.Host == "query1.finance.yahoo.com" && handler.LastUri.Query == "?interval=1d&range=1mo" && handler.HasUserAgent,
            "Quote requests target the source with an identifying user agent and bounded daily history.");
        session.Configure(new() { Symbols = [] });
        requests = handler.Requests;
        await session.RefreshAsync(CancellationToken.None);
        check(session.CurrentSymbol is null && handler.Requests == requests && session.Quotes.Count == 0, "Empty favorites do not make network calls.");
    }

    internal static string Chart(string symbol, decimal? percent = null, bool history = true) => JsonSerializer.Serialize(new
    {
        chart = new
        {
            result = new[] { new
            {
                meta = new { symbol, currency = "USD", longName = "Sample asset", regularMarketPrice = 105, regularMarketTime = Stamp("2026-10-20T15:00:00Z"),
                    chartPreviousClose = 10, gmtoffset = -14400, regularMarketChangePercent = percent },
                timestamp = history ? new[] { Stamp("2026-10-16T13:30:00Z"), Stamp("2026-10-19T13:30:00Z"), Stamp("2026-10-20T13:30:00Z") } : [],
                indicators = new { quote = new[] { new { close = history ? new[] { 90, 100, 105 } : [] } } }
            } },
            error = (object?)null
        }
    });
    private static long Stamp(string time) => DateTimeOffset.Parse(time, System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeSeconds();
    private sealed class MarketClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 20, 15, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
    private sealed class MarketHandler : HttpMessageHandler
    {
        public string? FailedSymbol;
        public TaskCompletionSource? Gate;
        public int Requests;
        public Uri? LastUri;
        public bool HasUserAgent;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            LastUri = request.RequestUri;
            HasUserAgent = request.Headers.UserAgent.Any();
            if (Gate is { } gate) await gate.Task.WaitAsync(token);
            var symbol = Uri.UnescapeDataString(request.RequestUri!.Segments[^1]);
            return new HttpResponseMessage(symbol == FailedSymbol ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK) { Content = new StringContent(Chart(symbol)) };
        }
    }
}
