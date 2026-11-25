using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Toobit.Platform.Infrastructure.Security;

public sealed record InternalKey(string KeyId, byte[] Secret, IReadOnlySet<string> Scopes);

public enum HmacResult { Ok, UnknownKey, BadTimestamp, Skew, BadSignature, Replay, ScopeDenied }

/// <summary>
/// HMAC auth for the internal .NET &lt;-&gt; Python channel. MUST stay byte-compatible with analytics/app/security.py:
///   canonical = METHOD \n PATH \n TIMESTAMP \n SHA256_HEX(body)    signature = hex(HMAC-SHA256(key, canonical))
/// Per-route authorization is by key scope (e.g. "signals:write"). Replay protection: skew window + seen-signature cache.
/// </summary>
public sealed class InternalHmacVerifier(IReadOnlyDictionary<string, InternalKey> keys, TimeSpan maxSkew)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new();

    public static string Canonical(string method, string path, string timestamp, byte[] body) =>
        string.Join("\n", method.ToUpperInvariant(), path, timestamp, Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant());

    public static string Sign(byte[] key, string method, string path, string timestamp, byte[] body) =>
        Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(Canonical(method, path, timestamp, body)))).ToLowerInvariant();

    public HmacResult Verify(string keyId, string timestamp, string signature, string method, string path,
        byte[] body, string requiredScope, DateTimeOffset now)
    {
        if (!keys.TryGetValue(keyId, out var key)) return HmacResult.UnknownKey;
        if (!double.TryParse(timestamp, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ts))
            return HmacResult.BadTimestamp;
        if (Math.Abs((now - DateTimeOffset.FromUnixTimeMilliseconds((long)(ts * 1000))).TotalSeconds) > maxSkew.TotalSeconds)
            return HmacResult.Skew;

        var expected = Encoding.ASCII.GetBytes(Sign(key.Secret, method, path, timestamp, body));
        var given = Encoding.ASCII.GetBytes(signature ?? "");
        if (!CryptographicOperations.FixedTimeEquals(expected, given)) return HmacResult.BadSignature;   // constant-time

        if (!key.Scopes.Contains(requiredScope)) return HmacResult.ScopeDenied;
        if (!_seen.TryAdd(signature!, now)) return HmacResult.Replay;
        foreach (var old in _seen.Where(kv => now - kv.Value > maxSkew * 2).Select(kv => kv.Key).ToList())
            _seen.TryRemove(old, out _);
        return HmacResult.Ok;
    }
}
