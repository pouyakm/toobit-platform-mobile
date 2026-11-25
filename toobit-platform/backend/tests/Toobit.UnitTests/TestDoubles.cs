using Microsoft.Extensions.Logging.Abstractions;
using Toobit.Platform.Application;
using Toobit.Platform.Domain;
using Toobit.Platform.Domain.Orders;
using Toobit.Platform.Execution;
using Toobit.Platform.Risk;

namespace Toobit.UnitTests;

public sealed class FakeClock(DateTimeOffset? start = null) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = start ?? new DateTimeOffset(2026, 11, 18, 8, 0, 0, TimeSpan.Zero);
}

public sealed class FakeKillSwitch : IKillSwitch
{
    public bool On { get; set; }
    public List<string> Sets { get; } = new();
    public Task<bool> IsOnAsync(CancellationToken ct) => Task.FromResult(On);
    public Task SetAsync(bool on, string actor, string reason, CancellationToken ct) { On = on; Sets.Add($"{on}:{actor}:{reason}"); return Task.CompletedTask; }
}

public sealed class InMemoryOrderRepo : IOrderRepository
{
    public List<Order> Orders { get; } = new();
    public List<OrderEvent> Events { get; } = new();
    public List<Guid> SavedBeforeSubmit { get; } = new();
    public Task SaveAsync(Order order, IReadOnlyList<OrderEvent> ev, CancellationToken ct)
    { if (!Orders.Contains(order)) Orders.Add(order); Events.AddRange(ev); return Task.CompletedTask; }
    public Task<IReadOnlyList<Order>> GetByStateAsync(OrderState s, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Order>>(Orders.Where(o => o.StateMachineState == s).ToList());
    public Task<IReadOnlyList<Order>> GetActiveAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Order>>(Orders.Where(o => !OrderStateMachine.IsTerminal(o.StateMachineState)).ToList());
}

/// <summary>Scripted exchange: each submit pops the next result; records every ClientOrderId it sees and whether the order was already persisted.</summary>
public sealed class ScriptedExchange(InMemoryOrderRepo repo) : ITradingExchange
{
    public Queue<SubmitResult> Script { get; } = new();
    public List<Guid> SeenClientIds { get; } = new();
    public bool AllPersistedBeforeCall { get; private set; } = true;
    public Dictionary<Guid, QueryResult> Queries { get; } = new();
    public List<ExchangeOrder> Open { get; set; } = new();

    public Task<SubmitResult> SubmitOrderAsync(Order o, CancellationToken ct)
    {
        SeenClientIds.Add(o.ClientOrderId);
        if (!repo.Orders.Contains(o)) AllPersistedBeforeCall = false;
        return Task.FromResult(Script.Count > 0 ? Script.Dequeue() : new SubmitResult(SubmitOutcome.Acked, "T-1"));
    }
    public Task<QueryResult> QueryOrderAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Queries.TryGetValue(id, out var q) ? q : new QueryResult(QueryStatus.Error));
    public Task<IReadOnlyList<ExchangeOrder>> GetOpenOrdersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ExchangeOrder>>(Open);
    public Task<IReadOnlyList<ExchangePosition>> GetPositionsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ExchangePosition>>(Array.Empty<ExchangePosition>());
}

public sealed class FakeAudit : IAuditLog
{
    public List<string> Actions { get; } = new();
    public Task AppendAsync(string actor, string action, string detail, string corr, CancellationToken ct) { Actions.Add(action); return Task.CompletedTask; }
}
public sealed class FakeRiskEvents : IRiskEventRepository
{
    public List<RiskEvent> Items { get; } = new();
    public Task AddAsync(RiskEvent e, CancellationToken ct) { Items.Add(e); return Task.CompletedTask; }
}
public sealed class FakeRiskContext : IRiskContextProvider
{
    public Task<RiskContext> GetAsync(string symbol, CancellationToken ct) =>
        Task.FromResult(new RiskContext(0, 0, 0, false, false, false));
}
public sealed class FakeStrategy : IStrategyConfigProvider
{
    public Task<StrategySettings> GetAsync(CancellationToken ct) => Task.FromResult(new StrategySettings(0.01m, 0.02m, 100m, true, TimeSpan.FromSeconds(10)));
    public Task<RiskLimits> GetRiskLimitsAsync(CancellationToken ct) => Task.FromResult(new RiskLimits(10_000m, 500m, 3));
}
public sealed class FakePauses : ISymbolPauseStore
{
    public HashSet<string> Paused { get; } = new();
    public Task PauseAsync(string s, string r, CancellationToken ct) { Paused.Add(s); return Task.CompletedTask; }
    public Task<bool> IsPausedAsync(string s, CancellationToken ct) => Task.FromResult(Paused.Contains(s));
    public Task<int> ClearAsync(string s, CancellationToken ct) => Task.FromResult(Paused.Remove(s) ? 1 : 0);
    public Task<IReadOnlyList<string>> ListPausedAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(Paused.ToList());
}
public sealed class FakeReconEvents : IReconciliationRepository
{
    public List<ReconciliationEvent> Items { get; } = new();
    public Task AddAsync(ReconciliationEvent e, CancellationToken ct) { Items.Add(e); return Task.CompletedTask; }
}

public sealed class Harness
{
    public FakeClock Clock { get; } = new();
    public InMemoryOrderRepo Orders { get; } = new();
    public ScriptedExchange Exchange { get; }
    public FakeKillSwitch Kill { get; } = new();
    public FakeAudit Audit { get; } = new();
    public FakeRiskEvents RiskEvents { get; } = new();
    public List<TimeSpan> Delays { get; } = new();
    public OrderExecutor Executor { get; }

    public Harness()
    {
        Exchange = new ScriptedExchange(Orders);
        Executor = new OrderExecutor(Exchange, Orders, Kill, new FakeRiskContext(), new FakeStrategy(), RiskEvents, Audit, Clock,
            new RiskEngine(), new ExecutorOptions { MaxRateLimitRetries = 3 }, NullLogger<OrderExecutor>.Instance,
            delay: (t, _) => { Delays.Add(t); return Task.CompletedTask; }, rng: () => 0.5);
    }

    public static OrderIntent GoodBuy() => new("BTC-SWAP-USDT", Side.Buy, 0.01m, 60_000m, 59_400m, 61_200m);
}
