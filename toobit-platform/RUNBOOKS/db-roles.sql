-- Run once per environment as a superuser (psql). Makes the audit trail append-only at the DATABASE level.
-- The application connects as toobit_app, which can INSERT/SELECT AuditLogs but never UPDATE/DELETE/TRUNCATE them.
-- (If EF migrations are run by a separate owner role, grant to toobit_app AFTER migrating.)
CREATE ROLE toobit_app LOGIN PASSWORD :'app_password';          -- psql -v app_password='...'
GRANT CONNECT ON DATABASE toobit TO toobit_app;
GRANT USAGE ON SCHEMA public TO toobit_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO toobit_app;
REVOKE UPDATE, DELETE, TRUNCATE ON "AuditLogs" FROM toobit_app;   -- append-only
-- OrderEvents are append-only except for retention pruning of terminal orders (RetentionService).
REVOKE UPDATE ON "OrderEvents" FROM toobit_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO toobit_app;
