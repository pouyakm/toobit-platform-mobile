using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Toobit.Platform.Api.Observability;
using Toobit.Platform.Application;
using Toobit.Platform.Domain.Auth;
using Toobit.Platform.Infrastructure.Persistence;
using Toobit.Platform.Infrastructure.Security;

namespace Toobit.Platform.Api.Controllers;

[ApiController, Route("api/auth"), AllowAnonymous]
public sealed class AuthController(IDbContextFactory<AppDbContext> dbf, TokenService tokens, PiiProtector pii, IAuditLog audit, IClock clock) : ControllerBase
{
    public sealed record LoginRequest(string Email, string Password);
    public sealed record RefreshRequest(string RefreshToken);
    public sealed record TokenResponse(string AccessToken, DateTimeOffset AccessExpires, string RefreshToken, string Role);

    private const int MaxFailures = 5;
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15), RefreshLifetime = TimeSpan.FromDays(7);

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest r, CancellationToken ct)
    {
        var corr = CorrelationMiddleware.Get(HttpContext); var now = clock.UtcNow;
        await using var db = await dbf.CreateDbContextAsync(ct);
        var hash = pii.LookupHash(r.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.EmailHash == hash, ct);

        if (user is null || !user.Active || (user.LockedUntil is { } l && l > now) || !PasswordHasher.Verify(r.Password, user.PasswordHash))
        {
            if (user is not null)
            {
                user.FailedLogins++;
                if (user.FailedLogins >= MaxFailures) { user.LockedUntil = now + Lockout; user.FailedLogins = 0; }
                user.UpdatedAt = now; await db.SaveChangesAsync(ct);
            }
            await audit.AppendAsync(hash[..12], "auth.login_failed", "invalid credentials", corr, ct);   // hash prefix, never the email
            return Unauthorized();     // same response for unknown user / bad password / locked: no enumeration
        }
        user.FailedLogins = 0; user.UpdatedAt = now;
        var resp = await IssueAsync(db, user, Guid.NewGuid(), now, ct);
        await audit.AppendAsync(user.Id.ToString(), "auth.login", "ok", corr, ct);
        return Ok(resp);
    }

    /// <summary>Rotation: each refresh token works once. Reuse of a spent token revokes the entire family.</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest r, CancellationToken ct)
    {
        var corr = CorrelationMiddleware.Get(HttpContext); var now = clock.UtcNow;
        await using var db = await dbf.CreateDbContextAsync(ct);
        var hash = TokenService.HashRefresh(r.RefreshToken);
        var tok = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (tok is null) return Unauthorized();

        var outcome = tok.Evaluate(now);
        if (outcome == RefreshOutcome.Reused)
        {
            foreach (var t in await db.RefreshTokens.Where(t => t.FamilyId == tok.FamilyId && t.RevokedAt == null).ToListAsync(ct)) t.RevokedAt = now;
            await db.SaveChangesAsync(ct);
            await audit.AppendAsync(tok.UserId.ToString(), "auth.refresh_reuse_detected", $"family {tok.FamilyId} revoked", corr, ct);
            return Unauthorized();
        }
        if (outcome != RefreshOutcome.Valid) return Unauthorized();

        var user = await db.Users.FirstAsync(u => u.Id == tok.UserId, ct);
        if (!user.Active) return Unauthorized();
        tok.UsedAt = now;
        var resp = await IssueAsync(db, user, tok.FamilyId, now, ct, tok);
        return Ok(resp);
    }

    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest r, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var tok = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == TokenService.HashRefresh(r.RefreshToken), ct);
        if (tok is not null)
        {
            foreach (var t in await db.RefreshTokens.Where(t => t.FamilyId == tok.FamilyId && t.RevokedAt == null).ToListAsync(ct)) t.RevokedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    private async Task<TokenResponse> IssueAsync(AppDbContext db, User user, Guid family, DateTimeOffset now, CancellationToken ct, RefreshToken? previous = null)
    {
        var (raw, hash) = TokenService.NewRefreshToken();
        var next = new RefreshToken { UserId = user.Id, FamilyId = family, TokenHash = hash, ExpiresAt = now + RefreshLifetime, CreatedAt = now, UpdatedAt = now };
        db.RefreshTokens.Add(next);
        if (previous is not null) previous.ReplacedById = next.Id;
        await db.SaveChangesAsync(ct);
        var (access, exp) = tokens.IssueAccess(user.Id, pii.Decrypt(user.EmailEncrypted), user.Role, now);
        return new TokenResponse(access, exp, raw, user.Role);
    }
}
