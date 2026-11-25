using Toobit.Platform.Domain.Auth;
using Toobit.Platform.Infrastructure.Security;
using Xunit;

namespace Toobit.UnitTests;

public class PasswordAndPiiTests
{
    [Fact]
    public void Hash_verifies_and_is_salted()
    {
        var a = PasswordHasher.Hash("correct horse battery"); var b = PasswordHasher.Hash("correct horse battery");
        Assert.NotEqual(a, b);                                       // random salt
        Assert.True(PasswordHasher.Verify("correct horse battery", a));
        Assert.False(PasswordHasher.Verify("wrong", a));
        Assert.False(PasswordHasher.Verify("x", "garbage"));
    }

    [Fact]
    public void Pii_is_encrypted_not_plaintext_and_roundtrips_and_detects_tampering()
    {
        var p = PiiProtector.FromBase64(Convert.ToBase64String(new byte[32].Select((_, i) => (byte)i).ToArray()));
        var blob = p.Encrypt("trader@example.com");
        Assert.DoesNotContain("trader", blob);
        Assert.Equal("trader@example.com", p.Decrypt(blob));
        var bytes = Convert.FromBase64String(blob); bytes[^1] ^= 1;
        Assert.ThrowsAny<Exception>(() => p.Decrypt(Convert.ToBase64String(bytes)));
        Assert.Equal(p.LookupHash(" Trader@Example.com "), p.LookupHash("trader@example.com"));   // normalized lookup
    }

    [Fact] public void Pii_key_must_be_32_bytes() => Assert.Throws<ArgumentException>(() => PiiProtector.FromBase64(Convert.ToBase64String(new byte[16])));
}

public class TokenTests
{
    private static JwtSettings S(TimeSpan life) => new("iss", "aud", new byte[32].Select((_, i) => (byte)(i + 1)).ToArray(), life);

    [Fact] public void Access_lifetime_over_15_minutes_is_rejected() =>
        Assert.Throws<ArgumentException>(() => new TokenService(S(TimeSpan.FromMinutes(16))).IssueAccess(Guid.NewGuid(), "a@b.c", "Admin", DateTimeOffset.UtcNow));

    [Fact] public void Access_token_issued_within_limit() =>
        Assert.False(string.IsNullOrEmpty(new TokenService(S(TimeSpan.FromMinutes(15))).IssueAccess(Guid.NewGuid(), "a@b.c", "Viewer", DateTimeOffset.UtcNow).Token));

    [Fact]
    public void Refresh_tokens_are_random_and_stored_hashed()
    {
        var (raw1, h1) = TokenService.NewRefreshToken(); var (raw2, _) = TokenService.NewRefreshToken();
        Assert.NotEqual(raw1, raw2); Assert.NotEqual(raw1, h1); Assert.Equal(h1, TokenService.HashRefresh(raw1));
    }

    [Fact]
    public void Refresh_evaluation_covers_valid_expired_reused_revoked()
    {
        var now = DateTimeOffset.UtcNow;
        var t = new RefreshToken { ExpiresAt = now.AddDays(1) };
        Assert.Equal(RefreshOutcome.Valid, t.Evaluate(now));
        Assert.Equal(RefreshOutcome.Expired, t.Evaluate(now.AddDays(2)));
        t.UsedAt = now; Assert.Equal(RefreshOutcome.Reused, t.Evaluate(now));       // reuse detection
        t.RevokedAt = now; Assert.Equal(RefreshOutcome.Revoked, t.Evaluate(now));
    }
}

public class SecretHandlingTests
{
    private sealed class MapSecrets(Dictionary<string, string> m) : Toobit.Platform.Application.ISecretProvider
    {
        public string GetRequired(string n) => m.TryGetValue(n, out var v) ? v : throw new InvalidOperationException($"Required secret '{n}' is not set");
    }

    [Fact]
    public void Startup_validation_fails_fast_and_names_missing_keys_but_never_values()
    {
        var s = new MapSecrets(new() { ["TOOBIT_API_KEY"] = "super-secret-value" });
        var ex = Assert.Throws<InvalidOperationException>(() => SecretValidation.ValidateOrThrow(s));
        Assert.Contains("TOOBIT_API_SECRET", ex.Message);
        Assert.DoesNotContain("super-secret-value", ex.Message);
    }
}
