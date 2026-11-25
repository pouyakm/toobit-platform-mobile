using Toobit.Platform.Domain.Orders;

namespace Toobit.Platform.Risk;

public sealed record OrderIntent(string Symbol, Side Side, decimal Quantity, decimal Price, decimal StopLoss, decimal TakeProfit);

/// <summary>Limits come from StrategyConfig (DB), never literals. Validated at load.</summary>
public sealed record RiskLimits(decimal MaxNotionalPerSymbol, decimal MaxDailyLoss, int MaxOpenPositions);

public sealed record RiskContext(decimal CurrentSymbolNotional, decimal DailyRealizedPnl, int OpenPositions,
    bool SymbolHasOpenPosition, bool KillSwitchOn, bool SymbolPaused);

public enum RiskRule { KillSwitch, SymbolPaused, MissingProtection, InvalidIntent, MaxNotional, DailyLoss, MaxOpenPositions }

public sealed record RiskDecision(bool Approved, RiskRule? Rule, string Reason)
{
    public static RiskDecision Ok { get; } = new(true, null, "approved");
    public static RiskDecision Reject(RiskRule rule, string reason) => new(false, rule, reason);
}

/// <summary>Pure, deterministic risk rules. First failing rule wins; order is part of the contract (tested).</summary>
public sealed class RiskEngine
{
    public RiskDecision Evaluate(OrderIntent i, RiskContext c, RiskLimits l)
    {
        if (c.KillSwitchOn) return RiskDecision.Reject(RiskRule.KillSwitch, "kill switch is ON");
        if (c.SymbolPaused) return RiskDecision.Reject(RiskRule.SymbolPaused, $"{i.Symbol} paused by reconciliation");
        if (i.Quantity <= 0 || i.Price <= 0)
            return RiskDecision.Reject(RiskRule.InvalidIntent, "quantity and price must be positive");

        // SL/TP are mandatory at entry and must sit on the correct side of the entry price.
        bool slOk = i.Side == Side.Buy ? i.StopLoss > 0 && i.StopLoss < i.Price : i.StopLoss > i.Price;
        bool tpOk = i.Side == Side.Buy ? i.TakeProfit > i.Price : i.TakeProfit > 0 && i.TakeProfit < i.Price;
        if (!slOk || !tpOk)
            return RiskDecision.Reject(RiskRule.MissingProtection, "SL/TP missing or on the wrong side of entry");

        if (c.DailyRealizedPnl <= -l.MaxDailyLoss)
            return RiskDecision.Reject(RiskRule.DailyLoss, $"daily loss limit {l.MaxDailyLoss} reached");

        var notional = c.CurrentSymbolNotional + i.Quantity * i.Price;
        if (notional > l.MaxNotionalPerSymbol)
            return RiskDecision.Reject(RiskRule.MaxNotional, $"notional {notional} exceeds {l.MaxNotionalPerSymbol}");

        if (!c.SymbolHasOpenPosition && c.OpenPositions >= l.MaxOpenPositions)
            return RiskDecision.Reject(RiskRule.MaxOpenPositions, $"max open positions {l.MaxOpenPositions} reached");

        return RiskDecision.Ok;
    }
}
