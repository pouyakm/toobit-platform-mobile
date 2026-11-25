# Runbook: Incident response

**First action in ANY trading incident: engage the kill switch** (Settings -> Halt, or `POST /api/killswitch`). Investigate second. Exchange-side SL/TP orders stay active, so open positions remain protected.

## Severity
| Sev | Examples | Response |
|---|---|---|
| 1 | Unexpected orders, position/ledger divergence, exchange ban (418), suspected credential leak | Halt now, page the owner, start a timeline |
| 2 | Orders stuck in `Unknown`, scanner or market data stale > 10 min, repeated 429s | Investigate within 30 min |
| 3 | Dashboard errors, a single failed scan | Next business day |

## Triage (5 minutes)
1. `GET /health/ready` and the Grafana *Toobit Platform* dashboard (scanner stale, market data age, rate limits, divergences).
2. Dashboard -> Risk & Logs -> **Reconciliation events**: any divergence pauses that symbol automatically.
3. Orders page: any order in `Unknown` for more than 2 minutes? Reconciler retries every 30 s; past `MaxUnknownRetries` the symbol is paused.
4. Logs: filter by `CorrelationId` (it spans .NET -> Python -> .NET).

## Playbooks
- **HTTP 418 (ban):** the system already engaged the kill switch. Do **not** retry. Wait for the ban window to end, then resume. Review request volume.
- **429 storm:** expected behavior is jittered backoff, same ClientOrderId. If persistent, lower scanner frequency (`Scanner__IntervalSeconds`).
- **Divergence on a symbol:** compare the exchange UI with Orders/Positions. The exchange is the source of truth. Fix the cause, then clear the pause (set `SymbolPauses.Active=false` via an audited admin action).
- **Suspected credential leak:** halt, revoke the Toobit API key on the exchange, rotate `TOOBIT_API_*`, `JWT_SIGNING_KEY`, `INTERNAL_HMAC_KEY` in the secret manager, restart. Rotating `JWT_SIGNING_KEY` logs everyone out.
- **Python down:** no new signals; nothing else breaks. Restart `analytics`.

## After
Write a blameless post-mortem within 3 business days: timeline (from AuditLog + OrderEvents), root cause, what the system did automatically, action items.
