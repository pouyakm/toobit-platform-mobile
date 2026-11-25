#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
C="docker compose -f docker-compose.yml -f e2e/docker-compose.e2e.yml"
WM=http://localhost:8089/__admin
echo "[chaos] stopping Redis"; $C stop redis; sleep 5
echo "ready (degraded still 200): $(curl -s -o /dev/null -w %{http_code} localhost:8080/health/ready)"
curl -s localhost:8080/health/ready; echo
read -rp "Verify the kill switch still reads from the DB (dashboard banner), then press Enter to restore... "; $C start redis
