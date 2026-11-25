using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Toobit.Platform.Application;
using Toobit.Platform.Domain.Market;

namespace Toobit.Platform.MarketScanner;

public sealed class ScannerOptions
{
    public int IntervalSeconds { get; set; } = 60;
    public int TopN { get; set; } = 20;
    public decimal MinQuoteVolume { get; set; } = 1_000_000m;
    public int StaleAfterSeconds { get; set; } = 180;
    public double BaselineEmaAlpha { get; set; } = 0.2;
    /// <summary>Named weight sets; a feature flag picks one at runtime (no redeploy).</summary>
    public Dictionary<string, ScoringWeights> WeightSets { get; set; } = new() { ["default"] = ScoringWeights.Default };
}

/// <summary>Last-known-good snapshot served when the scanner fails (fallback documented in ARCHITECTURE.md).</summary>
public sealed class ScannerState
{
    private IReadOnlyList<TrendingSymbol> _top = Array.Empty<TrendingSymbol>();
    public DateTimeOffset? LastSuccess { get; private set; }
    public bool IsStale { get; private set; }
    public IReadOnlyList<TrendingSymbol> Top => _top;

    // Manual overrides (MVP: in-memory only, lost on restart; durable ScannerConfig table is a follow-up).
    private readonly ConcurrentDictionary<string, bool> _pins = new();
    public IReadOnlyCollection<string> Pinned => _pins.Where(p => p.Value).Select(p => p.Key).ToList();
    public void SetPin(string symbol, bool pinned) { if (pinned) _pins[symbol] = true; else _pins.TryRemove(symbol, out _); }
    public void Update(IReadOnlyList<TrendingSymbol> top, DateTimeOffset now) { _top = top; LastSuccess = now; IsStale = false; }
    public void MarkStale() => IsStale = true;
}

public sealed class ScannerWorker(
    IScannerExchange exchange, ITrendingRepository repo, IFeatureFlags flags, ScannerState state, IClock clock,
    IOptionsMonitor<ScannerOptions> options, ILogger<ScannerWorker> log) : BackgroundService
{
    private readonly ConcurrentDictionary<string, decimal> _baseline = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            try { await Task.Delay(TimeSpan.FromSeconds(options.CurrentValue.IntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One scan. Never throws: failure keeps serving the last-known-good snapshot and flags staleness.</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        var opt = options.CurrentValue;
        var sw = Stopwatch.StartNew();
        try
        {
            var tickers = await exchange.GetTickersAsync(ct);
            var setName = await flags.ScannerWeightSetAsync(ct);
            var weights = opt.WeightSets.TryGetValue(setName, out var w) ? w : opt.WeightSets["default"];
            var ranked = RankingService.Rank(tickers, weights, opt.MinQuoteVolume,
                s => _baseline.TryGetValue(s, out var b) ? b : null, opt.TopN);

            var snapshotId = Domain.Common.Uuid7.NewGuid();
            var now = clock.UtcNow;
            var rows = ranked.Select(r => new TrendingSymbol
            {
                SnapshotId = snapshotId, Rank = r.Rank, Symbol = r.Symbol, Score = (decimal)r.Score,
                QuoteVolume = (decimal)r.QuoteVolume, Momentum = (decimal)r.Momentum,
                VolumeSpikeRatio = (decimal)r.VolumeSpikeRatio, NormVolume = (decimal)r.NormVolume,
                NormMomentum = (decimal)r.NormMomentum, NormSpike = (decimal)r.NormSpike,
                WeightsJson = RankingService.WeightsJson(weights), Explanation = r.Explanation,
                CreatedAt = now, UpdatedAt = now,
            }).ToList();

            await repo.SaveSnapshotAsync(rows, ct);
            state.Update(rows, now);
            PlatformMetrics.SetScannerStale(false);

            foreach (var t in tickers)   // update EMA baseline AFTER ranking so a spike is measured against history
                _baseline.AddOrUpdate(t.Symbol, t.QuoteVolume24h,
                    (_, old) => old + (decimal)opt.BaselineEmaAlpha * (t.QuoteVolume24h - old));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Never log request headers/URLs with credentials: the scanner endpoint is public and unsigned.
            log.LogWarning(ex, "Scanner run failed; serving last-known-good snapshot");
            var age = state.LastSuccess is null ? TimeSpan.MaxValue : clock.UtcNow - state.LastSuccess.Value;
            if (age.TotalSeconds > opt.StaleAfterSeconds) { state.MarkStale(); PlatformMetrics.SetScannerStale(true); }
        }
        finally { PlatformMetrics.ScannerRuntimeMs.Record(sw.Elapsed.TotalMilliseconds); }
    }
}
