using Microsoft.EntityFrameworkCore;
using Toobit.Platform.Domain;
using Toobit.Platform.Domain.Auth;
using Toobit.Platform.Domain.Common;
using Toobit.Platform.Domain.Market;
using Toobit.Platform.Domain.Orders;

namespace Toobit.Platform.Infrastructure.Persistence;

public sealed class KillSwitchState : Entity { public bool IsOn { get; set; } }
public sealed class SymbolPause : Entity { public string Symbol { get; set; } = ""; public string Reason { get; set; } = ""; public bool Active { get; set; } = true; }
public sealed class FeatureFlag : Entity { public string Key { get; set; } = ""; public string Value { get; set; } = ""; }

/// <summary>
/// Every table has Id, CreatedAt, UpdatedAt and RowVersion (mapped to Postgres xmin for optimistic concurrency).
/// Create migrations with:  dotnet ef migrations add Initial -p src/Infrastructure -s src/Api
/// Generate rollback scripts: dotnet ef migrations script <to> <from> --idempotent (see RUNBOOKS/rollback.md).
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();
    public DbSet<TrendingSymbol> TrendingSymbols => Set<TrendingSymbol>();
    public DbSet<Signal> Signals => Set<Signal>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<RiskEvent> RiskEvents => Set<RiskEvent>();
    public DbSet<ReconciliationEvent> ReconciliationEvents => Set<ReconciliationEvent>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<KillSwitchState> KillSwitch => Set<KillSwitchState>();
    public DbSet<SymbolPause> SymbolPauses => Set<SymbolPause>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        foreach (var et in mb.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)).ToList())
        {
            mb.Entity(et.ClrType).Property(nameof(Entity.RowVersion))
              .IsRowVersion();   // Npgsql maps a uint row version to the Postgres xmin system column (no DDL column)
            mb.Entity(et.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();   // UUIDv7 assigned in the domain
        }
        mb.Entity<Order>().HasIndex(o => o.ClientOrderId).IsUnique();                  // idempotency guard at the DB level
        mb.Entity<Order>().HasIndex(o => o.StateMachineState);
        mb.Entity<OrderEvent>().HasIndex(e => e.OrderId);
        mb.Entity<TrendingSymbol>().HasIndex(t => new { t.SnapshotId, t.Rank });
        mb.Entity<SymbolPause>().HasIndex(p => p.Symbol);
        mb.Entity<FeatureFlag>().HasIndex(f => f.Key).IsUnique();
        mb.Entity<User>().HasIndex(u => u.EmailHash).IsUnique();
        mb.Entity<RefreshToken>().HasIndex(t => t.TokenHash).IsUnique();
        mb.Entity<RefreshToken>().HasIndex(t => t.FamilyId);
        foreach (var p in mb.Model.GetEntityTypes().SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)).ToList())
        {
            p.SetPrecision(28);
            p.SetScale(10);
        }
    }
}
