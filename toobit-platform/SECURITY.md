# Security

## Secrets
- Loaded through `ISecretProvider`. Dev: environment variables. Prod: Vault or AWS Secrets Manager implementation.
- No secrets in `appsettings*.json`, Git, logs, error messages or the frontend. `gitleaks` runs in CI.
- Toobit API key/secret exist only in the .NET process. If ever persisted, they are encrypted with an envelope key from KMS/DPAPI.
- Log redaction: Serilog destructuring policy masks `apiKey`, `secret`, `signature`, `authorization`, `password`, `token`.
- Startup validation fails fast if a required secret is absent.

## Authentication and authorization
- JWT access tokens, lifetime 15 minutes max. Refresh tokens are single-use with rotation; presenting a rotated token revokes the whole token family (reuse detection).
- Roles: `Admin` (config, kill switch, user admin), `Trader` (orders, manual close, overrides), `Viewer` (read-only).
- MVP: one seeded Admin, no self-service signup.
- Passwords hashed with Argon2id (or PBKDF2 with 600k iterations if the library is unavailable). Emails stored as HMAC-SHA256 hash for lookup plus AES-GCM encrypted value for display.

## Internal API (.NET <-> Python)
- Python to .NET signal ingest: HMAC-SHA256 over `method \n path \n timestamp \n sha256(body)`, headers `X-Key-Id`, `X-Timestamp`, `X-Signature`. 60s clock skew window, nonce cache in Redis to block replay. Per-route key scopes.
- .NET to Python: same scheme in the other direction. mTLS is the recommended upgrade in k8s (see ADR-0004).

## Exchange traffic
- Signing: HMAC-SHA256 of the exact serialized query/body string, in the same parameter order as sent. Covered by known-vector unit tests.
- Honor `X-Api-Limit-Status` and `X-Api-Limit-Reset-Timestamp`. 429 means back off until reset; 418 means ban, stop trading calls and alert; 5XX on order submit means `Unknown`, never blind retry.

## Audit
Append-only `AuditLog` (DB role has INSERT but no UPDATE/DELETE) for logins, config changes, overrides, kill-switch toggles, and every order state transition.

## Supply chain and CI
`gitleaks`, `dotnet list package --vulnerable`, `npm audit`, `bandit`, `pip-audit`; pinned base image digests; containers run as non-root.

## Reporting
Report vulnerabilities privately to the engineering owner; do not open public issues.
