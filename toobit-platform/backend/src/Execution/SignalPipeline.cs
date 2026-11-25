using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Toobit.Platform.Application;
using Toobit.Platform.Domain;
using Toobit.Platform.Domain.Orders;
using Toobit.Platform.Risk;

namespace Toobit.Platform.Execution;

public sealed record IncomingSignal(string Symbol, string Action, decimal Confidence, decimal ReferencePrice,
    string? ModelVersion, string FeaturesJson, string ExplainJson, string CorrelationId, DateTimeOffset ReceivedAt);

public sealed record QueuedIntent(OrderIntent Intent, string CorrelationId, DateTimeOffset SignalReceivedAt);

/// <summary>Bounded queue between signal evaluation and order execution: overflow is dropped + alerted, NEVER blocks.</summary>
public sealed class OrderQueue(int capacity, ILogger<OrderQueue> log)
{
    private readonly Channel<QueuedIntent> _ch = Channel.CreateBounded<QueuedIntent>(
        new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public ChannelReader<QueuedIntent> Reader => _ch.Reader;

    public bool TryEnqueue(QueuedIntent item)
    {
        if (_ch.Writer.TryWrite(item)) return true;
        PlatformMetrics.QueueDrops.Add(1);
        log.LogError("ALERT: order queue full, dropped intent for {Symbol} (corr {Corr})", item.Intent.Symbol, item.CorrelationId);
        return false;
    }
    public void Complete() => _ch.Writer.TryComplete();
}

/// <summary>Persists every signal (shadow mode logs even when trading is off) and enqueues entries when allowed.</summary>
public sealed class SignalService(ISignalRepository signals, IFeatureFlags flags, IStrategyConfigProvider strategy,
    ISymbolPauseStore pauses, OrderQueue queue, IClock clock)
{
    public async Task<(bool Shadow, bool Enqueued)> HandleAsync(IncomingSignal s, CancellationToken ct)
    {
        bool tradingOn = await flags.AutomatedTradingEnabledAsync(ct);
        var now = clock.UtcNow;
        await signals.AddAsync(new Signal
        {
            Symbol = s.Symbol, Action = s.Action, Confidence = s.Confidence, ModelVersion = s.ModelVersion,
            FeaturesJson = s.FeaturesJson, ExplainJson = s.ExplainJson, Shadow = !tradingOn,
            CorrelationId = s.CorrelationId, CreatedAt = now, UpdatedAt = now,
        }, ct);

        if (!tradingOn) return (true, false);
        if (s.Action is not ("enter_long" or "enter_short")) return (false, false);
        if (await pauses.IsPausedAsync(s.Symbol, ct)) return (false, false);

        var cfg = await strategy.GetAsync(ct);
        bool buy = s.Action == "enter_long";
        decimal price = s.ReferencePrice;
        decimal sl = buy ? price * (1 - cfg.StopLossPct) : price * (1 + cfg.StopLossPct);
        decimal tp = buy ? price * (1 + cfg.TakeProfitPct) : price * (1 - cfg.TakeProfitPct);
        decimal qty = Math.Round(cfg.OrderNotionalUsdt / price, 6);
        var intent = new OrderIntent(s.Symbol, buy ? Side.Buy : Side.Sell, qty, price, sl, tp);
        return (false, queue.TryEnqueue(new QueuedIntent(intent, s.CorrelationId, s.ReceivedAt)));
    }
}

/// <summary>Single consumer. Graceful shutdown: stop accepting, drain what is in flight, then exit.</summary>
public sealed class OrderWorker(OrderQueue queue, OrderExecutor executor, IClock clock, ILogger<OrderWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    var order = await executor.ExecuteAsync(item.Intent, item.CorrelationId, CancellationToken.None);
                    PlatformMetrics.SignalToSubmitMs.Record((clock.UtcNow - item.SignalReceivedAt).TotalMilliseconds);
                    log.LogInformation("Order {Id} finished in state {State}", order.ClientOrderId, order.StateMachineState);
                }
                catch (Exception ex) { log.LogError(ex, "Order execution failed for {Symbol}", item.Intent.Symbol); }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Complete();                       // no new intents
        await base.StopAsync(cancellationToken);
        // Drain intents already queued, bounded by the host shutdown timeout.
        while (queue.Reader.TryRead(out var item) && !cancellationToken.IsCancellationRequested)
            await executor.ExecuteAsync(item.Intent, item.CorrelationId, cancellationToken);
    }
}
