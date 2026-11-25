using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Toobit.Platform.Api.Observability;
using Toobit.Platform.Application;
using Toobit.Platform.Domain.Common;
using Toobit.Platform.Infrastructure.Config;
using Toobit.Platform.Infrastructure.Persistence;
using Toobit.Platform.MarketScanner;

namespace Toobit.Platform.Api.Controllers;

[ApiController, Route("api/prices"), Authorize]
public sealed class PricesController(PriceBook prices, ScannerState scanner, IConfiguration cfg) : ControllerBase
{
    /// <summary>Live prices from the Toobit WebSocket plus the data the health banner needs (ws state, staleness, scanner stale).</summary>
    [HttpGet]
    public object Get()
    {
        var threshold = TimeSpan.FromSeconds(cfg.GetValue("Ws:StaleAfterSeconds", 30));
        return new { wsConnected = prices.WsConnected, marketDataStale = prices.IsStale(threshold), lastTickAt = prices.LastTickAt,
                     scannerStale = scanner.IsStale, items = prices.Snapshot() };
    }
}

[ApiController, Route("api/settings"), Authorize]
public sealed class SettingsController(IDbContextFactory<AppDbContext> dbf, IOptionsMonitor<StrategyOptions> strategy,
    IOptionsMonitor<ScannerOptions> scanner, IAuditLog audit, IClock clock) : ControllerBase
{
    public sealed record FlagsDto(bool AutomatedTrading, string ScannerWeightSet);
    private static readonly HashSet<string> WeightSets = new() { "default", "momentum_heavy", "volume_heavy" };

    [HttpGet]
    public async Task<object> Get(CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var flags = await db.FeatureFlags.AsNoTracking().ToDictionaryAsync(f => f.Key, f => f.Value, ct);
        var s = strategy.CurrentValue; var sc = scanner.CurrentValue;
        return new
        {
            flags = new FlagsDto(flags.GetValueOrDefault("automated_trading") == "true", flags.GetValueOrDefault("scanner_weight_set") ?? "default"),
            strategy = new { s.StopLossPct, s.TakeProfitPct, s.OrderNotionalUsdt, s.MaxNotionalPerSymbol, s.MaxDailyLoss, s.MaxOpenPositions },
            scanner = new { sc.IntervalSeconds, sc.TopN, sc.MinQuoteVolume, weightSets = sc.WeightSets.Keys },
            editableAtRuntime = new[] { "automated_trading", "scanner_weight_set" },   // strategy/scanner numbers are config-file driven in the MVP
        };
    }

    /// <summary>Admin only. Feature flags flip without redeploy and every change is audited.</summary>
    [HttpPut("flags"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetFlags([FromBody] FlagsDto dto, CancellationToken ct)
    {
        if (!WeightSets.Contains(dto.ScannerWeightSet) && !scanner.CurrentValue.WeightSets.ContainsKey(dto.ScannerWeightSet)) return BadRequest("unknown weight set");
        await using var db = await dbf.CreateDbContextAsync(ct);
        var now = clock.UtcNow;
        await Upsert(db, "automated_trading", dto.AutomatedTrading ? "true" : "false", now, ct);
        await Upsert(db, "scanner_weight_set", dto.ScannerWeightSet, now, ct);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(User.Identity?.Name ?? "unknown", "config.flags_changed",
            $"automated_trading={dto.AutomatedTrading} scanner_weight_set={dto.ScannerWeightSet}", CorrelationMiddleware.Get(HttpContext), ct);
        return NoContent();
    }

    private static async Task Upsert(AppDbContext db, string key, string value, DateTimeOffset now, CancellationToken ct)
    {
        var row = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == key, ct);
        if (row is null) db.FeatureFlags.Add(new FeatureFlag { Key = key, Value = value, CreatedAt = now, UpdatedAt = now });
        else { row.Value = value; row.UpdatedAt = now; }
    }
}

[ApiController, Route("api/pauses"), Authorize]
public sealed class PausesController(ISymbolPauseStore pauses, IAuditLog audit) : ControllerBase
{
    /// <summary>Symbols paused by reconciliation. Shown in Risk &amp; Logs.</summary>
    [HttpGet] public async Task<IReadOnlyList<string>> List(CancellationToken ct) => await pauses.ListPausedAsync(ct);

    /// <summary>Admin only, audited: operator clears a pause after investigating the divergence (RUNBOOKS/incident-response.md).</summary>
    [HttpDelete("{symbol}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Clear(string symbol, CancellationToken ct)
    {
        var n = await pauses.ClearAsync(symbol, ct);
        await audit.AppendAsync(User.Identity?.Name ?? "unknown", "reconcile.pause_cleared", $"{symbol} ({n} pause rows)", CorrelationMiddleware.Get(HttpContext), ct);
        return n == 0 ? NotFound() : NoContent();
    }
}
