# Runbook: Rollback

Principle: **forward-only migrations**, so roll back the *application image* first; use the generated rollback SQL only if a migration itself is the problem.

## 1. Engage the kill switch (see kill-switch-drill.md). Never roll back with live order flow.

## 2. Roll back the application
```bash
ssh deploy@host && cd /opt/toobit
export IMAGE_TAG=<previous-good-git-sha> GHCR_OWNER=<owner>
docker compose -f docker-compose.yml -f deploy/compose.prod.yml pull
docker compose -f docker-compose.yml -f deploy/compose.prod.yml up -d --wait
curl -fsS https://<host>/health/ready
```
Images are tagged by git SHA, so the previous tag is in the CD run history.

## 3. Roll back a migration (only if the schema change itself is faulty)
The CD job uploads `migrate-forward.sql`. Generate the inverse for a specific step:
```bash
dotnet ef migrations script <CurrentMigration> <PreviousMigration> -p src/Infrastructure -s src/Api -o rollback.sql
psql "$POSTGRES_CONNECTION" -f rollback.sql      # review it first; take a pg_dump beforehand
```
Migrations must be backward-compatible with the previous image (add columns nullable; drop in a later release) so step 2 alone is normally enough.

## 4. Verify and release
Readiness green, reconciliation clean, then release the kill switch (Admin, typed `RESUME`).

## Backup and restore (RPO/RTO)
- **Targets (MVP, single VM):** RPO <= 5 min with WAL archiving; RTO <= 60 min. Without WAL archiving, RPO equals the last `pg_dump` (nightly, up to 24 h): set expectations accordingly.
- **Nightly logical backup:** `pg_dump -Fc -f toobit-$(date +%F).dump "$POSTGRES_CONNECTION"`, encrypted and copied off-host (S3-compatible, 30-day retention).
- **PITR sketch:** `archive_mode=on`, `archive_command` ships WAL to object storage, weekly `pg_basebackup`. Restore: stop the stack, restore the base backup, set `recovery_target_time`, start Postgres, verify, then start the API **with the kill switch ON** (the seeder and DB flag keep it ON until an Admin releases it).
- **After any restore:** run reconciliation before enabling trading: the exchange is the source of truth and local state may be behind.
- **Drill the restore quarterly** into a scratch database and run `e2e/smoke.sh` against it.
