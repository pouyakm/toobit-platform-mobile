using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Toobit.Platform.Application;
using Toobit.Platform.Domain;
using Toobit.Platform.Domain.Orders;

namespace Toobit.Platform.Execution;

public sealed class ReconciliationOptions
{
    public int IntervalSeconds { get; set; } = 30;
    public int UnknownGraceSeconds { get; set; } = 20;
    public int MaxUnknownRetries { get; set; } = 10;
}

/// <summary>ADR-0003. The exchange is the source of truth; divergence pauses the affected symbol.</summary>
public sealed class ReconciliationService(
    ITradingExchange exchange, IOrderRepository orders, IReconciliationRepository events, ISymbolPauseStore pauses,
    IAuditLog audit, IClock clock, ReconciliationOptions options, ILogger<ReconciliationService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await ReconcileOnceAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Reconciliation cycle failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), ct); } catch (OperationCanceledException) { break; }
        }
    }

    public async Task ReconcileOnceAsync(CancellationToken ct)
    {
        await ResolveUnknownAsync(ct);
        await DiffAgainstExchangeAsync(ct);
    }

    /// <summary>Resolve Unknown orders by querying ClientOrderId. Absent is only trusted after a grace period.</summary>
    private async Task ResolveUnknownAsync(CancellationToken ct)
    {
        foreach (var order in await orders.GetByStateAsync(OrderState.Unknown, ct))
        {
            var res = await exchange.QueryOrderAsync(order.ClientOrderId, ct);
            var now = clock.UtcNow;
            order.IncrementUnknownRetries();

            if (res.Status == QueryStatus.Found && res.Order is { } found)
            {
                order.SetToobitOrderId(found.ToobitOrderId);
                var ev = order.TransitionTo(found.State, "reconciled from exchange by ClientOrderId", now);
                await orders.SaveAsync(order, new[] { ev }, ct);
                await events.AddAsync(Event(order.Symbol, "UnknownResolved", $"{order.ClientOrderId} -> {found.State}", false, now), ct);
                await audit.AppendAsync("system", "reconcile.unknown_resolved", $"{order.ClientOrderId} -> {found.State}", order.CorrelationId, ct);
            }
            else if (res.Status == QueryStatus.NotFound && (now - order.UpdatedAt).TotalSeconds >= options.UnknownGraceSeconds)
            {
                var ev = order.TransitionTo(OrderState.Rejected, "not_found_on_exchange after grace period", now);
                await orders.SaveAsync(order, new[] { ev }, ct);
                await events.AddAsync(Event(order.Symbol, "UnknownResolved", $"{order.ClientOrderId} not found -> Rejected", false, now), ct);
            }
            else
            {
                await orders.SaveAsync(order, Array.Empty<OrderEvent>(), ct);   // persist retry counter
                if (order.UnknownRetries >= options.MaxUnknownRetries)
                    await Diverged(order.Symbol, "OrderDivergence", $"{order.ClientOrderId} unresolved after {order.UnknownRetries} attempts", ct);
            }
        }
    }

    private async Task DiffAgainstExchangeAsync(CancellationToken ct)
    {
        var local = (await orders.GetActiveAsync(ct)).Where(o => o.StateMachineState is OrderState.Acked or OrderState.PartiallyFilled).ToList();
        var remote = await exchange.GetOpenOrdersAsync(ct);
        var remoteIds = remote.Select(r => r.ClientOrderId).ToHashSet();

        foreach (var o in local.Where(o => !remoteIds.Contains(o.ClientOrderId)))
        {
            // Locally open but gone remotely: it filled or was canceled elsewhere. Do not guess; flag and pause.
            var q = await exchange.QueryOrderAsync(o.ClientOrderId, ct);
            if (q.Status == QueryStatus.Found && q.Order is { } e && e.State != o.StateMachineState && OrderStateMachine.CanTransition(o.StateMachineState, e.State))
            {
                var ev = o.TransitionTo(e.State, "reconciled: state changed on exchange", clock.UtcNow);
                await orders.SaveAsync(o, new[] { ev }, ct);
            }
            else await Diverged(o.Symbol, "OrderDivergence", $"{o.ClientOrderId} open locally, not open on exchange", ct);
        }
        foreach (var r in remote.Where(r => local.All(l => l.ClientOrderId != r.ClientOrderId)))
            await Diverged("*", "OrderDivergence", $"exchange has open order {r.ClientOrderId} unknown locally", ct);
    }

    private async Task Diverged(string symbol, string kind, string detail, CancellationToken ct)
    {
        var now = clock.UtcNow;
        PlatformMetrics.ReconDivergences.Add(1);
        log.LogWarning("RECONCILIATION DIVERGENCE {Kind} {Symbol}: {Detail}", kind, symbol, detail);
        if (symbol != "*") await pauses.PauseAsync(symbol, detail, ct);
        await events.AddAsync(Event(symbol, kind, detail, symbol != "*", now), ct);
        await audit.AppendAsync("system", "reconcile.divergence", $"{symbol}: {detail}", "", ct);
    }

    private static ReconciliationEvent Event(string symbol, string kind, string detail, bool paused, DateTimeOffset now) =>
        new() { Symbol = symbol, Kind = kind, Detail = detail, PausedSymbol = paused, CreatedAt = now, UpdatedAt = now };
}
