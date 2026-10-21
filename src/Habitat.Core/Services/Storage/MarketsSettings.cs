namespace Habitat.Services;

internal sealed record MarketsSettings
{
    public const int MaximumSymbols = 5;
    public string[] Symbols { get; init; } = ["^GSPC", "^IBEX", "EURUSD=X", "BTC-USD"];
    public string? PinnedSymbol { get; init; }

    public static string? NormalizeSymbol(string? value)
    {
        var symbol = value?.Trim().ToUpperInvariant();
        return symbol is { Length: > 0 and <= 32 } && symbol.Any(char.IsAsciiLetterOrDigit) &&
            symbol.All(c => char.IsAsciiLetterOrDigit(c) || c is '^' or '.' or '-' or '=') ? symbol : null;
    }

    public MarketsSettings Normalize()
    {
        var symbols = (Symbols ?? new MarketsSettings().Symbols).Select(NormalizeSymbol)
            .OfType<string>().Distinct(StringComparer.Ordinal).Take(MaximumSymbols).ToArray();
        var pinned = NormalizeSymbol(PinnedSymbol);
        return this with { Symbols = symbols, PinnedSymbol = symbols.Contains(pinned) ? pinned : null };
    }
}
