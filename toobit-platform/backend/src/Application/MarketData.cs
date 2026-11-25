using System.Collections.Concurrent;

namespace Toobit.Platform.Application;

public sealed record PriceTick(string Symbol, decimal Last, decimal Open, decimal High, decimal Low, decimal QuoteVolume, DateTimeOffset At);
public sealed record Candle(long OpenTimeMs, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

/// <summary>Latest tick per symbol (feeds the dashboard) with staleness tracking.</summary>
public sealed class PriceBook(IClock clock)
{
    private readonly ConcurrentDictionary<string, PriceTick> _ticks = new();
    public void Update(PriceTick t) { _ticks[t.Symbol] = t; PlatformMetrics.MarkMarketTick(); }
    public IReadOnlyList<PriceTick> Snapshot() => _ticks.Values.OrderBy(t => t.Symbol, StringComparer.Ordinal).ToList();
    public DateTimeOffset? LastTickAt => _ticks.IsEmpty ? null : _ticks.Values.Max(t => t.At);
    public bool IsStale(TimeSpan threshold) => LastTickAt is not { } t || clock.UtcNow - t > threshold;
    public bool WsConnected { get; set; }
}

/// <summary>Rolling per-symbol candle window. An update with the same open time replaces the forming candle.</summary>
public sealed class CandleBuffer(int capacity)
{
    private readonly ConcurrentDictionary<string, List<Candle>> _c = new();

    public void Upsert(string symbol, Candle c)
    {
        var list = _c.GetOrAdd(symbol, _ => new List<Candle>());
        lock (list)
        {
            int i = list.FindIndex(x => x.OpenTimeMs == c.OpenTimeMs);
            if (i >= 0) list[i] = c;
            else { list.Add(c); list.Sort((a, b) => a.OpenTimeMs.CompareTo(b.OpenTimeMs)); }
            while (list.Count > capacity) list.RemoveAt(0);
        }
    }

    public IReadOnlyList<Candle> Get(string symbol)
    {
        if (!_c.TryGetValue(symbol, out var l)) return Array.Empty<Candle>();
        lock (l) return l.ToList();
    }
}
