using System.Text.Json;
using Toobit.Platform.Domain.Market;

namespace Toobit.Platform.MarketScanner;

/// <summary>ADR-0002. Weights must sum to 1 (validated at load).</summary>
public sealed record ScoringWeights(double Volume, double Momentum, double Spike)
{
    public static ScoringWeights Default { get; } = new(0.4, 0.4, 0.2);
    public ScoringWeights Validate()
    {
        if (Volume < 0 || Momentum < 0 || Spike < 0) throw new ArgumentException("weights must be >= 0");
        if (Math.Abs(Volume + Momentum + Spike - 1.0) > 1e-9) throw new ArgumentException("weights must sum to 1");
        return this;
    }
}

public sealed record RankedSymbol(int Rank, string Symbol, double Score, double QuoteVolume, double Momentum,
    double VolumeSpikeRatio, double NormVolume, double NormMomentum, double NormSpike, string Explanation);

/// <summary>
/// Pure ranking. Properties enforced by FsCheck tests: score in [0,1]; monotone in each metric;
/// deterministic and independent of input order.
/// </summary>
public static class RankingService
{
    public const string SymbolSuffix = "-SWAP-USDT";

    public static IReadOnlyList<RankedSymbol> Rank(
        IEnumerable<TickerSnapshot> tickers, ScoringWeights weights, decimal minQuoteVolume,
        Func<string, decimal?> volumeBaseline, int topN)
    {
        weights.Validate();
        var universe = tickers
            .Where(t => t.Symbol.EndsWith(SymbolSuffix, StringComparison.Ordinal))
            .Where(t => t.QuoteVolume24h >= minQuoteVolume)
            .OrderBy(t => t.Symbol, StringComparer.Ordinal)            // determinism
            .ToList();
        if (universe.Count == 0) return Array.Empty<RankedSymbol>();

        var raw = universe.Select(t =>
        {
            double vol = Math.Log(1.0 + (double)t.QuoteVolume24h);   // log1p dampens outliers
            double mom = Math.Abs((double)t.PriceChangePercent24h);
            var baseline = volumeBaseline(t.Symbol);
            double spike = baseline is > 0 ? (double)(t.QuoteVolume24h / baseline.Value) : 1.0;
            return (t.Symbol, t.QuoteVolume24h, vol, mom, spike);
        }).ToList();

        var nv = Normalize(raw.Select(r => r.vol).ToList());
        var nm = Normalize(raw.Select(r => r.mom).ToList());
        var ns = Normalize(raw.Select(r => r.spike).ToList());

        var scored = raw.Select((r, i) =>
        {
            double score = weights.Volume * nv[i] + weights.Momentum * nm[i] + weights.Spike * ns[i];
            score = Math.Clamp(score, 0.0, 1.0);
            string why = $"score={score:F4} = {weights.Volume:F2}*vol({nv[i]:F3}) + {weights.Momentum:F2}*mom({nm[i]:F3}) + {weights.Spike:F2}*spike({ns[i]:F3})";
            return (r.Symbol, Score: score, Vol: (double)r.QuoteVolume24h, r.mom, r.spike,
                    NV: nv[i], NM: nm[i], NS: ns[i], Why: why);
        })
        .OrderByDescending(s => s.Score).ThenBy(s => s.Symbol, StringComparer.Ordinal)
        .Take(topN).ToList();

        return scored.Select((s, i) => new RankedSymbol(i + 1, s.Symbol, s.Score, s.Vol, s.mom, s.spike,
            s.NV, s.NM, s.NS, s.Why)).ToList();
    }

    /// <summary>Min-max to [0,1]; a constant series maps to 0.5.</summary>
    public static double[] Normalize(IReadOnlyList<double> xs)
    {
        double min = xs.Min(), max = xs.Max();
        if (max - min < 1e-12) return xs.Select(_ => 0.5).ToArray();
        return xs.Select(x => (x - min) / (max - min)).ToArray();
    }

    public static string WeightsJson(ScoringWeights w) => JsonSerializer.Serialize(w);
}
