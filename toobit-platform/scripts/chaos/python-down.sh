#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
C="docker compose -f docker-compose.yml -f e2e/docker-compose.e2e.yml"
WM=http://localhost:8089/__admin
echo "[chaos] stopping analytics"; $C stop analytics; sleep 5
echo "ready: $(curl -s -o /dev/null -w %{http_code} localhost:8080/health/ready) (expect 200 degraded). No new signals will be produced."
read -rp "Press Enter to restore... "; $C start analytics
