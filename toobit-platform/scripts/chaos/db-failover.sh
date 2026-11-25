#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
C="docker compose -f docker-compose.yml -f e2e/docker-compose.e2e.yml"
WM=http://localhost:8089/__admin
echo "[chaos] pausing Postgres for 20s"
$C pause postgres; sleep 3
echo "ready during outage: $(curl -s -o /dev/null -w %{http_code} localhost:8080/health/ready) (expect 503)"
sleep 17; $C unpause postgres; sleep 5
echo "ready after recovery: $(curl -s -o /dev/null -w %{http_code} localhost:8080/health/ready) (expect 200)"
