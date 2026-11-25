using Toobit.Platform.Domain.Common;

namespace Toobit.Platform.Domain.Orders;

public enum Side { Buy, Sell }

/// <summary>Append-only record of one state transition. Never updated or deleted.</summary>
public sealed class OrderEvent : Entity
{
    public Guid OrderId { get; init; }
    public OrderState From { get; init; }
    public OrderState To { get; init; }
    public string Reason { get; init; } = "";
    public string CorrelationId { get; init; } = "";
    public DateTimeOffset OccurredAt { get; init; }
}

public sealed class Order : Entity
{
    private Order() { } // EF

    /// <summary>Client-generated UUIDv7, persisted BEFORE submit; sent as newClientOrderId (idempotency).</summary>
    public Guid ClientOrderId { get; private set; }
    public string? ToobitOrderId { get; private set; }
    public string Symbol { get; private set; } = "";
    public Side Side { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal? LimitPrice { get; private set; }
    public decimal StopLoss { get; private set; }
    public decimal TakeProfit { get; private set; }
    public bool ReduceOnly { get; private set; }
    public OrderState StateMachineState { get; private set; } = OrderState.Received;
    public int UnknownRetries { get; private set; }
    public string CorrelationId { get; private set; } = "";

    public static Order Create(string symbol, Side side, decimal qty, decimal? limitPrice,
        decimal stopLoss, decimal takeProfit, string correlationId, DateTimeOffset now, bool reduceOnly = false) =>
        new()
        {
            ClientOrderId = Uuid7.NewGuid(now), Symbol = symbol, Side = side, Quantity = qty, LimitPrice = limitPrice,
            StopLoss = stopLoss, TakeProfit = takeProfit, ReduceOnly = reduceOnly, CorrelationId = correlationId,
            CreatedAt = now, UpdatedAt = now,
        };

    /// <summary>Applies a legal transition and returns the event to append. Illegal moves throw.</summary>
    public OrderEvent TransitionTo(OrderState next, string reason, DateTimeOffset now)
    {
        if (!OrderStateMachine.CanTransition(StateMachineState, next))
            throw new InvalidOrderTransitionException(StateMachineState, next);
        var ev = new OrderEvent
        {
            OrderId = Id, From = StateMachineState, To = next, Reason = reason,
            CorrelationId = CorrelationId, OccurredAt = now, CreatedAt = now, UpdatedAt = now,
        };
        StateMachineState = next;
        UpdatedAt = now;
        return ev;
    }

    public void SetToobitOrderId(string id) => ToobitOrderId = id;
    public void IncrementUnknownRetries() => UnknownRetries++;
}
