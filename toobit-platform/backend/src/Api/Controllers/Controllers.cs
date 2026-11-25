using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Toobit.Platform.Api.Observability;
using Toobit.Platform.Api.Security;
using Toobit.Platform.Application;
using Toobit.Platform.Domain.Orders;
using Toobit.Platform.Execution;
using Toobit.Platform.Infrastructure.Persistence;
using Toobit.Platform.MarketScanner;

namespace Toobit.Platform.Api.Controllers;

public static class Roles { public const string Admin = "Admin", Trader = "Trader", Viewer = "Viewer"; }

[ApiController, Route("api/scanner"), Authorize]
public sealed class ScannerController(ITrendingRepository repo, ScannerState state, IAuditLog audit) : ControllerBase
{
    public sealed record ScannerStatus(bool Stale, DateTimeOffset? LastSuccess);

    /// <summary>Top-N with the per-metric breakdown and score explanation. Served from the last-known-good snapshot on failure.</summary>
    [HttpGet("top")]
    public async Task<IActionResult> Top([FromQuery] int n = 20, CancellationToken ct = default)
    {
        n = Math.Clamp(n, 1, 100);
        var rows = state.Top.Count > 0 ? state.Top.Take(n).ToList() : (await repo.GetLatestAsync(n, ct)).ToList();
        return Ok(new { stale = state.IsStale, lastSuccess = state.LastSuccess, items = rows });
    }

    [HttpGet("status")]
    public ScannerStatus Status() => new(state.IsStale, state.LastSuccess);

    public sealed record OverrideRequest(string Symbol, bool Pinned);

    /// <summary>Manual override (Trader+). MVP stores pins in memory only; audited. Idempotent per (symbol, state).</summary>
    [HttpPost("overrides"), Authorize(Roles = $"{Roles.Admin},{Roles.Trader}")]
    public async Task<IActionResult> Override([FromBody] OverrideRequest req, CancellationToken ct)
    {
        if (!req.Symbol.EndsWith("-SWAP-USDT", StringComparison.Ordinal)) return BadRequest("symbol must be *-SWAP-USDT");
        state.SetPin(req.Symbol, req.Pinned);
        await audit.AppendAsync(User.Identity?.Name ?? "unknown", "scanner.override", $"{req.Symbol} pinned={req.Pinned}",
            CorrelationMiddleware.Get(HttpContext), ct);
        return NoContent();
    }
}

public sealed record SignalIn(string Symbol, string Action, decimal Confidence, decimal ReferencePrice, string? ModelVersion,
    JsonElement Features, JsonElement Explain);

[ApiController, Route("internal/signals")]
public sealed class SignalIngestController(SignalService signals, IClock clock) : ControllerBase
{
    /// <summary>HMAC-guarded (scope signals:write). Shadow mode: persisted even when automated trading is off.</summary>
    [HttpPost, InternalHmac("signals:write")]
    public async Task<IActionResult> Ingest([FromBody] SignalIn s, CancellationToken ct)
    {
        if (!s.Symbol.EndsWith("-SWAP-USDT", StringComparison.Ordinal) || s.ReferencePrice <= 0)
            return BadRequest("invalid signal");
        var corr = CorrelationMiddleware.Get(HttpContext);
        var (shadow, enqueued) = await signals.HandleAsync(new IncomingSignal(s.Symbol, s.Action, s.Confidence, s.ReferencePrice,
            s.ModelVersion, s.Features.GetRawText(), s.Explain.GetRawText(), corr, clock.UtcNow), ct);
        return Accepted(new { shadow, enqueued, correlationId = corr });
    }
}

[ApiController, Route("api/killswitch"), Authorize]
public sealed class KillSwitchController(IKillSwitch ks) : ControllerBase
{
    public sealed record ToggleRequest(bool On, string Reason, string Confirm);

    [HttpGet] public async Task<object> Get(CancellationToken ct) => new { on = await ks.IsOnAsync(ct) };

    /// <summary>Admin only. Requires an explicit confirm token ("HALT" to engage, "RESUME" to release): the dashboard's confirm step.</summary>
    [HttpPost, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Toggle([FromBody] ToggleRequest r, CancellationToken ct)
    {
        if (r.Confirm != (r.On ? "HALT" : "RESUME")) return BadRequest("confirmation token mismatch");
        await ks.SetAsync(r.On, User.Identity?.Name ?? "unknown", r.Reason, ct);
        return Ok(new { on = await ks.IsOnAsync(ct) });
    }
}

[ApiController, Route("api/positions"), Authorize]
public sealed class PositionsController(IDbContextFactory<AppDbContext> dbf, OrderExecutor executor) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        return Ok(await db.Positions.AsNoTracking().Where(p => p.Quantity != 0).OrderBy(p => p.Symbol).ToListAsync(ct));
    }

    public sealed record CloseRequest(bool Confirm, decimal ReferencePrice);

    /// <summary>Manual close (Trader+): reduce-only MARKET order, requires confirm=true, audited with the acting user.</summary>
    [HttpPost("{symbol}/close"), Authorize(Roles = $"{Roles.Admin},{Roles.Trader}")]
    public async Task<IActionResult> Close(string symbol, [FromBody] CloseRequest req, CancellationToken ct)
    {
        if (!req.Confirm) return BadRequest("confirm required");
        await using var db = await dbf.CreateDbContextAsync(ct);
        var pos = await db.Positions.AsNoTracking().FirstOrDefaultAsync(p => p.Symbol == symbol && p.Quantity != 0, ct);
        if (pos is null) return NotFound();
        var closeSide = pos.Side == "LONG" ? Side.Sell : Side.Buy;
        var order = await executor.ClosePositionAsync(symbol, closeSide, pos.Quantity, req.ReferencePrice,
            User.Identity?.Name ?? "unknown", CorrelationMiddleware.Get(HttpContext), ct);
        return Accepted(new { order.ClientOrderId, state = order.StateMachineState.ToString() });
    }
}

[ApiController, Route("api"), Authorize]
public sealed class ReadModelsController(IDbContextFactory<AppDbContext> dbf) : ControllerBase
{
    [HttpGet("orders")]
    public async Task<IActionResult> Orders([FromQuery] int take = 100, CancellationToken ct = default)
    { await using var db = await dbf.CreateDbContextAsync(ct); return Ok(await db.Orders.AsNoTracking().OrderByDescending(o => o.CreatedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct)); }

    [HttpGet("orders/{id:guid}/events")]
    public async Task<IActionResult> OrderEvents(Guid id, CancellationToken ct)
    { await using var db = await dbf.CreateDbContextAsync(ct); return Ok(await db.OrderEvents.AsNoTracking().Where(e => e.OrderId == id).OrderBy(e => e.OccurredAt).ToListAsync(ct)); }

    [HttpGet("signals")]
    public async Task<IActionResult> Signals([FromQuery] int take = 100, CancellationToken ct = default)
    { await using var db = await dbf.CreateDbContextAsync(ct); return Ok(await db.Signals.AsNoTracking().OrderByDescending(s => s.CreatedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct)); }

    [HttpGet("risk-events")]
    public async Task<IActionResult> RiskEvents([FromQuery] int take = 100, CancellationToken ct = default)
    { await using var db = await dbf.CreateDbContextAsync(ct); return Ok(await db.RiskEvents.AsNoTracking().OrderByDescending(s => s.CreatedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct)); }

    [HttpGet("reconciliation-events")]
    public async Task<IActionResult> Recon([FromQuery] int take = 100, CancellationToken ct = default)
    { await using var db = await dbf.CreateDbContextAsync(ct); return Ok(await db.ReconciliationEvents.AsNoTracking().OrderByDescending(s => s.CreatedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct)); }
}
