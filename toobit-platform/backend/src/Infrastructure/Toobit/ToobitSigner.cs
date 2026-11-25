using System.Security.Cryptography;
using System.Text;

namespace Toobit.Platform.Infrastructure.Toobit;

/// <summary>
/// Toobit HMAC-SHA256 request signing (verified against api-docs.toobit.com "Code Examples").
///  * Form/query endpoints: sign the parameters as key=value joined by '&amp;' IN THE ORDER THEY ARE SENT,
///    excluding "signature"; append "&amp;signature=&lt;lowercase hex&gt;".
///  * JSON-body endpoints (API v2): sign (query string without signature) + (raw JSON body), concatenated with
///    NO separator. The body bytes sent MUST be identical to the bytes signed.
/// Parameter order matters: the same pairs in a different order produce a different signature (unit-tested).
/// The secret is never logged or included in exceptions.
/// </summary>
public static class ToobitSigner
{
    public static string Sign(string secret, string payload)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    /// <summary>Serializes pairs in the given order. Values are URL-encoded identically for signing and sending.</summary>
    public static string BuildQuery(IEnumerable<KeyValuePair<string, string>> pairs) =>
        string.Join("&", pairs.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

    /// <summary>Returns "query&amp;signature=..." for GET / form endpoints.</summary>
    public static string SignQuery(string secret, IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var q = BuildQuery(pairs);
        return $"{q}&signature={Sign(secret, q)}";
    }

    /// <summary>Returns the signed query string for JSON-body endpoints; the caller sends <paramref name="jsonBody"/> verbatim.</summary>
    public static string SignQueryWithJsonBody(string secret, IEnumerable<KeyValuePair<string, string>> pairs, string jsonBody)
    {
        var q = BuildQuery(pairs);
        return $"{q}&signature={Sign(secret, q + jsonBody)}";
    }
}
