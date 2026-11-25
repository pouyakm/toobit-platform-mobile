# Runbook: Kill-switch drill

**Purpose:** prove, on a schedule (monthly, and after every deploy to prod), that order submission can be halted instantly and resumed safely.
**Who:** an Admin. **Where:** staging first, prod only in a quiet window.

## Engage
1. Dashboard -> Settings -> *Halt all order submission*. Type `HALT`, add a reason, Confirm.
   API equivalent: `POST /api/killswitch {"on":true,"reason":"drill","confirm":"HALT"}`.
2. Within seconds the banner reads **KILL SWITCH IS ON**.
3. Send a test signal (shadow mode is fine). Expected: the signal is stored, **no order** is created, a `RiskEvent` with rule `KillSwitch` appears under Risk & Logs.

## Verify the fail-closed behavior
| Fault | Expected |
|---|---|
| `docker compose stop redis` | Readiness is *degraded* (still 200). Kill switch reads from Postgres and is still ON. |
| Stop Postgres **and** Redis | Kill switch reads as ON (fail closed); `/health/ready` is 503. |

## Release
1. Confirm reconciliation shows no open divergence (Risk & Logs -> Reconciliation).
2. Settings -> *Resume order submission*. Type `RESUME`, Confirm.
3. Check the AuditLog has two entries: `killswitch.on` and `killswitch.off`, with your user and reasons.

## Pass criteria
Halt takes effect in under 5 s; no order is placed while ON; both toggles are audited; release needs the typed confirmation.
**Automated check:** `e2e/smoke.sh` step 8 and the unit test `Kill_switch_halts_submission_and_exchange_is_never_called`.
