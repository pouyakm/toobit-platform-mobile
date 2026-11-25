using System.Diagnostics.Metrics;

namespace Toobit.Platform.Application;

/// <summary>All custom metrics (exported by OpenTelemetry -> Prometheus). Names are part of the Grafana contract.</summary>
public static class PlatformMetrics
{
    public const string MeterName = "Toobit.Platform";
    private static readonly Meter M = new(MeterName, "1.0.0");

    public static readonly Histogram<double> OrderLatencyMs = M.CreateHistogram<double>("order_submit_latency", "ms");
    public static readonly Histogram<double> SignalToSubmitMs = M.CreateHistogram<double>("signal_to_submit_latency", "ms");
    public static readonly Histogram<double> ScannerRuntimeMs = M.CreateHistogram<double>("scanner_runtime", "ms");
    public static readonly Counter<long> RateLimitHits = M.CreateCounter<long>("exchange_rate_limit_total");   // tag: status=429|418
    public static readonly Counter<long> WsReconnects = M.CreateCounter<long>("ws_reconnect_total");
    public static readonly Counter<long> RiskRejections = M.CreateCounter<long>("risk_rejections_total");     // tag: rule
    public static readonly Counter<long> OrderTransitions = M.CreateCounter<long>("order_transitions_total"); // tag: to
    public static readonly Counter<long> QueueDrops = M.CreateCounter<long>("pipeline_queue_drops_total");
    public static readonly Counter<long> ReconDivergences = M.CreateCounter<long>("reconciliation_divergence_total");

    private static int _scannerStale;
    private static long _lastTickMs;
    static PlatformMetrics()
    {
        M.CreateObservableGauge("scanner_stale", () => _scannerStale);
        M.CreateObservableGauge("market_data_age_seconds", () =>
            _lastTickMs == 0 ? double.NaN : (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastTickMs) / 1000.0);
    }
    public static void SetScannerStale(bool stale) => Interlocked.Exchange(ref _scannerStale, stale ? 1 : 0);
    public static void MarkMarketTick() => Interlocked.Exchange(ref _lastTickMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}
