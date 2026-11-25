// Mirrors Toobit.Platform.Domain.Orders.OrderState (JSON enum index). Keep in sync with the backend enum.
export const ORDER_STATES = ['Received', 'Validated', 'RiskChecked', 'Submitted', 'Acked', 'PartiallyFilled', 'Filled', 'Canceled', 'Rejected', 'Unknown'] as const
export type OrderStateName = (typeof ORDER_STATES)[number]
export const stateName = (i: number | undefined): string => (i === undefined ? '' : ORDER_STATES[i] ?? String(i))
