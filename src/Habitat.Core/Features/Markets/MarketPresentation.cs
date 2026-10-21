using Habitat.Models;

namespace Habitat.Services;

internal static class MarketPresentation
{
    public static string Price(MarketSnapshot quote) => quote.Price.ToString(quote.Price < 10 ? "N4" : "N2", UiText.Culture) + " " + quote.Currency;
    public static string Change(decimal? percent, bool compact = false) => percent is { } value
        ? (value > 0 ? "+" : "") + value.ToString(compact ? "0.0" : "0.00", UiText.Culture) + "%" : "—";
    public static string Timestamp(MarketSnapshot quote) => UiText.Format("MarketsQuotedAt", quote.QuotedAt.ToLocalTime().ToString("g", UiText.Culture));
    public static string Question(MarketSnapshot quote) => UiText.Format("MarketsQuestion",
        quote.Name, quote.Symbol, Price(quote), Change(quote.DayChangePercent),
        quote.QuotedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture),
        quote.SourceUri.AbsoluteUri,
        string.Join("; ", quote.History.Select(point => point.Time.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ": " +
            point.Close.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture))));
}
