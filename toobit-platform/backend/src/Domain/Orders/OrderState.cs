namespace Toobit.Platform.Domain.Orders;

/// <summary>
/// Received -> Validated -> RiskChecked -> Submitted -> Acked -> PartiallyFilled -> Filled | Canceled | Rejected | Unknown.
/// <c>Unknown</c> is first-class: a timeout/5XX on submit means the outcome is NOT known. Reconcile, never mark failed.
/// </summary>
public enum OrderState
{
    Received, Validated, RiskChecked, Submitted, Acked, PartiallyFilled, Filled, Canceled, Rejected, Unknown
}

public static class OrderStateMachine
{
    private static readonly IReadOnlyDictionary<OrderState, OrderState[]> Allowed =
        new Dictionary<OrderState, OrderState[]>
        {
            [OrderState.Received]        = new[] { OrderState.Validated, OrderState.Rejected },
            [OrderState.Validated]       = new[] { OrderState.RiskChecked, OrderState.Rejected },
            [OrderState.RiskChecked]     = new[] { OrderState.Submitted, OrderState.Rejected },
            [OrderState.Submitted]       = new[] { OrderState.Acked, OrderState.Rejected, OrderState.Unknown },
            // Unknown may resolve to ANY real exchange state (found by ClientOrderId) or to Rejected (confirmed absent).
            [OrderState.Unknown]         = new[] { OrderState.Acked, OrderState.PartiallyFilled, OrderState.Filled,
                                                   OrderState.Canceled, OrderState.Rejected },
            [OrderState.Acked]           = new[] { OrderState.PartiallyFilled, OrderState.Filled, OrderState.Canceled },
            [OrderState.PartiallyFilled] = new[] { OrderState.PartiallyFilled, OrderState.Filled, OrderState.Canceled },
            [OrderState.Filled]          = Array.Empty<OrderState>(),
            [OrderState.Canceled]        = Array.Empty<OrderState>(),
            [OrderState.Rejected]        = Array.Empty<OrderState>(),
        };

    public static bool CanTransition(OrderState from, OrderState to) => Allowed[from].Contains(to);
    public static bool IsTerminal(OrderState s) => Allowed[s].Length == 0;
    public static IReadOnlyCollection<OrderState> NextStates(OrderState s) => Allowed[s];
}

public sealed class InvalidOrderTransitionException(OrderState from, OrderState to)
    : InvalidOperationException($"Illegal order transition {from} -> {to}");
