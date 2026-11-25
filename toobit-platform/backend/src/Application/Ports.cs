using Toobit.Platform.Domain;
using Toobit.Platform.Domain.Market;
using Toobit.Platform.Domain.Orders;
using Toobit.Platform.Risk;

namespace Toobit.Platform.Application;

public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

/// <summary>Env vars in dev; Vault / AWS Secrets Manager in prod. Never appsettings, never logged.</summary>
public interface ISecretProvider { string GetRequired(string name); }

/// <summary>Single flag that disables order submission instantly. Must fail CLOSED (unreadable => ON).</summary>
public interface IKillSwitch
{
    Task<bool> IsOnAsync(CancellationToken ct);
    Task SetAsync(bool on, string actor, string reason, CancellationToken ct);
}

/// <summary>One backing store for the kill switch flag (Redis for speed, DB as source of truth).</summary>
public interface IKillSwitchStore
{
    Task<bool> GetAsync(CancellationToken ct);
    Task SetAsync(bool on, CancellationToken ct);
}

public interface IAuditLog { Task AppendAsync(string actor, string action, string detail, string correlationId, CancellationToken ct); }

public interface IFeatureFlags
{
    Task<bool> AutomatedTradingEnabledAsync(CancellationToken ct);
    Task<string> ScannerWeightSetAsync(CancellationToken ct);
}

public sealed record StrategySettings(decimal StopLossPct, decimal TakeProfitPct, decimal OrderNotionalUsdt,
    bool LimitFirst, TimeSpan LimitTimeout);
public interface IStrategyConfigProvider
{
    Task<StrategySettings> GetAsync(CancellationToken ct);
    Task<RiskLimits> GetRiskLimitsAsync(CancellationToken ct);
}

public interface IOrderRepository
{
    /// <summary>Persists the order and any new events atomically.</summary>
    Task SaveAsync(Order order, IReadOnlyList<OrderEvent> newEvents, CancellationToken ct);
    Task<IReadOnlyList<Order>> GetByStateAsync(OrderState state, CancellationToken ct);
    Task<IReadOnlyList<Order>> GetActiveAsync(CancellationToken ct);
}

public interface ISignalRepository { Task AddAsync(Signal signal, CancellationToken ct); }
public interface IRiskEventRepository { Task AddAsync(RiskEvent e, CancellationToken ct); }
public interface IRiskContextProvider { Task<RiskContext> GetAsync(string symbol, CancellationToken ct); }

public interface ISymbolPauseStore
{
    Task PauseAsync(string symbol, string reason, CancellationToken ct);
    Task<bool> IsPausedAsync(string symbol, CancellationToken ct);
    Task<int> ClearAsync(string symbol, CancellationToken ct);
    Task<IReadOnlyList<string>> ListPausedAsync(CancellationToken ct);
}
public interface IReconciliationRepository { Task AddAsync(ReconciliationEvent e, CancellationToken ct); }

public interface ITrendingRepository
{
    Task SaveSnapshotAsync(IReadOnlyList<TrendingSymbol> ranked, CancellationToken ct);
    Task<IReadOnlyList<TrendingSymbol>> GetLatestAsync(int top, CancellationToken ct);
}

// ---- Exchange ports (implemented in Infrastructure; the ONLY code that talks to Toobit) ----------

public enum SubmitOutcome { Acked, Rejected, RateLimited, Banned, Ambiguous }
/// <summary>429 => not processed (safe to retry same ClientOrderId). 418 => ban. Timeout/5XX => Ambiguous.</summary>
public sealed record SubmitResult(SubmitOutcome Outcome, string? ToobitOrderId = null, TimeSpan? RetryAfter = null, string? Error = null);

public enum QueryStatus { Found, NotFound, Error }
public sealed record ExchangeOrder(string ToobitOrderId, Guid ClientOrderId, OrderState State);
public sealed record QueryResult(QueryStatus Status, ExchangeOrder? Order = null);
public sealed record ExchangePosition(string Symbol, string Side, decimal Quantity);

public interface ITradingExchange
{
    Task<SubmitResult> SubmitOrderAsync(Order order, CancellationToken ct);
    Task<QueryResult> QueryOrderAsync(Guid clientOrderId, CancellationToken ct);
    Task<IReadOnlyList<ExchangeOrder>> GetOpenOrdersAsync(CancellationToken ct);
    Task<IReadOnlyList<ExchangePosition>> GetPositionsAsync(CancellationToken ct);
}

/// <summary>Public, unsigned GET /quote/v1/contract/ticker/24hr on its OWN HttpClient and rate-limit budget.</summary>
public interface IScannerExchange { Task<IReadOnlyList<TickerSnapshot>> GetTickersAsync(CancellationToken ct); }
