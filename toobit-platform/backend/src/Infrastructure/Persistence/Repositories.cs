using Microsoft.EntityFrameworkCore;
using Toobit.Platform.Application;
using Toobit.Platform.Domain;
using Toobit.Platform.Domain.Market;
using Toobit.Platform.Domain.Orders;
using Toobit.Platform.Risk;

namespace Toobit.Platform.Infrastructure.Persistence;

// All repositories use IDbContextFactory so they are safe to share from singleton BackgroundServices.
// Each call gets a short-lived DbContext; optimistic concurrency uses the Postgres xmin RowVersion.

public sealed class EfOrderRepository(IDbContextFactory<AppDbContext> factory) : IOrderRepository
{
    public async Task SaveAsync(Order order, IReadOnlyList<OrderEvent> newEvents, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Orders.AsNoTracking().AnyAsync(o => o.Id == order.Id, ct)) db.Orders.Update(order);
        else db.Orders.Add(order);               // first save persists ClientOrderId BEFORE any exchange call
        db.OrderEvents.AddRange(newEvents);      // append-only; events are never updated or deleted
        await db.SaveChangesAsync(ct);           // unique index on ClientOrderId is the DB-level idempotency guard
    }

    public async Task<IReadOnlyList<Order>> GetByStateAsync(OrderState state, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Orders.AsNoTracking().Where(o => o.StateMachineState == state).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Order>> GetActiveAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Orders.AsNoTracking().Where(o => o.StateMachineState == OrderState.Acked
            || o.StateMachineState == OrderState.PartiallyFilled || o.StateMachineState == OrderState.Unknown).ToListAsync(ct);
    }
}

public sealed class EfTrendingRepository(IDbContextFactory<AppDbContext> factory) : ITrendingRepository
{
    public async Task SaveSnapshotAsync(IReadOnlyList<TrendingSymbol> ranked, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.TrendingSymbols.AddRange(ranked);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TrendingSymbol>> GetLatestAsync(int top, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var latest = await db.TrendingSymbols.AsNoTracking().OrderByDescending(t => t.CreatedAt)
            .Select(t => t.SnapshotId).FirstOrDefaultAsync(ct);
        return latest == Guid.Empty ? Array.Empty<TrendingSymbol>()
            : await db.TrendingSymbols.AsNoTracking().Where(t => t.SnapshotId == latest).OrderBy(t => t.Rank).Take(top).ToListAsync(ct);
    }
}

public sealed class EfSignalRepository(IDbContextFactory<AppDbContext> factory) : ISignalRepository
{
    public async Task AddAsync(Signal s, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); db.Signals.Add(s); await db.SaveChangesAsync(ct); }
}

public sealed class EfRiskEventRepository(IDbContextFactory<AppDbContext> factory) : IRiskEventRepository
{
    public async Task AddAsync(RiskEvent e, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); db.RiskEvents.Add(e); await db.SaveChangesAsync(ct); }
}

public sealed class EfReconciliationRepository(IDbContextFactory<AppDbContext> factory) : IReconciliationRepository
{
    public async Task AddAsync(ReconciliationEvent e, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); db.ReconciliationEvents.Add(e); await db.SaveChangesAsync(ct); }
}

/// <summary>Append-only. The runtime DB role is granted INSERT and SELECT only on "AuditLogs" (see RUNBOOKS/db-roles.sql).</summary>
public sealed class EfAuditLog(IDbContextFactory<AppDbContext> factory, IClock clock) : IAuditLog
{
    public async Task AppendAsync(string actor, string action, string detail, string correlationId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = clock.UtcNow;
        db.AuditLogs.Add(new AuditLog { Actor = actor, Action = action, Detail = detail, CorrelationId = correlationId, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync(ct);
    }
}

public sealed class DbKillSwitchStore(IDbContextFactory<AppDbContext> factory, IClock clock) : IKillSwitchStore
{
    public async Task<bool> GetAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.KillSwitch.AsNoTracking().OrderBy(k => k.CreatedAt).FirstOrDefaultAsync(ct))?.IsOn ?? false;
    }

    public async Task SetAsync(bool on, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.KillSwitch.FirstOrDefaultAsync(ct);
        var now = clock.UtcNow;
        if (row is null) db.KillSwitch.Add(new KillSwitchState { IsOn = on, CreatedAt = now, UpdatedAt = now });
        else { row.IsOn = on; row.UpdatedAt = now; }
        await db.SaveChangesAsync(ct);
    }
}

public sealed class DbSymbolPauseStore(IDbContextFactory<AppDbContext> factory, IClock clock) : ISymbolPauseStore
{
    public async Task PauseAsync(string symbol, string reason, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = clock.UtcNow;
        db.SymbolPauses.Add(new SymbolPause { Symbol = symbol, Reason = reason, Active = true, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> IsPausedAsync(string symbol, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.SymbolPauses.AsNoTracking().AnyAsync(p => p.Symbol == symbol && p.Active, ct);
    }

    public async Task<int> ClearAsync(string symbol, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = clock.UtcNow;
        var rows = await db.SymbolPauses.Where(p => p.Symbol == symbol && p.Active).ToListAsync(ct);
        foreach (var r in rows) { r.Active = false; r.UpdatedAt = now; }
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    public async Task<IReadOnlyList<string>> ListPausedAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.SymbolPauses.AsNoTracking().Where(p => p.Active).Select(p => p.Symbol).Distinct().ToListAsync(ct);
    }
}

/// <summary>Flags live in the DB so they flip without redeploy. Absent keys fall back to the safe value (trading OFF).</summary>
public sealed class DbFeatureFlags(IDbContextFactory<AppDbContext> factory) : IFeatureFlags
{
    public async Task<bool> AutomatedTradingEnabledAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.FeatureFlags.AsNoTracking().FirstOrDefaultAsync(f => f.Key == "automated_trading", ct))?.Value == "true";
    }

    public async Task<string> ScannerWeightSetAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.FeatureFlags.AsNoTracking().FirstOrDefaultAsync(f => f.Key == "scanner_weight_set", ct))?.Value ?? "default";
    }
}

/// <summary>
/// Live risk inputs from persisted state.
/// KNOWN GAP (must be closed before live trading): DailyRealizedPnl is NOT computed yet. It needs a fills/PnL source
/// (Toobit trade history, reconciled into a table). Until then the per-day loss rule can never trigger, so the
/// daily-loss limit is NOT enforced in practice. Tracked in README "Known gaps".
/// </summary>
public sealed class DbRiskContextProvider(IDbContextFactory<AppDbContext> factory, ISymbolPauseStore pauses) : IRiskContextProvider
{
    public async Task<RiskContext> GetAsync(string symbol, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var open = await db.Positions.AsNoTracking().Where(p => p.Quantity != 0).ToListAsync(ct);
        var symbolNotional = open.Where(p => p.Symbol == symbol).Sum(p => p.Quantity * p.EntryPrice);
        return new RiskContext(symbolNotional, DailyRealizedPnl: 0m /* NOT IMPLEMENTED, see summary */, open.Count,
            open.Any(p => p.Symbol == symbol), KillSwitchOn: false /* overwritten by executor with a live read */,
            SymbolPaused: await pauses.IsPausedAsync(symbol, ct));
    }
}
