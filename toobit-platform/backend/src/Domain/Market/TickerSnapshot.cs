using Toobit.Platform.Domain.Common;

namespace Toobit.Platform.Domain.Market;

/// <summary>One symbol from GET /quote/v1/contract/ticker/24hr, reduced to what ranking needs.</summary>
public sealed record TickerSnapshot(string Symbol, decimal LastPrice, decimal QuoteVolume24h, decimal PriceChangePercent24h);

/// <summary>Transparent per-metric breakdown persisted with each ranked symbol (ADR-0002).</summary>
public sealed class TrendingSymbol : Entity
{
    public Guid SnapshotId { get; init; }
    public int Rank { get; init; }
    public string Symbol { get; init; } = "";
    public decimal Score { get; init; }
    public decimal QuoteVolume { get; init; }
    public decimal Momentum { get; init; }
    public decimal VolumeSpikeRatio { get; init; }
    public decimal NormVolume { get; init; }
    public decimal NormMomentum { get; init; }
    public decimal NormSpike { get; init; }
    public string WeightsJson { get; init; } = "";
    public string Explanation { get; init; } = "";
}
