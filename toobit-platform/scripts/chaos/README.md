# Chaos drills (scriptable)

Run against the e2e stack: `docker compose -f docker-compose.yml -f e2e/docker-compose.e2e.yml up -d --wait`.
Each script injects ONE fault, prints what to observe, then restores. Expected behaviour is asserted where it can be scripted.

| Script | Fault | Expected |
|---|---|---|
| `toobit-5xx.sh` | Mock Toobit returns 503 on order submit | Order -> `Unknown` (not Rejected); one POST only; reconciler resolves it after restore |
| `toobit-429.sh` | Mock returns 429 + reset header | Jittered backoff, SAME ClientOrderId, `exchange_rate_limit_total{status="429"}` increments, no crash |
| `ws-disconnect.sh` | Restart the mock (drops sockets) | `ws_reconnect_total` increments, staleness banner appears then clears |
| `db-failover.sh` | Pause Postgres 20s | Readiness fails; kill switch fails CLOSED; recovery without restart |
| `redis-down.sh` | Stop Redis | Readiness DEGRADED (200); kill switch falls back to DB; trading continues safely |
| `python-down.sh` | Stop analytics | No new signals; API stays ready (degraded); existing exchange-side SL/TP untouched |
