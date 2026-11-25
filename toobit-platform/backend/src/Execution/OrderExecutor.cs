using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Toobit.Platform.Application;
using Toobit.Platform.Domain;
using Toobit.Platform.Domain.Orders;
using Toobit.Platform.Risk;

namespace Toobit.Platform.Execution;

public sealed class ExecutorOptions
{
    public int MaxRateLimitRetries { get; set; } = 4;
    public TimeSpan BackoffBase { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan BackoffMax { get; set; } = TimeSpan.FromSeconds(8);
}

/// <summary>
/// Drives one order through Received -> Validated -> RiskChecked -> Submitted -> Acked | Unknown | Rejected.
/// Rules that must never be broken (each has a test):
///  * Order row + ClientOrderId are persisted BEFORE any exchange call (idempotency).
///  * Kill switch is re-read immediately before submit, not only at risk-check time.
///  * 429 => request was NOT processed: back off with jitter and retry the SAME ClientOrderId.
///  * 418 => IP ban: stop, trip the kill switch, alert. Never keep hammering.
///  * Timeout / 5XX => Unknown (NOT failed). Never blind-retry a POST; the reconciler resolves it.
/// </summary>
public sealed class OrderExecutor(
    ITradingExchange exchange, IOrderRepository orders, IKillSwitch killSwitch, IRiskContextProvider riskContext,
    IStrategyConfigProvider strategy, IRiskEventRepository riskEvents, IAuditLog audit, IClock clock,
    RiskEngine riskEngine, ExecutorOptions options, ILogger<OrderExecutor> log,
    Func<TimeSpan, CancellationToken, Task>? delay = null, Func<double>? rng = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((t, c) => Task.Delay(t, c));
    private readonly Func<double> _rng = rng ?? Random.Shared.NextDouble;

    public async Task<Order> ExecuteAsync(OrderIntent intent, string correlationId, CancellationToken ct)
    {
        var order = Order.Create(intent.Symbol, intent.Side, intent.Quantity, intent.Price,
            intent.StopLoss, intent.TakeProfit, correlationId, clock.UtcNow);
        await orders.SaveAsync(order, Array.Empty<OrderEvent>(), ct);          // persisted BEFORE submit

        await Step(order, OrderState.Validated, "intent valid", ct);

        var ctx = await riskContext.GetAsync(intent.Symbol, ct);
        var limits = await strategy.GetRiskLimitsAsync(ct);
        var decision = riskEngine.Evaluate(intent, ctx with { KillSwitchOn = await killSwitch.IsOnAsync(ct) }, limits);
        if (!decision.Approved)
        {
            PlatformMetrics.RiskRejections.Add(1, new KeyValuePair<string, object?>("rule", decision.Rule.ToString()));
            var now = clock.UtcNow;
            await riskEvents.AddAsync(new RiskEvent { Symbol = intent.Symbol, Rule = decision.Rule.ToString()!,
                Reason = decision.Reason, CorrelationId = correlationId, CreatedAt = now, UpdatedAt = now }, ct);
            await Step(order, OrderState.Rejected, $"risk: {decision.Reason}", ct);
            return order;
        }
        await Step(order, OrderState.RiskChecked, "risk approved", ct);

        // Instant halt: re-check at the last possible moment.
        if (await killSwitch.IsOnAsync(ct))
        {
            await Step(order, OrderState.Rejected, "kill switch engaged before submit", ct);
            return order;
        }

        await Step(order, OrderState.Submitted, "sending to exchange", ct);
        var sw = Stopwatch.StartNew();
        SubmitResult result = await SubmitWithBackoffAsync(order, ct);
        PlatformMetrics.OrderLatencyMs.Record(sw.Elapsed.TotalMilliseconds);

        switch (result.Outcome)
        {
            case SubmitOutcome.Acked:
                order.SetToobitOrderId(result.ToobitOrderId ?? "");
                await Step(order, OrderState.Acked, "exchange acknowledged", ct);
                break;
            case SubmitOutcome.Ambiguous:
                await Step(order, OrderState.Unknown, "ambiguous response (timeout/5XX); awaiting reconciliation", ct);
                break;
            case SubmitOutcome.Banned:
                await killSwitch.SetAsync(true, "system", "HTTP 418 from exchange", ct);
                await Step(order, OrderState.Rejected, "exchange ban (418); kill switch engaged", ct);
                break;
            default:   // Rejected, or RateLimited after exhausting retries (still never processed)
                await Step(order, OrderState.Rejected, result.Error ?? result.Outcome.ToString(), ct);
                break;
        }
        return order;
    }

    private async Task<SubmitResult> SubmitWithBackoffAsync(Order order, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            var r = await exchange.SubmitOrderAsync(order, ct);
            if (r.Outcome != SubmitOutcome.RateLimited) return r;

            PlatformMetrics.RateLimitHits.Add(1, new KeyValuePair<string, object?>("status", "429"));
            if (attempt > options.MaxRateLimitRetries) return r;
            var wait = Backoff.Compute(attempt, options.BackoffBase, options.BackoffMax, r.RetryAfter, _rng);
            log.LogWarning("429 on order {ClientOrderId}; attempt {Attempt}, backing off {WaitMs} ms",
                order.ClientOrderId, attempt, (int)wait.TotalMilliseconds);
            await _delay(wait, ct);
            if (await killSwitch.IsOnAsync(ct)) return new SubmitResult(SubmitOutcome.Rejected, Error: "kill switch engaged during backoff");
        }
    }

    private async Task Step(Order order, OrderState next, string reason, CancellationToken ct)
    {
        var ev = order.TransitionTo(next, reason, clock.UtcNow);
        await orders.SaveAsync(order, new[] { ev }, ct);
        PlatformMetrics.OrderTransitions.Add(1, new KeyValuePair<string, object?>("to", next.ToString()));
        // Every lifecycle transition is audited and observable.
        await audit.AppendAsync("system", $"order.{next}", $"{order.Symbol} {order.ClientOrderId}: {reason}", order.CorrelationId, ct);
        log.LogInformation("Order {ClientOrderId} {From} -> {To}: {Reason}", order.ClientOrderId, ev.From, next, reason);
    }

    /// <summary>Manual close from the dashboard: reduce-only MARKET order, audited with the acting user.</summary>
    public async Task<Order> ClosePositionAsync(string symbol, Side closeSide, decimal quantity, decimal refPrice,
        string actor, string correlationId, CancellationToken ct)
    {
        await audit.AppendAsync(actor, "position.manual_close", $"{symbol} {closeSide} qty={quantity}", correlationId, ct);
        var order = Order.Create(symbol, closeSide, quantity, null, 0, 0, correlationId, clock.UtcNow, reduceOnly: true);
        await orders.SaveAsync(order, Array.Empty<OrderEvent>(), ct);
        await Step(order, OrderState.Validated, $"manual close by {actor}", ct);
        // Closing reduces risk, so it deliberately bypasses the kill switch and notional rules (but is still audited).
        await Step(order, OrderState.RiskChecked, "reduce-only close", ct);
        await Step(order, OrderState.Submitted, "sending to exchange", ct);
        var r = await SubmitWithBackoffAsync(order, ct);
        if (r.Outcome == SubmitOutcome.Acked) { order.SetToobitOrderId(r.ToobitOrderId ?? ""); await Step(order, OrderState.Acked, "close acknowledged", ct); }
        else if (r.Outcome == SubmitOutcome.Ambiguous) await Step(order, OrderState.Unknown, "ambiguous close response", ct);
        else await Step(order, OrderState.Rejected, r.Error ?? r.Outcome.ToString(), ct);
        return order;
    }
}
