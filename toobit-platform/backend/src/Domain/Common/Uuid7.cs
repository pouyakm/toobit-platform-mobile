using System.Security.Cryptography;

namespace Toobit.Platform.Domain.Common;

/// <summary>RFC 9562 UUIDv7 (48-bit unix-ms timestamp + random). Guid.CreateVersion7 only exists in .NET 9.</summary>
public static class Uuid7
{
    public static Guid NewGuid(DateTimeOffset? now = null)
    {
        long ms = (now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();
        Span<byte> b = stackalloc byte[16];
        RandomNumberGenerator.Fill(b);
        // Big-endian timestamp in bytes 0..5
        b[0] = (byte)(ms >> 40); b[1] = (byte)(ms >> 32); b[2] = (byte)(ms >> 24);
        b[3] = (byte)(ms >> 16); b[4] = (byte)(ms >> 8);  b[5] = (byte)ms;
        b[6] = (byte)((b[6] & 0x0F) | 0x70); // version 7
        b[8] = (byte)((b[8] & 0x3F) | 0x80); // RFC variant
        // Guid(byte[], bigEndian: true) keeps the RFC byte order in the string form.
        return new Guid(b, bigEndian: true);
    }
}
