using Habitat.Models;

namespace Habitat.Services;

internal sealed class MarketsSession(MarketsService service, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<string, MarketSnapshot> _quotes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failed = new(StringComparer.Ordinal);
    private int _revision;
    private int _index;
    private DateTimeOffset _rotateAt;
    public MarketsSettings Settings { get; private set; } = new();
    public IReadOnlyDictionary<string, MarketSnapshot> Quotes => _quotes;
    public bool IsRefreshing { get; private set; }
    public DateTimeOffset NextRefresh { get; private set; }
    public string? CurrentSymbol => Settings.PinnedSymbol ?? (Settings.Symbols.Length == 0 ? null : Settings.Symbols[_index % Settings.Symbols.Length]);
    public bool Failed(string symbol) => _failed.Contains(symbol);
    public event Action? Changed;

    public void Configure(MarketsSettings settings)
    {
        var normalized = settings.Normalize();
        var symbolsChanged = !Settings.Symbols.SequenceEqual(normalized.Symbols);
        Settings = normalized;
        _index = 0;
        _rotateAt = _clock.GetUtcNow().AddSeconds(8);
        if (symbolsChanged)
        {
            _revision++;
            foreach (var key in _quotes.Keys.Where(key => !Settings.Symbols.Contains(key)).ToArray()) _quotes.Remove(key);
            _failed.RemoveWhere(key => !Settings.Symbols.Contains(key));
            NextRefresh = DateTimeOffset.MinValue;
        }
        Changed?.Invoke();
    }

    public void Tick(bool pauseRotation)
    {
        var now = _clock.GetUtcNow();
        if (pauseRotation) _rotateAt = now.AddSeconds(8);
        if (Settings.PinnedSymbol is not null || Settings.Symbols.Length < 2 || now < _rotateAt) return;
        _index = (_index + 1) % Settings.Symbols.Length;
        _rotateAt = now.AddSeconds(8);
        Changed?.Invoke();
    }

    public async Task RefreshAsync(CancellationToken token, bool force = false)
    {
        if (IsRefreshing || !force && _clock.GetUtcNow() < NextRefresh) return;
        IsRefreshing = true;
        var revision = _revision;
        var symbols = Settings.Symbols.ToArray();
        Changed?.Invoke();
        try
        {
            // Independent failures must not discard the rest of the watchlist.
            var results = await Task.WhenAll(symbols.Select(async symbol =>
            {
                try { return (Symbol: symbol, Quote: (MarketSnapshot?)await service.GetQuoteAsync(symbol, token)); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception exception) when (exception is HttpRequestException or IOException or System.Text.Json.JsonException or OperationCanceledException)
                { return (Symbol: symbol, Quote: (MarketSnapshot?)null); }
            }));
            token.ThrowIfCancellationRequested();
            if (revision != _revision) return;
            foreach (var result in results)
            {
                if (result.Quote is { } quote) { _quotes[result.Symbol] = quote; _failed.Remove(result.Symbol); }
                else _failed.Add(result.Symbol);
            }
            NextRefresh = _clock.GetUtcNow().AddMinutes(_failed.Count > 0 ? 5 : 15);
        }
        finally { IsRefreshing = false; Changed?.Invoke(); }
    }
}
