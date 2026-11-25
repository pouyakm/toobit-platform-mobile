using Toobit.Platform.Domain.Common;
using Toobit.Platform.Domain.Orders;
using Toobit.Platform.Risk;
using Xunit;

namespace Toobit.UnitTests;

public class OrderStateMachineTests
{
    [Theory]
    [InlineData(OrderState.Received, OrderState.Validated)]
    [InlineData(OrderState.Validated, OrderState.RiskChecked)]
    [InlineData(OrderState.RiskChecked, OrderState.Submitted)]
    [InlineData(OrderState.Submitted, OrderState.Acked)]
    [InlineData(OrderState.Submitted, OrderState.Unknown)]
    [InlineData(OrderState.Unknown, OrderState.Filled)]
    [InlineData(OrderState.Unknown, OrderState.Rejected)]
    [InlineData(OrderState.Acked, OrderState.PartiallyFilled)]
    [InlineData(OrderState.PartiallyFilled, OrderState.Filled)]
    public void Legal_transitions_are_allowed(OrderState from, OrderState to) => Assert.True(OrderStateMachine.CanTransition(from, to));

    [Theory]
    [InlineData(OrderState.Received, OrderState.Submitted)]    // cannot skip risk
    [InlineData(OrderState.Validated, OrderState.Submitted)]   // cannot skip risk
    [InlineData(OrderState.Filled, OrderState.Canceled)]
    [InlineData(OrderState.Rejected, OrderState.Acked)]
    [InlineData(OrderState.Acked, OrderState.Unknown)]
    public void Illegal_transitions_are_blocked(OrderState from, OrderState to) => Assert.False(OrderStateMachine.CanTransition(from, to));

    [Fact]
    public void Terminal_states_have_no_exits()
    {
        foreach (var s in new[] { OrderState.Filled, OrderState.Canceled, OrderState.Rejected })
        { Assert.True(OrderStateMachine.IsTerminal(s)); Assert.Empty(OrderStateMachine.NextStates(s)); }
        Assert.False(OrderStateMachine.IsTerminal(OrderState.Unknown));   // Unknown is NOT terminal: it must be reconciled
    }

    [Fact]
    public void Order_throws_on_illegal_transition_and_records_event_on_legal_one()
    {
        var o = Order.Create("BTC-SWAP-USDT", Side.Buy, 1, 100, 99, 102, "c", DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOrderTransitionException>(() => o.TransitionTo(OrderState.Submitted, "skip", DateTimeOffset.UtcNow));
        var ev = o.TransitionTo(OrderState.Validated, "ok", DateTimeOffset.UtcNow);
        Assert.Equal((OrderState.Received, OrderState.Validated), (ev.From, ev.To));
        Assert.Equal(OrderState.Validated, o.StateMachineState);
    }
}

public class Uuid7Tests
{
    [Fact]
    public void Is_version7_variant_rfc_and_time_ordered()
    {
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var a = Uuid7.NewGuid(t).ToString();
        var b = Uuid7.NewGuid(t.AddMilliseconds(5)).ToString();
        Assert.Equal('7', a[14]);
        Assert.Contains(a[19], "89ab");
        Assert.True(string.CompareOrdinal(a, b) < 0);      // lexicographic order follows time
    }
}

public class RiskEngineTests
{
    private static readonly RiskEngine Engine = new();
    private static readonly RiskLimits Limits = new(1_000m, 200m, 2);
    private static RiskContext Ctx(decimal notional = 0, decimal pnl = 0, int open = 0, bool has = false, bool kill = false, bool paused = false) =>
        new(notional, pnl, open, has, kill, paused);
    private static OrderIntent Buy(decimal qty = 0.01m) => new("BTC-SWAP-USDT", Side.Buy, qty, 60_000m, 59_000m, 62_000m);

    [Fact] public void Approves_a_good_order() => Assert.True(Engine.Evaluate(Buy(), Ctx(), Limits).Approved);

    [Fact] public void Kill_switch_rejects_first()
    {
        var d = Engine.Evaluate(Buy(), Ctx(kill: true, paused: true), Limits);
        Assert.Equal(RiskRule.KillSwitch, d.Rule);   // precedence is part of the contract
    }

    [Fact] public void Paused_symbol_is_rejected() => Assert.Equal(RiskRule.SymbolPaused, Engine.Evaluate(Buy(), Ctx(paused: true), Limits).Rule);

    [Theory]
    [InlineData(0, 62_000)]            // no SL
    [InlineData(61_000, 62_000)]       // SL above entry on a long
    [InlineData(59_000, 59_500)]       // TP below entry on a long
    public void Long_requires_sl_below_and_tp_above_entry(decimal sl, decimal tp)
    {
        var i = new OrderIntent("BTC-SWAP-USDT", Side.Buy, 0.01m, 60_000m, sl, tp);
        Assert.Equal(RiskRule.MissingProtection, Engine.Evaluate(i, Ctx(), Limits).Rule);
    }

    [Fact] public void Short_requires_sl_above_and_tp_below_entry()
    {
        Assert.True(Engine.Evaluate(new("BTC-SWAP-USDT", Side.Sell, 0.01m, 60_000m, 61_000m, 58_000m), Ctx(), Limits).Approved);
        Assert.Equal(RiskRule.MissingProtection, Engine.Evaluate(new("BTC-SWAP-USDT", Side.Sell, 0.01m, 60_000m, 59_000m, 58_000m), Ctx(), Limits).Rule);
    }

    [Fact] public void Per_symbol_notional_cap_counts_existing_exposure()
    {
        Assert.True(Engine.Evaluate(Buy(0.01m), Ctx(notional: 300), Limits).Approved);                 // 300 + 600 <= 1000
        Assert.Equal(RiskRule.MaxNotional, Engine.Evaluate(Buy(0.01m), Ctx(notional: 500), Limits).Rule); // 500 + 600 > 1000
    }

    [Fact] public void Daily_loss_limit_blocks_at_and_beyond_limit()
    {
        Assert.True(Engine.Evaluate(Buy(), Ctx(pnl: -199), Limits).Approved);
        Assert.Equal(RiskRule.DailyLoss, Engine.Evaluate(Buy(), Ctx(pnl: -200), Limits).Rule);
    }

    [Fact] public void Max_open_positions_applies_to_new_symbols_only()
    {
        Assert.Equal(RiskRule.MaxOpenPositions, Engine.Evaluate(Buy(), Ctx(open: 2, has: false), Limits).Rule);
        Assert.True(Engine.Evaluate(Buy(), Ctx(open: 2, has: true), Limits).Approved);   // adding to an existing symbol is allowed
    }

    [Fact] public void Non_positive_quantity_or_price_is_invalid() =>
        Assert.Equal(RiskRule.InvalidIntent, Engine.Evaluate(Buy(0m), Ctx(), Limits).Rule);
}
