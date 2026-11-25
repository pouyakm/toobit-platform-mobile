using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Toobit.Platform.Infrastructure.Security;

/// <summary>PBKDF2-HMAC-SHA256, 600k iterations (OWASP 2023), per-user random salt, constant-time compare.</summary>
public static class PasswordHasher
{
    private const int Iterations = 600_000, SaltBytes = 16, HashBytes = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var h = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(h)}";
    }

    public static bool Verify(string password, string stored)
    {
        var p = stored.Split('.');
        if (p.Length != 4 || p[0] != "v1" || !int.TryParse(p[1], out var it)) return false;
        var expected = Convert.FromBase64String(p[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(p[2]), it, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}

/// <summary>AES-256-GCM for PII at rest + a keyed hash for lookups. Key (32 bytes, base64) comes from ISecretProvider.</summary>
public sealed class PiiProtector(byte[] key)
{
    public static PiiProtector FromBase64(string b64)
    {
        var k = Convert.FromBase64String(b64);
        return k.Length == 32 ? new PiiProtector(k) : throw new ArgumentException("PII key must be 32 bytes");
    }

    public string LookupHash(string value) =>
        Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()))).ToLowerInvariant();

    public string Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[data.Length]; var tag = new byte[16];
        using var gcm = new AesGcm(key, 16);
        gcm.Encrypt(nonce, data, cipher, tag);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    public string Decrypt(string blob)
    {
        var all = Convert.FromBase64String(blob);
        var plain = new byte[all.Length - 28];
        using var gcm = new AesGcm(key, 16);
        gcm.Decrypt(all[..12], all[28..], all[12..28], plain);
        return Encoding.UTF8.GetString(plain);
    }
}

public sealed record JwtSettings(string Issuer, string Audience, byte[] SigningKey, TimeSpan AccessLifetime)
{
    public const int MaxAccessMinutes = 15;      // security requirement
    public JwtSettings Validate() => AccessLifetime > TimeSpan.FromMinutes(MaxAccessMinutes) || AccessLifetime <= TimeSpan.Zero
        ? throw new ArgumentException($"access token lifetime must be in (0, {MaxAccessMinutes}] minutes") : this;
}

public sealed class TokenService(JwtSettings settings)
{
    public (string Token, DateTimeOffset Expires) IssueAccess(Guid userId, string email, string role, DateTimeOffset now)
    {
        var exp = now + settings.Validate().AccessLifetime;
        var jwt = new JwtSecurityToken(settings.Issuer, settings.Audience,
            new[] { new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim(ClaimTypes.Name, email), new Claim(ClaimTypes.Role, role) },
            now.UtcDateTime, exp.UtcDateTime, new SigningCredentials(new SymmetricSecurityKey(settings.SigningKey), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(jwt), exp);
    }

    /// <summary>256-bit random opaque token; store only its hash.</summary>
    public static (string Raw, string Hash) NewRefreshToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (raw, HashRefresh(raw));
    }
    public static string HashRefresh(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
}
