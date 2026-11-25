using Microsoft.Extensions.Options;
using Toobit.Platform.Application;
using Toobit.Platform.Risk;

namespace Toobit.Platform.Infrastructure.Config;

public sealed class StrategyOptions
{
    public decimal StopLossPct { get; set; }
    public decimal TakeProfitPct { get; set; }
    public decimal OrderNotionalUsdt { get; set; }
    public bool LimitFirst { get; set; } = true;
    public int LimitTimeoutSeconds { get; set; } = 10;
    public decimal MaxNotionalPerSymbol { get; set; }
    public decimal MaxDailyLoss { get; set; }
    public int MaxOpenPositions { get; set; }

    /// <summary>No numeric defaults for risk values: a missing limit fails startup instead of silently allowing trades.</summary>
    public static bool IsValid(StrategyOptions o) =>
        o.StopLossPct > 0 && o.TakeProfitPct > 0 && o.OrderNotionalUsdt > 0 &&
        o.MaxNotionalPerSymbol > 0 && o.MaxDailyLoss > 0 && o.MaxOpenPositions > 0;
}

/// <summary>MVP: hot-reloadable options. The StrategyConfig table + editor UI is a follow-up (see README Known gaps).</summary>
public sealed class OptionsStrategyConfigProvider(IOptionsMonitor<StrategyOptions> o) : IStrategyConfigProvider
{
    public Task<StrategySettings> GetAsync(CancellationToken ct) =>
        Task.FromResult(new StrategySettings(o.CurrentValue.StopLossPct, o.CurrentValue.TakeProfitPct,
            o.CurrentValue.OrderNotionalUsdt, o.CurrentValue.LimitFirst, TimeSpan.FromSeconds(o.CurrentValue.LimitTimeoutSeconds)));

    public Task<RiskLimits> GetRiskLimitsAsync(CancellationToken ct) =>
        Task.FromResult(new RiskLimits(o.CurrentValue.MaxNotionalPerSymbol, o.CurrentValue.MaxDailyLoss, o.CurrentValue.MaxOpenPositions));
}
