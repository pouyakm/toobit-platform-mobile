using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Toobit.Platform.Application;

namespace Toobit.Platform.Infrastructure.Persistence;

/// <summary>Retention windows in days. Defaults: Signals 90, OrderEvents 180, Logs 30 (log sink/Loki, not this DB), RiskEvents 365.</summary>
public sealed class RetentionOptions
{
    public int SignalsDays { get; set; } = 90;
    public int OrderEventsDays { get; set; } = 180;
    public int RiskEventsDays { get; set; } = 365;
    public int LogsDays { get; set; } = 30;
    public int RunEveryHours { get; set; } = 6;
}

/// <summary>
/// Deletes rows past retention. AuditLogs are NEVER pruned here: the runtime role has no DELETE on them (RUNBOOKS/db-roles.sql).
/// Only terminal-state order events are eligible, so a live order's trail is never cut.
/// </summary>
public sealed class RetentionService(IDbContextFactory<AppDbContext> dbf, IOptionsMonitor<RetentionOptions> options, IClock clock,
    ILogger<RetentionService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await PruneOnceAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogError(ex, "Retention run failed"); }
            try { await Task.Delay(TimeSpan.FromHours(options.CurrentValue.RunEveryHours), ct); } catch (OperationCanceledException) { return; }
        }
    }

    public async Task<(int Signals, int OrderEvents, int RiskEvents)> PruneOnceAsync(CancellationToken ct)
    {
        var o = options.CurrentValue; var now = clock.UtcNow;
        await using var db = await dbf.CreateDbContextAsync(ct);
        var sCut = now.AddDays(-o.SignalsDays); var eCut = now.AddDays(-o.OrderEventsDays); var rCut = now.AddDays(-o.RiskEventsDays);
        int s = await db.Signals.Where(x => x.CreatedAt < sCut).ExecuteDeleteAsync(ct);
        int r = await db.RiskEvents.Where(x => x.CreatedAt < rCut).ExecuteDeleteAsync(ct);
        // Terminal states only: Filled=6, Canceled=7, Rejected=8.
        int e = await db.OrderEvents.Where(ev => ev.CreatedAt < eCut &&
            db.Orders.Any(ord => ord.Id == ev.OrderId && (ord.StateMachineState == Domain.Orders.OrderState.Filled
                || ord.StateMachineState == Domain.Orders.OrderState.Canceled || ord.StateMachineState == Domain.Orders.OrderState.Rejected)))
            .ExecuteDeleteAsync(ct);
        log.LogInformation("Retention pruned {Signals} signals, {OrderEvents} order events, {RiskEvents} risk events", s, e, r);
        return (s, e, r);
    }
}
