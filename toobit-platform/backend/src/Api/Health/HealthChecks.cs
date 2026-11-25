using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using Toobit.Platform.Infrastructure.Persistence;

namespace Toobit.Platform.Api.Health;

// /health/live  : process is up (no dependencies).
// /health/ready : Postgres + Redis + Toobit (public ping) + Python. Tagged "ready".

public sealed class PostgresHealth(IDbContextFactory<AppDbContext> f) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext c, CancellationToken ct = default)
    {
        await using var db = await f.CreateDbContextAsync(ct);
        return await db.Database.CanConnectAsync(ct) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("postgres unreachable");
    }
}

public sealed class RedisHealth(IConnectionMultiplexer r) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext c, CancellationToken ct = default)
    {
        try { await r.GetDatabase().PingAsync(); return HealthCheckResult.Healthy(); }
        catch (Exception) { return HealthCheckResult.Degraded("redis unreachable; kill switch falls back to DB"); }   // degraded, not down
    }
}

/// <summary>Public, unsigned reachability probe (server time). No credentials involved.</summary>
public sealed class ToobitHealth(IHttpClientFactory f) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext c, CancellationToken ct = default)
    {
        try
        {
            using var r = await f.CreateClient("toobit-scanner").GetAsync("/api/v1/time", ct);
            return r.IsSuccessStatusCode ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded($"toobit HTTP {(int)r.StatusCode}");
        }
        catch (Exception) { return HealthCheckResult.Degraded("toobit unreachable"); }
    }
}

public sealed class PythonHealth(IHttpClientFactory f) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext c, CancellationToken ct = default)
    {
        try
        {
            using var r = await f.CreateClient("analytics").GetAsync("/health", ct);
            return r.IsSuccessStatusCode ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded($"analytics HTTP {(int)r.StatusCode}");
        }
        catch (Exception) { return HealthCheckResult.Degraded("analytics unreachable; no new signals, existing SL/TP remain on exchange"); }
    }
}
