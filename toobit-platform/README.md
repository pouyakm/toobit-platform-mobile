# Toobit USDT-M Analysis & Trading Platform

Auditable, observable platform that (1) ranks all `*-SWAP-USDT` contracts by a transparent trendiness score, (2) produces option-proxy signals in an isolated Python service, and (3) executes orders on Toobit under strict risk governance with reconciliation and audit logging.

| Module | Path | Stack |
|---|---|---|
| A. Scanner + Execution | `backend/` | .NET 8, ASP.NET Core, EF Core, Polly, Serilog, OpenTelemetry |
| B. Analytics / ML | `analytics/` | Python 3.12, FastAPI, pandas/numpy, ONNX Runtime |
| C. Dashboard | `frontend/` | React 18, TypeScript, Vite, TanStack Query, Zustand (en + fa) |
| E. Android app | `mobile/` | React Native (Expo SDK 58), Expo Router, TanStack Query (en + fa, RTL) |
| D. Contracts | `contracts/` | OpenAPI 3 + JSON Schema, generated TS client |

Read first: [ARCHITECTURE.md](ARCHITECTURE.md) (diagrams), [SECURITY.md](SECURITY.md), [ADRs/](ADRs), [RUNBOOKS/](RUNBOOKS).

> **Status: read "Known gaps" before enabling live trading.** Live trading is OFF by default (kill switch ON, `automated_trading=false`).

## Quick start (dev, simulated exchange)

```bash
cp .env.example .env                 # placeholders are fine for dev
docker compose -f docker-compose.yml -f e2e/docker-compose.e2e.yml up --build --wait
open http://localhost:8081           # login with ADMIN_EMAIL / ADMIN_PASSWORD from .env
curl localhost:8080/health/live ; curl localhost:8080/health/ready
e2e/smoke.sh                         # full scripted check: signal -> limit order -> state trail -> kill switch
```
Observability: add `--profile observability` (Jaeger :16686, Prometheus :9090, Grafana :3000, dashboard "Toobit Platform").

Per-module dev loops:
```bash
cd analytics && python -m venv .venv && . .venv/bin/activate && pip install -r requirements-dev.txt && pytest
cd frontend  && npm ci && npm run dev            # proxies /api to :8080
cd backend   && dotnet test Toobit.sln           # integration tests need Docker
```

## Environment variables
Authoritative list with comments: [`.env.example`](.env.example) (+ `.env.dev|staging|prod.example`). **Required secrets fail startup if missing**: `TOOBIT_API_KEY`, `TOOBIT_API_SECRET`, `JWT_SIGNING_KEY`, `PII_ENCRYPTION_KEY`, `INTERNAL_HMAC_KEY_ID`, `INTERNAL_HMAC_KEY`, `POSTGRES_CONNECTION`, `REDIS_CONNECTION`. The Python service refuses to start without every `SIGNALS_*` threshold (no hidden defaults, ADR-0001). Secrets are read through `ISecretProvider` (env in dev; implement Vault/AWS Secrets Manager for prod). Never in `appsettings*.json`, Git, logs or the frontend.

## Runbooks by environment
- **dev:** compose as above; the schema is created by `Db__EnsureCreated=true` (dev only).
- **staging/prod:** generate the first migration once (`cd backend && dotnet ef migrations add Initial -p src/Infrastructure -s src/Api`) and commit it. CD builds an EF migration bundle, applies it, deploys images tagged by git SHA, runs smoke tests; prod needs approval on the `production` GitHub environment. Run `RUNBOOKS/db-roles.sql` once to make `AuditLogs` append-only. First prod start: kill switch ON; an Admin releases it after verifying health.
- **Incidents / drills / rollback / backup:** `RUNBOOKS/incident-response.md`, `kill-switch-drill.md`, `rollback.md`, `scripts/chaos/`.

## Tests and coverage targets
| Suite | Where | Intent | Target |
|---|---|---|---|
| Signing vectors | `SigningTests.cs` | HMAC known vectors (Python + openssl agree), parameter-order sensitivity, JSON-body signing, body byte-identity | 100% of signer |
| Order state machine | `DomainAndRiskTests.cs` | legal/illegal transitions, terminal states, `Unknown` not terminal | 100% |
| Risk rules | `DomainAndRiskTests.cs` | rule precedence, SL/TP side, notional, daily loss, max positions | >= 90% |
| Ranking (+ property tests) | `ScannerTests.cs` | filter, explainability; FsCheck: score in [0,1], order independence, monotonicity | >= 90% |
| Executor / reconciliation | `ExecutionTests.cs` | persist-before-submit, 429 backoff same ClientOrderId, 5XX -> Unknown, 418 trips kill switch, divergence pauses symbol | >= 85% |
| Kill switch / auth / PII | `ExecutionTests.cs`, `AuthTests.cs` | fail-closed, JWT <= 15 min, refresh reuse detection, AES-GCM tamper detection | >= 85% |
| WireMock integration | `IntegrationTests/ToobitWireMockTests.cs` | real HTTP client vs mock exchange: scanner is unsigned, signature recomputed from the wire, 429/418/5XX classes, timeout | n/a |
| Postgres integration | `IntegrationTests/RepositoryTests.cs` | Testcontainers: trail round-trip, unique `ClientOrderId` | n/a |
| Python | `analytics/tests` | Greeks vs closed form, no Toobit access / no hardcoded thresholds, API auth + replay, walk-forward, drift, hypothesis property test | >= 80% (CI gate) |
| Contract | `test_contract.py`, `ContractTests.cs` | one JSON Schema + fixture validated by both Python and .NET | n/a |
| Frontend | `frontend/src/**/*.test.*` | i18n key parity en/fa, token storage, confirm dialog, defensive parsing | n/a |
| E2E smoke | `e2e/smoke.sh` | compose up -> mock Toobit -> signed signal -> Acked order with trail 1,2,3,4 -> kill switch blocks | CI `e2e` job |
| Load | `loadtest/scanner.js` | k6, 25 VUs, p95 < 500 ms | manual / nightly |

Backend CI gate: >= 80% line coverage on `Risk`, `MarketScanner`, `Execution`.

## Verification status (what has actually been run)
| Check | Result |
|---|---|
| Python: 50 tests, coverage | pass, 91.7% |
| Frontend: typecheck, lint, 12 tests, production build | pass |
| Generated TS client from `contracts/openapi.json` | pass |
| YAML (compose, workflows, Prometheus, Grafana) and Grafana JSON parse | pass |
| Shell scripts `bash -n` | pass |
| Backend compile review | An independent reviewer was started but aborted by a rate limit with no findings. I reviewed the riskiest areas myself and fixed 2 certain defects (missing package references; xmin mapping). Not a substitute for a compiler. |
| Mobile: typecheck, lint, 19 jest tests, API types generated | pass |
| **Android build / app on emulator or device** | **NOT RUN** (no Android SDK in the authoring sandbox) |
| **.NET build and tests, docker compose up, CI/CD runs, Grafana rendering** | **NOT RUN** in the authoring sandbox (no .NET SDK or Docker). Treat the first CI run as the real compile check. |

## Known gaps (honest list)
1. **Daily loss limit is not enforced in practice.** `DbRiskContextProvider` returns `DailyRealizedPnl = 0` because no fills/PnL source is wired. Implement a trade-history sync before live trading.
2. **Positions are never written.** Nothing populates the `Positions` table from the exchange (`GetPositionsAsync` is implemented but unused), so the Positions page, max-open-positions and per-symbol notional rules see no positions. Add a position sync into reconciliation.
3. **Unverified Toobit details:** positions endpoint path (`Toobit:PositionsPath`), the "order not found" error code (`OrderNotFoundCode`), the exact 24hr ticker JSON fields (parser accepts short and long names), and the historical-kline source. Order, cancel, query and open-orders paths/params, signing, and the public WebSocket protocol were verified against api-docs.toobit.com. Confirm the rest with recorded real responses before go-live.
4. **Candle history** comes only from the WebSocket kline stream after startup, so signals start after enough 1h candles accumulate (`Analysis:MinCandles`, default 60). Backfill via REST once the endpoint is confirmed.
5. **Limit-then-market fallback** is not implemented: orders go out as LIMIT; `StrategySettings.LimitTimeout` is configured but unused.
6. **Strategy/scanner numbers** are config-file values (hot-reload); only feature flags are editable in the dashboard. No `StrategyConfig`/`ScannerConfig` tables or editor yet. Manual scanner pins are in-memory only.
7. **Toobit.Net** is not used; the client is the raw `HttpClient` implementation (the brief's allowed fallback).
8. **No EF migrations committed** (needs the SDK): generate `Initial`; CI fails without it.
9. **Per-route internal auth is HMAC only**; mTLS is documented as the k8s upgrade. Nonce cache is in-memory (single replica).
10. **Python has no `/metrics`**; drift reports are computed by `ml/drift.py` but not yet scheduled or exposed. `ModelRegistryEntry` table is not created (registry is filesystem only). Logs retention (30d) belongs to the log sink.
11. **Grafana/alert metric names** assume the OpenTelemetry Prometheus exporter's unit suffixing (`*_milliseconds_bucket`). Check `/metrics` once and adjust queries if needed.
12. **Trading is long-only entry signals' mapping** is simple (enter_long -> BUY/LONG, enter_short -> SELL/SHORT); `exit` signals are logged but do not close positions. Exits rely on exchange-side SL/TP and manual close.

## Decision log
| # | Decision | Why | Where |
|---|---|---|---|
| 1 | Option data is a **proxy**: Garman-Klass IV + 30-period HV on 1h candles, synthetic ATM 7-day Black-Scholes | Toobit has no options data | ADR-0001, `analytics/analytics/options_proxy.py` |
| 2 | Score = 0.4 vol + 0.4 momentum + 0.2 spike, min-max normalized | Transparent, explainable per metric | ADR-0002 |
| 3 | `Unknown` is a first-class order state; exchange is source of truth; divergence pauses the symbol | Never mark ambiguous submits as failed | ADR-0003 |
| 4 | Inference runs in the Python service; ONNX artifacts keep in-process .NET a drop-in | Isolation of signal logic from execution | ADR-0004 |
| 5 | Trading client has **no automatic HTTP retry**; 429 retried by the executor with the same ClientOrderId | A POST must never be blindly retried | `ResilienceRegistration.cs`, `OrderExecutor.cs` |
| 6 | Kill switch is fail-closed and re-read right before submit; starts ON | Safety over availability | `FailClosedKillSwitch.cs` |
| 7 | HTTP 418 auto-engages the kill switch | A ban must stop all traffic | `OrderExecutor.cs` |
| 8 | Redis/Toobit/Python outages report `Degraded` (ready stays 200); Postgres is the hard dependency | Graceful degradation | `HealthChecks.cs` |
| 9 | Access token in memory, single-use rotating refresh token in sessionStorage | Survives reload without long-lived bearer in storage | `store.ts`, `AuthController.cs` |
| 10 | Single seeded Admin, full RBAC roles, no signup | MVP scope (defaults accepted) | `AdminSeeder.cs` |
| 11 | Single-VM compose for dev/staging; k8s later | Deployment default accepted | `docker-compose.yml`, `deploy/` |
| 12 | Simulated exchange (WireMock) for all automated tests; live trading opt-in via flag | No confirmed testnet | `e2e/`, `IntegrationTests/` |
