using Microsoft.Extensions.Logging.Abstractions;
using Toobit.Platform.Application;
using Toobit.Platform.Domain.Orders;
using Toobit.Platform.Execution;
using Toobit.Platform.Infrastructure.Security;
using Xunit;

namespace Toobit.UnitTests;

public class OrderExecutorTests
{
    [Fact]
    public async Task Happy_path_leaves_full_state_machine_trail_in_order_events()   // acceptance #5
    {
        var h = new Harness();
        var o = await h.Executor.ExecuteAsync(Harness.GoodBuy(), "corr-1", default);

        Assert.Equal(OrderState.Acked, o.StateMachineState);
        Assert.Equal(new[] { OrderState.Validated, OrderState.RiskChecked, OrderState.Submitted, OrderState.Acked },
            h.Orders.Events.Select(e => e.To));
        Assert.Equal("T-1", o.ToobitOrderId);
        Assert.All(h.Orders.Events, e => Assert.Equal("corr-1", e.CorrelationId));
        Assert.Contains("order.Acked", h.Audit.Actions);                 // lifecycle is audited
        Assert.True(o.StopLoss > 0 && o.TakeProfit > 0);                 // SL/TP attached at entry
    }

    [Fact]
    public async Task Order_and_ClientOrderId_are_persisted_before_the_exchange_call()
    {
        var h = new Harness();
        await h.Executor.ExecuteAsync(Harness.GoodBuy(), "c", default);
        Assert.True(h.Exchange.AllPersistedBeforeCall);
    }

    [Fact]
    public async Task Kill_switch_halts_submission_and_exchange_is_never_called()   // acceptance #8
    {
        var h = new Harness { Kill = { On = true } };
        var o = await h.Executor.ExecuteAsync(Harness.GoodBuy(), "c", default);

        Assert.Equal(OrderState.Rejected, o.StateMachineState);
        Assert.Empty(h.Exchange.SeenClientIds);
        Assert.Single(h.RiskEvents.Items);
        Assert.Equal("KillSwitch", h.RiskEvents.Items[0].Rule);
    }

    [Fact]
    public async Task Kill_switch_flipped_between_risk_check_and_submit_still_blocks()
    {
        var h = new Harness();
        var flipping = new FlipAfterFirstRead(h.Kill);
        var ex = new OrderExecutor(h.Exchange, h.Orders, flipping, new FakeRiskContext(), new FakeStrategy(), h.RiskEvents, h.Audit,
            h.Clock, new Toobit.Platform.Risk.RiskEngine(), new ExecutorOptions(), NullLogger<OrderExecutor>.Instance);
        var o = await ex.ExecuteAsync(Harness.GoodBuy(), "c", default);
        Assert.Equal(OrderState.Rejected, o.StateMachineState);
        Assert.Empty(h.Exchange.SeenClientIds);
    }

    [Fact]
    public async Task Rate_limit_429_backs_off_with_jitter_and_retries_the_SAME_ClientOrderId()   // acceptance #6
    {
        var h = new Harness();
        h.Exchange.Script.Enqueue(new SubmitResult(SubmitOutcome.RateLimited, RetryAfter: TimeSpan.FromSeconds(2)));
        h.Exchange.Script.Enqueue(new SubmitResult(SubmitOutcome.RateLimited));
        h.Exchange.Script.Enqueue(new SubmitResult(SubmitOutcome.Acked, "T-9"));

        var o = await h.Executor.ExecuteAsync(Harness.GoodBuy(), "c", default);

        Assert.Equal(OrderState.Acked, o.StateMachineState);
        Assert.Equal(3, h.Exchange.SeenClientIds.Count);
        Assert.Single(h.Exchange.SeenClientIds.Distinct());              // idempotent: one id across retries
        Assert.Equal(2, h.Delays.Count);
        Assert.True(h.Delays[0] >= TimeSpan.FromSeconds(2));             // honors the exchange reset hint
        Assert.True(h.Delays[1] > h.Delays[0] - TimeSpan.FromSeconds(2) || h.Delays[1] >= TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task Persistent_429_gives_up_after_max_retries_without_crashing_and_rejects()
    {
        var h = new Harness();
        for (int i = 0; i < 10; i++) h.Exchange.Script.Enqueue(new SubmitResult(SubmitOutcome.RateLimited));
        var o = await h.Executor.ExecuteAsync(Harness.GoodBuy(), "c", default);
        Assert.Equal(OrderState.Rejected, o.StateMachineState);
        Assert.Equal(4, h.Exchange.SeenClientIds.Count);                 // 1 try + MaxRateLimitRetries(3)
    }

    [Fact]
    public async Task Ambiguous_5xx_or_timeout_becomes_Unknown_not_Rejected_and_is_not_retried()   // acceptance #6
    {
        var h = new Harness();
        h.Exchange.Script.Enqueue(new SubmitResult(SubmitOutcome.Ambiguous, Error: "HTTP 503"));
        var o = await h.Executor.ExecuteAsync(Harness.GoodBuy(), "c", default);
        Assert.Equal(OrderState.Unknown, o.StateMachineState);
        Assert.Single(h.Exchange.SeenClientIds);                         // never blindly re-POSTed
        Assert.Empty(h.Delays);
    }

    [Fact]
    public async Task HTTP_418_ban_trips_the_kill_switch_and_stops()
    {
        var h = new Harness();
        h.Exchange.Script.Enqueue(new SubmitResult(SubmitOutcome.Banned));
        var o = await h.Executor.ExecuteAsync(Harness.GoodBuy(), "c", default);
        Assert.Equal(OrderState.Rejected, o.StateMachineState);
        Assert.True(h.Kill.On);
        Assert.Single(h.Exchange.SeenClientIds);
    }

    [Fact]
    public async Task Risk_rejection_records_a_RiskEvent_and_never_reaches_the_exchange()
    {
        var h = new Harness();
        var bad = Harness.GoodBuy() with { StopLoss = 0 };
        var o = await h.Executor.ExecuteAsync(bad, "c", default);
        Assert.Equal(OrderState.Rejected, o.StateMachineState);
        Assert.Empty(h.Exchange.SeenClientIds);
        Assert.Equal("MissingProtection", h.RiskEvents.Items.Single().Rule);
    }

    [Fact]
    public async Task Manual_close_is_reduce_only_and_audited_with_the_actor()   // acceptance #7
    {
        var h = new Harness();
        var o = await h.Executor.ClosePositionAsync("BTC-SWAP-USDT", Side.Sell, 0.01m, 60_000m, "alice", "c", default);
        Assert.True(o.ReduceOnly);
        Assert.Equal(OrderState.Acked, o.StateMachineState);
        Assert.Contains("position.manual_close", h.Audit.Actions);
    }

    private sealed class FlipAfterFirstRead(FakeKillSwitch inner) : IKillSwitch
    {
        private int _reads;
        public Task<bool> IsOnAsync(CancellationToken ct) { _reads++; if (_reads >= 2) inner.On = true; return inner.IsOnAsync(ct); }
        public Task SetAsync(bool on, string a, string r, CancellationToken ct) => inner.SetAsync(on, a, r, ct);
    }
}

public class ReconciliationTests
{
    private static (ReconciliationService svc, Harness h, FakePauses pauses, FakeReconEvents ev) Build(ReconciliationOptions? o = null)
    {
        var h = new Harness(); var p = new FakePauses(); var ev = new FakeReconEvents();
        var svc = new ReconciliationService(h.Exchange, h.Orders, ev, p, h.Audit, h.Clock, o ?? new ReconciliationOptions { UnknownGraceSeconds = 20 },
            NullLogger<ReconciliationService>.Instance);
        return (svc, h, p, ev);
    }

    private static async Task<Order> MakeUnknown(Harness h)
    {
        h.Exchange.Script.Enqueue(new SubmitResult(SubmitOutcome.Ambiguous));
        return await h.Executor.ExecuteAsync(Harness.GoodBuy(), "c", default);
    }

    [Fact]
    public async Task Unknown_order_found_on_exchange_is_resolved_to_the_real_state()   // acceptance #6
    {
        var (svc, h, _, ev) = Build();
        var o = await MakeUnknown(h);
        h.Exchange.Queries[o.ClientOrderId] = new QueryResult(QueryStatus.Found, new ExchangeOrder("T-77", o.ClientOrderId, OrderState.Filled));

        await svc.ReconcileOnceAsync(default);

        Assert.Equal(OrderState.Filled, o.StateMachineState);
        Assert.Equal("T-77", o.ToobitOrderId);
        Assert.Equal("UnknownResolved", ev.Items.Single().Kind);
    }

    [Fact]
    public async Task Unknown_order_not_found_is_only_rejected_after_the_grace_period()
    {
        var (svc, h, _, _) = Build();
        var o = await MakeUnknown(h);
        h.Exchange.Queries[o.ClientOrderId] = new QueryResult(QueryStatus.NotFound);

        await svc.ReconcileOnceAsync(default);
        Assert.Equal(OrderState.Unknown, o.StateMachineState);           // too early to trust "absent"

        h.Clock.UtcNow += TimeSpan.FromSeconds(30);
        await svc.ReconcileOnceAsync(default);
        Assert.Equal(OrderState.Rejected, o.StateMachineState);
    }

    [Fact]
    public async Task Query_errors_keep_the_order_Unknown_and_count_retries()
    {
        var (svc, h, _, _) = Build();
        var o = await MakeUnknown(h);
        await svc.ReconcileOnceAsync(default);                           // no scripted query => QueryStatus.Error
        Assert.Equal(OrderState.Unknown, o.StateMachineState);
        Assert.Equal(1, o.UnknownRetries);
    }

    [Fact]
    public async Task Unresolvable_unknown_pauses_the_symbol_after_max_retries()
    {
        var (svc, h, p, ev) = Build(new ReconciliationOptions { MaxUnknownRetries = 2, UnknownGraceSeconds = 20 });
        await MakeUnknown(h);
        await svc.ReconcileOnceAsync(default);
        await svc.ReconcileOnceAsync(default);
        Assert.Contains("BTC-SWAP-USDT", p.Paused);
        Assert.Contains(ev.Items, e => e.Kind == "OrderDivergence" && e.PausedSymbol);
    }

    [Fact]
    public async Task Simulated_divergence_open_locally_but_gone_on_exchange_is_detected_and_pauses_symbol()   // acceptance #9
    {
        var (svc, h, p, ev) = Build();
        var o = await h.Executor.ExecuteAsync(Harness.GoodBuy(), "c", default);     // Acked locally
        h.Exchange.Open = new();                                                    // exchange shows nothing open
        h.Exchange.Queries[o.ClientOrderId] = new QueryResult(QueryStatus.Error);   // cannot explain it

        await svc.ReconcileOnceAsync(default);

        Assert.Contains("BTC-SWAP-USDT", p.Paused);
        Assert.Contains(ev.Items, e => e.Kind == "OrderDivergence");
        Assert.Contains("reconcile.divergence", h.Audit.Actions);
    }

    [Fact]
    public async Task Order_that_filled_on_exchange_is_reconciled_without_a_false_alarm()
    {
        var (svc, h, p, ev) = Build();
        var o = await h.Executor.ExecuteAsync(Harness.GoodBuy(), "c", default);
        h.Exchange.Open = new();
        h.Exchange.Queries[o.ClientOrderId] = new QueryResult(QueryStatus.Found, new ExchangeOrder("T-1", o.ClientOrderId, OrderState.Filled));
        await svc.ReconcileOnceAsync(default);
        Assert.Equal(OrderState.Filled, o.StateMachineState);
        Assert.Empty(p.Paused); Assert.Empty(ev.Items);
    }

    [Fact]
    public async Task Clean_reconciliation_logs_nothing_and_pauses_nothing()   // "runs and logs cleanly"
    {
        var (svc, h, p, ev) = Build();
        var o = await h.Executor.ExecuteAsync(Harness.GoodBuy(), "c", default);
        h.Exchange.Open = new() { new ExchangeOrder("T-1", o.ClientOrderId, OrderState.Acked) };
        await svc.ReconcileOnceAsync(default);
        Assert.Empty(p.Paused); Assert.Empty(ev.Items);
    }
}

public class KillSwitchAndBackoffTests
{
    private sealed class Store(Func<bool> get) : IKillSwitchStore
    {
        public bool? Written;
        public Task<bool> GetAsync(CancellationToken ct) => Task.FromResult(get());
        public Task SetAsync(bool on, CancellationToken ct) { Written = on; return Task.CompletedTask; }
    }
    private static Store Down() => new(() => throw new InvalidOperationException("down"));
    private static FailClosedKillSwitch Ks(IKillSwitchStore fast, IKillSwitchStore durable) =>
        new(fast, durable, new FakeAudit(), NullLogger<FailClosedKillSwitch>.Instance);

    [Fact] public async Task Reads_fast_store_when_available() => Assert.True(await Ks(new Store(() => true), new Store(() => false)).IsOnAsync(default));

    [Fact] public async Task Falls_back_to_db_when_redis_is_down() => Assert.True(await Ks(Down(), new Store(() => true)).IsOnAsync(default));

    [Fact] public async Task Fails_CLOSED_when_both_stores_are_down() => Assert.True(await Ks(Down(), Down()).IsOnAsync(default));

    [Fact]
    public async Task Set_writes_db_even_if_redis_is_down_and_is_audited()
    {
        var db = new Store(() => false); var audit = new FakeAudit();
        var ks = new FailClosedKillSwitch(Down(), db, audit, NullLogger<FailClosedKillSwitch>.Instance);
        await ks.SetAsync(true, "alice", "drill", default);
        Assert.True(db.Written);
        Assert.Contains("killswitch.on", audit.Actions);
    }

    [Fact]
    public void Backoff_is_exponential_jittered_capped_and_respects_retry_after()
    {
        var b = TimeSpan.FromMilliseconds(100); var max = TimeSpan.FromSeconds(1);
        Assert.Equal(TimeSpan.FromMilliseconds(75), Backoff.Compute(1, b, max, null, () => 0.5));      // 100 * (0.5 + 0.25)
        Assert.Equal(TimeSpan.FromMilliseconds(150), Backoff.Compute(2, b, max, null, () => 0.5));
        Assert.Equal(TimeSpan.FromMilliseconds(750), Backoff.Compute(20, b, max, null, () => 0.5));   // capped at max
        Assert.True(Backoff.Compute(1, b, max, null, () => 0.0) >= TimeSpan.FromMilliseconds(50));     // jitter floor
        Assert.True(Backoff.Compute(1, b, max, null, () => 1.0) <= b);                                   // jitter ceiling
        Assert.Equal(TimeSpan.FromSeconds(5), Backoff.Compute(1, b, max, TimeSpan.FromSeconds(5), () => 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Backoff.Compute(0, b, max, null, () => 0.5));
    }
}

public class SignalServiceTests
{
    private sealed class Sigs : Toobit.Platform.Application.ISignalRepository
    {
        public List<Toobit.Platform.Domain.Signal> Items { get; } = new();
        public Task AddAsync(Toobit.Platform.Domain.Signal s, CancellationToken ct) { Items.Add(s); return Task.CompletedTask; }
    }
    private sealed class Flags(bool on) : IFeatureFlags
    {
        public Task<bool> AutomatedTradingEnabledAsync(CancellationToken ct) => Task.FromResult(on);
        public Task<string> ScannerWeightSetAsync(CancellationToken ct) => Task.FromResult("default");
    }
    private static IncomingSignal Sig(string action = "enter_long") =>
        new("BTC-SWAP-USDT", action, 0.8m, 60_000m, null, "{}", "{}", "corr", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Shadow_mode_logs_the_signal_but_enqueues_no_order()
    {
        var sigs = new Sigs(); var q = new OrderQueue(8, NullLogger<OrderQueue>.Instance);
        var svc = new SignalService(sigs, new Flags(false), new FakeStrategy(), new FakePauses(), q, new FakeClock());
        var (shadow, enqueued) = await svc.HandleAsync(Sig(), default);
        Assert.True(shadow); Assert.False(enqueued);
        Assert.True(sigs.Items.Single().Shadow);
        Assert.False(q.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Trading_on_enqueues_intent_with_sl_tp_derived_from_config()
    {
        var q = new OrderQueue(8, NullLogger<OrderQueue>.Instance);
        var svc = new SignalService(new Sigs(), new Flags(true), new FakeStrategy(), new FakePauses(), q, new FakeClock());
        var (_, enqueued) = await svc.HandleAsync(Sig(), default);
        Assert.True(enqueued);
        Assert.True(q.Reader.TryRead(out var item));
        Assert.Equal(60_000m * 0.99m, item!.Intent.StopLoss);
        Assert.Equal(60_000m * 1.02m, item.Intent.TakeProfit);
    }

    [Fact]
    public async Task Paused_symbol_and_non_entry_actions_are_not_enqueued()
    {
        var pauses = new FakePauses(); pauses.Paused.Add("BTC-SWAP-USDT");
        var q = new OrderQueue(8, NullLogger<OrderQueue>.Instance);
        var svc = new SignalService(new Sigs(), new Flags(true), new FakeStrategy(), pauses, q, new FakeClock());
        Assert.False((await svc.HandleAsync(Sig(), default)).Enqueued);
        Assert.False((await new SignalService(new Sigs(), new Flags(true), new FakeStrategy(), new FakePauses(), q, new FakeClock()).HandleAsync(Sig("hold"), default)).Enqueued);
    }

    [Fact]
    public void Full_queue_drops_with_alert_and_never_blocks()   // backpressure
    {
        var q = new OrderQueue(1, NullLogger<OrderQueue>.Instance);
        var item = new QueuedIntent(Harness.GoodBuy(), "c", DateTimeOffset.UtcNow);
        Assert.True(q.TryEnqueue(item));
        Assert.False(q.TryEnqueue(item));        // returns immediately, item dropped
    }
}
