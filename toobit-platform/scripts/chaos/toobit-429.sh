#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
C="docker compose -f docker-compose.yml -f e2e/docker-compose.e2e.yml"
WM=http://localhost:8089/__admin
echo "[chaos] mock Toobit -> 429 with reset hint"
RESET=$(( ($(date +%s) + 3) * 1000 ))
ID=$(curl -sf -X POST $WM/mappings -H "Content-Type: application/json" -d "{\"priority\":1,\"request\":{\"method\":\"POST\",\"urlPath\":\"/api/v2/futures/order\"},\"response\":{\"status\":429,\"headers\":{\"X-Api-Limit-Status\":\"limited\",\"X-Api-Limit-Reset-Timestamp\":\"$RESET\"}}}" | jq -r .id)
echo "Send a signal. Expect retries with growing jittered delays, one ClientOrderId, and the 429 counter rising on /metrics."
read -rp "Press Enter to restore... "; curl -sf -X DELETE $WM/mappings/$ID >/dev/null; echo "[chaos] restored"
