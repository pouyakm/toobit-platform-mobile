#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
C="docker compose -f docker-compose.yml -f e2e/docker-compose.e2e.yml"
WM=http://localhost:8089/__admin
echo "[chaos] mock Toobit -> 503 on order submit"
ID=$(curl -sf -X POST $WM/mappings -H "Content-Type: application/json" -d "{\"priority\":1,\"request\":{\"method\":\"POST\",\"urlPath\":\"/api/v2/futures/order\"},\"response\":{\"status\":503}}" | jq -r .id)
echo "Now send a signal (see e2e/smoke.sh send_signal). Expect Orders.StateMachineState = 9 (Unknown) and exactly one POST in $WM/requests."
read -rp "Press Enter to restore the exchange (reconciler should then resolve the order)... "
curl -sf -X DELETE $WM/mappings/$ID >/dev/null; echo "[chaos] restored"
