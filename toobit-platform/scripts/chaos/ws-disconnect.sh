#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
C="docker compose -f docker-compose.yml -f e2e/docker-compose.e2e.yml"
WM=http://localhost:8089/__admin
echo "[chaos] restarting the mock exchange (drops all connections)"
$C restart mock-toobit; sleep 5
curl -s localhost:8080/metrics | grep -E "^ws_reconnect_total" || echo "(metric not present yet: wait one backoff cycle)"
