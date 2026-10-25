namespace Habitat.Models;

internal sealed record MarketPoint(DateTimeOffset Time, decimal Close);

internal sealed record MarketSnapshot(string Symbol, string Name, string Currency, decimal Price,
    decimal? DayChangePercent, DateTimeOffset QuotedAt, IReadOnlyList<MarketPoint> History)
{
    public Uri SourceUri => new("https://finance.yahoo.com/quote/" + Uri.EscapeDataString(Symbol) + "/");
}

internal sealed record MarketNews(string Title, Uri Link, DateTimeOffset PublishedAt, string Source);
