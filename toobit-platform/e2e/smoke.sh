#!/usr/bin/env bash
# E2E smoke (acceptance #1, #5, #8): docker compose up -> mock Toobit -> signed signal -> limit order -> assert DB state trail.
# Usage: e2e/smoke.sh        (from repo root; needs docker, curl, openssl, jq)
set -euo pipefail
cd "$(dirname "$0")/.."
COMPOSE="docker compose -f docker-compose.yml -f e2e/docker-compose.e2e.yml"
[ -f .env ] || cp .env.example .env
set -a; . ./.env; set +a
API=http://localhost:8080
fail() { echo "SMOKE FAIL: $*" >&2; $COMPOSE logs --tail=60 api >&2 || true; exit 1; }

$COMPOSE up -d --build --wait --wait-timeout 240 || fail "stack did not become healthy"

echo "1. health endpoints"
[ "$(curl -s -o /dev/null -w '%{http_code}' $API/health/live)" = 200 ] || fail "live"
[ "$(curl -s -o /dev/null -w '%{http_code}' $API/health/ready)" = 200 ] || fail "ready"

echo "2. login as seeded Admin"
TOKEN=$(curl -sf -X POST $API/api/auth/login -H 'Content-Type: application/json' \
  -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" | jq -r .accessToken) || fail "login"
AUTH="Authorization: Bearer $TOKEN"

echo "3. scanner produced a persisted Top-N"
for i in $(seq 1 30); do
  N=$(curl -sf -H "$AUTH" "$API/api/scanner/top?n=5" | jq '.items|length' || echo 0); [ "$N" -gt 0 ] && break; sleep 2
done
[ "$N" -gt 0 ] || fail "scanner Top-N empty"

echo "4. kill switch starts ON: a signal must NOT produce an order"
send_signal() {
  local body ts sig
  body='{"symbol":"BTC-SWAP-USDT","action":"enter_long","confidence":0.8,"referencePrice":60000,"modelVersion":null,"features":{"z":2.0},"explain":{"methodology":"options_proxy_v1","top_features":[{"name":"z","contribution":1.3}]}}'
  ts=$(date +%s)
  sig=$(printf 'POST\n/internal/signals\n%s\n%s' "$ts" "$(printf '%s' "$body" | openssl dgst -sha256 -hex | awk '{print $NF}')" \
        | openssl dgst -sha256 -hmac "$INTERNAL_HMAC_KEY" -hex | awk '{print $NF}')
  curl -s -o /dev/null -w '%{http_code}' -X POST $API/internal/signals -H 'Content-Type: application/json' \
    -H "X-Key-Id: $INTERNAL_HMAC_KEY_ID" -H "X-Timestamp: $ts" -H "X-Signature: $sig" -H "X-Correlation-Id: smoke-$ts" -d "$body"
}
[ "$(send_signal)" = 202 ] || fail "signal ingest should accept (shadow mode)"
sleep 2
PSQL="$COMPOSE exec -T postgres psql -U $POSTGRES_USER -d $POSTGRES_DB -tA"
[ "$($PSQL -c 'select count(*) from "Orders"')" = 0 ] || fail "order created while automated trading was OFF"
[ "$($PSQL -c 'select count(*) from "Signals" where "Shadow"')" -ge 1 ] || fail "shadow signal not recorded"

echo "5. bad HMAC is rejected"
[ "$(curl -s -o /dev/null -w '%{http_code}' -X POST $API/internal/signals -H 'Content-Type: application/json' -H 'X-Key-Id: x' -H 'X-Timestamp: 1' -H 'X-Signature: 00' -d '{}')" = 401 ] || fail "unsigned ingest accepted"

echo "6. enable trading + release kill switch (with confirm token)"
curl -sf -X PUT $API/api/settings/flags -H "$AUTH" -H 'Content-Type: application/json' -d '{"automatedTrading":true,"scannerWeightSet":"default"}' >/dev/null || fail "flags"
[ "$(curl -s -o /dev/null -w '%{http_code}' -X POST $API/api/killswitch -H "$AUTH" -H 'Content-Type: application/json' -d '{"on":false,"reason":"smoke","confirm":"nope"}')" = 400 ] || fail "kill switch accepted a wrong confirm token"
curl -sf -X POST $API/api/killswitch -H "$AUTH" -H 'Content-Type: application/json' -d '{"on":false,"reason":"smoke","confirm":"RESUME"}' >/dev/null || fail "resume"

echo "7. signal -> simulated limit order with full state trail"
[ "$(send_signal)" = 202 ] || fail "signal ingest"
for i in $(seq 1 20); do
  STATE=$($PSQL -c 'select "StateMachineState" from "Orders" order by "CreatedAt" desc limit 1' || true); [ "$STATE" = 4 ] && break; sleep 1   # 4 = Acked
done
[ "$STATE" = 4 ] || fail "order not Acked (state='$STATE')"
TRAIL=$($PSQL -c 'select string_agg("To"::text, chr(44) order by "OccurredAt") from "OrderEvents"')
[ "$TRAIL" = "1,2,3,4" ] || fail "state trail was '$TRAIL', expected 1,2,3,4 (Validated,RiskChecked,Submitted,Acked)"
[ "$($PSQL -c 'select count(*) from "Orders" where "StopLoss" > 0 and "TakeProfit" > 0')" = 1 ] || fail "SL/TP missing"

echo "8. kill switch engages instantly and blocks the next order"
curl -sf -X POST $API/api/killswitch -H "$AUTH" -H 'Content-Type: application/json' -d '{"on":true,"reason":"smoke","confirm":"HALT"}' >/dev/null
BEFORE=$($PSQL -c 'select count(*) from "Orders" where "StateMachineState" = 4'); send_signal >/dev/null; sleep 3
[ "$($PSQL -c 'select count(*) from "Orders" where "StateMachineState" = 4')" = "$BEFORE" ] || fail "order placed while kill switch ON"
[ "$($PSQL -c 'select count(*) from "AuditLogs" where "Action" like '"'"'killswitch.%'"'"'')" -ge 2 ] || fail "kill switch toggles not audited"

echo "SMOKE PASS"
