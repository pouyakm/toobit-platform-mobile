using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Toobit.Platform.Application;

namespace Toobit.Platform.Infrastructure.Security;

/// <summary>
/// Kill switch with a fast store (Redis) and a durable source of truth (DB).
///  read:  Redis first; if Redis is down read the DB; if BOTH fail => treated as ON (fail closed).
///  write: DB first (durable), then Redis (best effort), and the change is audited.
/// Order submission checks this immediately before every exchange call.
/// </summary>
public sealed class FailClosedKillSwitch(IKillSwitchStore fast, IKillSwitchStore durable, IAuditLog audit,
    ILogger<FailClosedKillSwitch> log) : IKillSwitch
{
    public async Task<bool> IsOnAsync(CancellationToken ct)
    {
        try { return await fast.GetAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("Kill-switch fast store unavailable ({Type}); falling back to DB", ex.GetType().Name);
        }
        try { return await durable.GetAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError("Kill-switch DB unavailable ({Type}); failing CLOSED (trading halted)", ex.GetType().Name);
            return true;
        }
    }

    public async Task SetAsync(bool on, string actor, string reason, CancellationToken ct)
    {
        await durable.SetAsync(on, ct);                                  // must succeed
        try { await fast.SetAsync(on, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { log.LogWarning("Kill-switch cache write failed ({Type}); DB is authoritative", ex.GetType().Name); }
        await audit.AppendAsync(actor, on ? "killswitch.on" : "killswitch.off", reason, "", ct);
        log.LogWarning("KILL SWITCH {State} by {Actor}: {Reason}", on ? "ON" : "OFF", actor, reason);
    }
}

public sealed class RedisKillSwitchStore(IConnectionMultiplexer redis) : IKillSwitchStore
{
    private const string Key = "toobit:killswitch";
    public async Task<bool> GetAsync(CancellationToken ct)
    {
        var v = await redis.GetDatabase().StringGetAsync(Key);
        if (v.IsNull) throw new InvalidOperationException("kill switch not cached");   // forces DB read on a cold cache
        return v == "1";
    }
    public Task SetAsync(bool on, CancellationToken ct) => redis.GetDatabase().StringSetAsync(Key, on ? "1" : "0");
}
