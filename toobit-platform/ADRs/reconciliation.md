# ADR-0003: Reconciliation strategy

Status: Accepted

## Context
Order submits can time out or return 5XX with unknown outcome. Local state can drift from exchange state (fills while disconnected, manual action on the exchange).

## Decision
- 5XX or timeout on submit moves the order to `Unknown` (first-class state), never `Rejected`.
- A `ReconciliationService` (BackgroundService) runs every `reconcile.interval_seconds` (default 30):
  1. Resolve each `Unknown` order by querying Toobit with `ClientOrderId` (fallback `ToobitOrderId`). Found: transition to the real state. Absent after `unknown_grace_seconds`: `Rejected` with reason `not_found_on_exchange`. Increment `UnknownRetries`; past `max_unknown_retries`, raise an alert and pause the symbol.
  2. Diff open orders and positions against local rows. Any divergence writes a `ReconciliationEvent` and pauses automated trading for that symbol until an operator clears it.
- Exchange is the source of truth; local rows are corrected, never the reverse.
- Reconciliation uses the trading rate-limit budget with low priority and never retries non-idempotent writes.

## Consequences
Pausing per symbol limits blast radius. Operators must clear pauses via the dashboard (audited).
