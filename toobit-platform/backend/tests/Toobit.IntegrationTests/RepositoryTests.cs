using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Toobit.Platform.Domain.Orders;
using Toobit.Platform.Infrastructure.Persistence;
using Toobit.UnitTests;
using Xunit;

namespace Toobit.IntegrationTests;

/// <summary>EF Core repositories against a REAL Postgres (Testcontainers). Requires Docker; skipped in CI jobs without it.</summary>
public sealed class RepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private sealed class Factory(string cs) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options);
    }
    private IDbContextFactory<AppDbContext> _f = null!;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _f = new Factory(_pg.GetConnectionString());
        await using var db = _f.CreateDbContext();
        await db.Database.EnsureCreatedAsync();   // CI applies real migrations via `dotnet ef database update`; EnsureCreated keeps this test hermetic
    }
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    [Fact]
    public async Task Order_and_events_roundtrip_with_full_trail()
    {
        var repo = new EfOrderRepository(_f);
        var o = Order.Create("BTC-SWAP-USDT", Side.Buy, 0.01m, 60_000m, 59_400m, 61_200m, "corr", DateTimeOffset.UtcNow);
        await repo.SaveAsync(o, Array.Empty<OrderEvent>(), default);                       // persisted BEFORE submit
        var ev = o.TransitionTo(OrderState.Validated, "ok", DateTimeOffset.UtcNow);
        await repo.SaveAsync(o, new[] { ev }, default);

        await using var db = _f.CreateDbContext();
        var loaded = await db.Orders.AsNoTracking().SingleAsync(x => x.ClientOrderId == o.ClientOrderId);
        Assert.Equal(OrderState.Validated, loaded.StateMachineState);
        Assert.Equal(1, await db.OrderEvents.CountAsync(e => e.OrderId == o.Id));
    }

    [Fact]
    public async Task Duplicate_ClientOrderId_is_rejected_by_the_database()   // idempotency guard
    {
        var repo = new EfOrderRepository(_f);
        var o = Order.Create("ETH-SWAP-USDT", Side.Sell, 1m, 3000m, 3100m, 2900m, "c", DateTimeOffset.UtcNow);
        await repo.SaveAsync(o, Array.Empty<OrderEvent>(), default);
        await using var db = _f.CreateDbContext();
        var second = Order.Create("ETH-SWAP-USDT", Side.Sell, 1m, 3000m, 3100m, 2900m, "c2", DateTimeOffset.UtcNow);   // new PK
        db.Orders.Add(second);
        db.Entry(second).Property(nameof(Order.ClientOrderId)).CurrentValue = o.ClientOrderId;                          // collide ONLY on the unique index
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("ClientOrderId", ex.InnerException?.Message ?? ex.Message);
    }

    [Fact]
    public async Task Kill_switch_store_persists_and_audit_log_appends()
    {
        var clock = new FakeClock();
        var ks = new DbKillSwitchStore(_f, clock);
        Assert.False(await ks.GetAsync(default));
        await ks.SetAsync(true, default);
        Assert.True(await ks.GetAsync(default));

        var audit = new EfAuditLog(_f, clock);
        await audit.AppendAsync("alice", "killswitch.on", "drill", "corr", default);
        await using var db = _f.CreateDbContext();
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "killswitch.on"));
    }
}
