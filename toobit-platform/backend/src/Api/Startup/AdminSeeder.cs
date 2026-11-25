using Microsoft.EntityFrameworkCore;
using Toobit.Platform.Application;
using Toobit.Platform.Domain.Auth;
using Toobit.Platform.Infrastructure.Persistence;
using Toobit.Platform.Infrastructure.Security;

namespace Toobit.Platform.Api.Startup;

/// <summary>
/// MVP: no self-service signup. Seeds ONE Admin from ADMIN_EMAIL / ADMIN_PASSWORD (secret provider) when the Users table is empty.
/// Also applies safe defaults for feature flags (automated_trading=false, kill switch ON until an Admin releases it).
/// </summary>
public sealed class AdminSeeder(IDbContextFactory<AppDbContext> dbf, ISecretProvider secrets, PiiProtector pii, IClock clock, ILogger<AdminSeeder> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var now = clock.UtcNow;
        if (!await db.Users.AnyAsync(ct))
        {
            string email, pw;
            try { email = secrets.GetRequired("ADMIN_EMAIL"); pw = secrets.GetRequired("ADMIN_PASSWORD"); }
            catch (InvalidOperationException) { log.LogWarning("No users and no ADMIN_EMAIL/ADMIN_PASSWORD: nobody can log in"); return; }
            if (pw.Length < 12) throw new InvalidOperationException("ADMIN_PASSWORD must be at least 12 characters");
            db.Users.Add(new User { EmailHash = pii.LookupHash(email), EmailEncrypted = pii.Encrypt(email), PasswordHash = PasswordHasher.Hash(pw), Role = "Admin", CreatedAt = now, UpdatedAt = now });
            log.LogInformation("Seeded initial Admin user");
        }
        if (!await db.FeatureFlags.AnyAsync(ct))
            db.FeatureFlags.AddRange(
                new FeatureFlag { Key = "automated_trading", Value = "false", CreatedAt = now, UpdatedAt = now },
                new FeatureFlag { Key = "scanner_weight_set", Value = "default", CreatedAt = now, UpdatedAt = now });
        if (!await db.KillSwitch.AnyAsync(ct))
            db.KillSwitch.Add(new KillSwitchState { IsOn = true, CreatedAt = now, UpdatedAt = now });   // starts HALTED (fail safe)
        await db.SaveChangesAsync(ct);
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
