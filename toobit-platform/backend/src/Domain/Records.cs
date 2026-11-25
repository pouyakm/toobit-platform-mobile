using Toobit.Platform.Domain.Common;

namespace Toobit.Platform.Domain;

public sealed class Signal : Entity
{
    public string Symbol { get; init; } = "";
    public string Action { get; init; } = "";
    public decimal Confidence { get; init; }
    public string? ModelVersion { get; init; }
    public string FeaturesJson { get; init; } = "{}";
    public string ExplainJson { get; init; } = "{}";
    public bool Shadow { get; init; }           // true = generated while automated trading was disabled
    public string CorrelationId { get; init; } = "";
}

public sealed class Position : Entity
{
    public string Symbol { get; set; } = "";
    public string Side { get; set; } = "";      // LONG | SHORT
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public DateTimeOffset? ReconciledAt { get; set; }
}

public sealed class RiskEvent : Entity
{
    public string Symbol { get; init; } = "";
    public string Rule { get; init; } = "";
    public string Reason { get; init; } = "";
    public string CorrelationId { get; init; } = "";
}

public sealed class ReconciliationEvent : Entity
{
    public string Symbol { get; init; } = "";
    public string Kind { get; init; } = "";     // UnknownResolved | OrderDivergence | PositionDivergence
    public string Detail { get; init; } = "";
    public bool PausedSymbol { get; init; }
}

/// <summary>Append-only: the DB role used by the app has INSERT/SELECT only on this table.</summary>
public sealed class AuditLog : Entity
{
    public string Actor { get; init; } = "";
    public string Action { get; init; } = "";
    public string Detail { get; init; } = "";
    public string CorrelationId { get; init; } = "";
}
