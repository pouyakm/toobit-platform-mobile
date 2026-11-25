using Toobit.Platform.Domain.Common;

namespace Toobit.Platform.Domain.Auth;

/// <summary>PII policy: the email is stored as an HMAC hash (lookup) plus an AES-GCM ciphertext (display). Never plaintext.</summary>
public sealed class User : Entity
{
    public string EmailHash { get; set; } = "";
    public string EmailEncrypted { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Viewer";          // Admin | Trader | Viewer
    public bool Active { get; set; } = true;
    public int FailedLogins { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
}

public enum RefreshOutcome { Valid, Expired, Reused, Revoked }

/// <summary>Single-use refresh token. Only a SHA-256 hash is stored. Presenting a used token = theft signal: revoke the whole family.</summary>
public sealed class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedById { get; set; }

    public RefreshOutcome Evaluate(DateTimeOffset now)
    {
        if (RevokedAt is not null) return RefreshOutcome.Revoked;
        if (UsedAt is not null) return RefreshOutcome.Reused;      // reuse detection
        if (now >= ExpiresAt) return RefreshOutcome.Expired;
        return RefreshOutcome.Valid;
    }
}
