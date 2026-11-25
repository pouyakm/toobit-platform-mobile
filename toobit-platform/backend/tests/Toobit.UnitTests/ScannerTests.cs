using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Toobit.Platform.Application;
using Toobit.Platform.Domain.Market;
using Toobit.Platform.Infrastructure.Toobit;
using Toobit.Platform.MarketScanner;
using Xunit;

namespace Toobit.UnitTests;

public class RankingServiceTests
{
    private static TickerSnapshot T(string s, decimal vol, decimal pct) => new(s, 1m, vol, pct);
    private static readonly Func<string, decimal?> NoBaseline = _ => null;

    [Fact]
    public void Filters_to_swap_usdt_and_min_volume()
    {
        var r = RankingService.Rank(new[] { T("BTC-SWAP-USDT", 5e6m, 1), T("BTCUSDT", 9e9m, 9), T("ETH-SWAP-USDT", 10m, 5) },
            ScoringWeights.Default, 1_000_000m, NoBaseline, 10);
        Assert.Single(r);
        Assert.Equal("BTC-SWAP-USDT", r[0].Symbol);
    }

    [Fact]
    public void Ranks_by_composite_score_and_explains_it()
    {
        var r = RankingService.Rank(new[] { T("A-SWAP-USDT", 1e6m, 0.5m), T("B-SWAP-USDT", 9e6m, 8m), T("C-SWAP-USDT", 3e6m, 2m) },
            ScoringWeights.Default, 0, NoBaseline, 3);
        Assert.Equal(new[] { "B-SWAP-USDT", "C-SWAP-USDT", "A-SWAP-USDT" }, r.Select(x => x.Symbol));
        Assert.Equal(new[] { 1, 2, 3 }, r.Select(x => x.Rank));
        Assert.All(r, x => Assert.Contains("score=", x.Explanation));
    }

    [Fact]
    public void Momentum_uses_absolute_change_so_big_drops_rank_high()
    {
        var r = RankingService.Rank(new[] { T("UP-SWAP-USDT", 1e6m, 1m), T("DOWN-SWAP-USDT", 1e6m, -9m) }, ScoringWeights.Default, 0, NoBaseline, 2);
        Assert.Equal("DOWN-SWAP-USDT", r[0].Symbol);
    }

    [Fact]
    public void Volume_spike_vs_baseline_lifts_the_score()
    {
        Func<string, decimal?> baseline = s => s == "A-SWAP-USDT" ? 1e6m : 4e6m;
        var r = RankingService.Rank(new[] { T("A-SWAP-USDT", 4e6m, 1m), T("B-SWAP-USDT", 4e6m, 1m) }, ScoringWeights.Default, 0, baseline, 2);
        Assert.Equal("A-SWAP-USDT", r[0].Symbol);   // 4x its baseline vs 1x
    }

    [Fact]
    public void Constant_universe_normalizes_to_half_and_never_divides_by_zero()
    {
        var r = RankingService.Rank(new[] { T("A-SWAP-USDT", 1e6m, 1m), T("B-SWAP-USDT", 1e6m, 1m) }, ScoringWeights.Default, 0, NoBaseline, 2);
        Assert.All(r, x => Assert.Equal(0.5, x.Score, 9));
    }

    [Fact]
    public void Weights_must_sum_to_one() => Assert.Throws<ArgumentException>(() => new ScoringWeights(0.5, 0.5, 0.5).Validate());

    [Fact]
    public void Empty_input_gives_empty_ranking() =>
        Assert.Empty(RankingService.Rank(Array.Empty<TickerSnapshot>(), ScoringWeights.Default, 0, NoBaseline, 5));

    // ---- property-based (FsCheck) ------------------------------------------------------------------------------
    private static IEnumerable<TickerSnapshot> Universe(PositiveInt[] vols, int[] pcts) =>
        vols.Select((v, i) => T($"S{i}-SWAP-USDT", v.Get, (decimal)(pcts[i % pcts.Length] % 100)));

    [Property(MaxTest = 200)]
    public bool Score_is_always_within_0_and_1(PositiveInt[] vols, NonEmptyArray<int> pcts)
    {
        if (vols.Length == 0) return true;
        return RankingService.Rank(Universe(vols, pcts.Get), ScoringWeights.Default, 0, NoBaseline, 1000)
            .All(r => r.Score >= 0.0 && r.Score <= 1.0 && !double.IsNaN(r.Score));
    }

    [Property(MaxTest = 200)]
    public bool Ranking_is_independent_of_input_order(PositiveInt[] vols, NonEmptyArray<int> pcts)
    {
        if (vols.Length == 0) return true;
        var u = Universe(vols, pcts.Get).ToList();
        var a = RankingService.Rank(u, ScoringWeights.Default, 0, NoBaseline, 1000).Select(r => (r.Symbol, r.Score));
        var b = RankingService.Rank(Enumerable.Reverse(u), ScoringWeights.Default, 0, NoBaseline, 1000).Select(r => (r.Symbol, r.Score));
        return a.SequenceEqual(b);
    }

    /// <summary>Monotonicity: raising ONE metric of one symbol (others fixed) never lowers that symbol's score.</summary>
    [Property(MaxTest = 200)]
    public bool Raising_volume_never_lowers_the_score(PositiveInt[] others, PositiveInt baseVol, PositiveInt bump)
    {
        var rest = others.Select((v, i) => T($"O{i}-SWAP-USDT", v.Get, 1m)).ToList();
        decimal Score(decimal vol)
        {
            var u = rest.Append(T("X-SWAP-USDT", vol, 1m));
            return (decimal)RankingService.Rank(u, ScoringWeights.Default, 0, NoBaseline, 10_000).Single(r => r.Symbol == "X-SWAP-USDT").Score;
        }
        return Score(baseVol.Get + bump.Get) >= Score(baseVol.Get) - 1e-12m;
    }

    [Property(MaxTest = 200)]
    public bool Raising_momentum_never_lowers_the_score(PositiveInt[] others, PositiveInt baseMom, PositiveInt bump)
    {
        var rest = others.Select((v, i) => T($"O{i}-SWAP-USDT", 1e6m, v.Get % 50)).ToList();
        decimal Score(decimal pct)
        {
            var u = rest.Append(T("X-SWAP-USDT", 1e6m, pct));
            return (decimal)RankingService.Rank(u, ScoringWeights.Default, 0, NoBaseline, 10_000).Single(r => r.Symbol == "X-SWAP-USDT").Score;
        }
        return Score(baseMom.Get % 50 + bump.Get % 50) >= Score(baseMom.Get % 50) - 1e-12m;
    }
}

public class ScannerParsingTests
{
    [Fact]
    public void Parses_short_and_long_field_names_and_filters_symbols()
    {
        using var doc = JsonDocument.Parse("""
            [{"s":"BTC-SWAP-USDT","c":"60000","qv":"123456.7","pcp":"1.5"},
             {"symbol":"ETH-SWAP-USDT","lastPrice":"3000","quoteVolume":"9000","priceChangePercent":"-2"},
             {"s":"BTCUSDT","c":"1","qv":"1","pcp":"1"}]
            """);
        var r = ToobitScannerExchange.Parse(doc.RootElement);
        Assert.Equal(2, r.Count);
        Assert.Equal(-2m, r.Single(x => x.Symbol == "ETH-SWAP-USDT").PriceChangePercent24h);
    }

    [Fact] public void Scanner_path_is_exactly_the_public_ticker_endpoint() =>
        Assert.Equal("/quote/v1/contract/ticker/24hr", ToobitScannerExchange.Path);
}

public class ScannerWorkerTests
{
    private sealed class FlakyExchange : IScannerExchange
    {
        public bool Fail { get; set; }
        public Task<IReadOnlyList<TickerSnapshot>> GetTickersAsync(CancellationToken ct) =>
            Fail ? throw new HttpRequestException("boom")
                 : Task.FromResult<IReadOnlyList<TickerSnapshot>>(new[] { new TickerSnapshot("BTC-SWAP-USDT", 1, 5e6m, 2), new TickerSnapshot("ETH-SWAP-USDT", 1, 6e6m, 3) });
    }
    private sealed class MemRepo : ITrendingRepository
    {
        public int Saves;
        public Task SaveSnapshotAsync(IReadOnlyList<TrendingSymbol> r, CancellationToken ct) { Saves++; return Task.CompletedTask; }
        public Task<IReadOnlyList<TrendingSymbol>> GetLatestAsync(int n, CancellationToken ct) => Task.FromResult<IReadOnlyList<TrendingSymbol>>(Array.Empty<TrendingSymbol>());
    }
    private sealed class Flags : IFeatureFlags
    {
        public Task<bool> AutomatedTradingEnabledAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<string> ScannerWeightSetAsync(CancellationToken ct) => Task.FromResult("default");
    }
    private sealed class Opts(ScannerOptions v) : IOptionsMonitor<ScannerOptions>
    {
        public ScannerOptions CurrentValue => v;
        public ScannerOptions Get(string? name) => v;
        public IDisposable? OnChange(Action<ScannerOptions, string?> listener) => null;
    }

    [Fact]
    public async Task Failure_serves_last_known_good_and_flags_stale_only_after_threshold()
    {
        var ex = new FlakyExchange(); var repo = new MemRepo(); var state = new ScannerState(); var clock = new FakeClock();
        var worker = new ScannerWorker(ex, repo, new Flags(), state, clock, new Opts(new ScannerOptions { StaleAfterSeconds = 120, MinQuoteVolume = 0 }),
            NullLogger<ScannerWorker>.Instance);

        await worker.RunOnceAsync(default);
        Assert.Equal(2, state.Top.Count); Assert.False(state.IsStale); Assert.Equal(1, repo.Saves);

        ex.Fail = true;
        clock.UtcNow += TimeSpan.FromSeconds(60);
        await worker.RunOnceAsync(default);                        // fails, but does NOT throw
        Assert.Equal(2, state.Top.Count); Assert.False(state.IsStale);   // still within threshold: last-known-good served

        clock.UtcNow += TimeSpan.FromSeconds(120);
        await worker.RunOnceAsync(default);
        Assert.Equal(2, state.Top.Count); Assert.True(state.IsStale);    // snapshot kept, flagged stale for the banner
    }
}

public class WebSocketProtocolTests
{
    [Fact]
    public void Subscribe_message_matches_documented_format()
    {
        using var d = System.Text.Json.JsonDocument.Parse(Toobit.Platform.Infrastructure.Toobit.ToobitWsProtocol.Subscribe("realtimes", new[] { "BTC-SWAP-USDT", "ETH-SWAP-USDT" }));
        var r = d.RootElement;
        Assert.Equal("BTC-SWAP-USDT,ETH-SWAP-USDT", r.GetProperty("symbol").GetString());
        Assert.Equal("realtimes", r.GetProperty("topic").GetString());
        Assert.Equal("sub", r.GetProperty("event").GetString());
    }

    [Fact]
    public void Parses_ticker_kline_and_pong_frames()
    {
        var tick = Toobit.Platform.Infrastructure.Toobit.ToobitWsProtocol.Parse("""{"symbol":"BTC-SWAP-USDT","topic":"realtimes","data":[{"t":1668753480049,"s":"BTC-SWAP-USDT","c":"445","h":"450","l":"310","o":"311","v":"3747.7","qv":"1426443.9"}],"f":true}""");
        Assert.Equal(445m, tick.Tick!.Last); Assert.Equal("BTC-SWAP-USDT", tick.Tick.Symbol); Assert.Equal(1426443.9m, tick.Tick.QuoteVolume);

        var k = Toobit.Platform.Infrastructure.Toobit.ToobitWsProtocol.Parse("""{"symbol":"BTC-SWAP-USDT","topic":"kline","params":{"klineType":"1h"},"data":[{"t":1668753840000,"s":"BTC-SWAP-USDT","c":"445","h":"446","l":"440","o":"441","v":"7"}]}""");
        Assert.Equal("BTC-SWAP-USDT", k.Kline!.Value.Symbol); Assert.Equal(441m, k.Kline.Value.Candle.Open); Assert.Equal(1668753840000, k.Kline.Value.Candle.OpenTimeMs);

        Assert.True(Toobit.Platform.Infrastructure.Toobit.ToobitWsProtocol.Parse("""{"pong":1535975085052}""").IsPong);
        Assert.Null(Toobit.Platform.Infrastructure.Toobit.ToobitWsProtocol.Parse("""{"topic":"other","data":[]}""").Tick);
    }
}

public class MarketDataTests
{
    [Fact]
    public void Candle_buffer_replaces_forming_candle_orders_by_time_and_caps_capacity()
    {
        var b = new CandleBuffer(3);
        Candle C(long t, decimal close) => new(t, 1, 2, 0.5m, close, 1);
        b.Upsert("X", C(2, 1)); b.Upsert("X", C(1, 1)); b.Upsert("X", C(2, 9));      // same open time => replaced, not duplicated
        Assert.Equal(new[] { 1L, 2L }, b.Get("X").Select(c => c.OpenTimeMs));
        Assert.Equal(9m, b.Get("X")[1].Close);
        b.Upsert("X", C(3, 1)); b.Upsert("X", C(4, 1));
        Assert.Equal(new[] { 2L, 3L, 4L }, b.Get("X").Select(c => c.OpenTimeMs));   // oldest evicted
    }

    [Fact]
    public void Price_book_reports_staleness_after_threshold()
    {
        var clock = new FakeClock(); var pb = new PriceBook(clock);
        Assert.True(pb.IsStale(TimeSpan.FromSeconds(30)));                            // no data yet => stale
        pb.Update(new PriceTick("BTC-SWAP-USDT", 1, 1, 1, 1, 1, clock.UtcNow));
        Assert.False(pb.IsStale(TimeSpan.FromSeconds(30)));
        clock.UtcNow += TimeSpan.FromSeconds(31);
        Assert.True(pb.IsStale(TimeSpan.FromSeconds(30)));                            // banner condition
    }
}
