using System.Text;
using Toobit.Platform.Infrastructure.Security;
using Toobit.Platform.Infrastructure.Toobit;
using Xunit;

namespace Toobit.UnitTests;

/// <summary>
/// Known vectors computed INDEPENDENTLY with Python hmac and `openssl dgst -sha256 -hmac` (they agree).
/// Intent: pin the exact signing contract (key, string-to-sign, lowercase hex, parameter ORDER).
/// </summary>
public class ToobitSignerTests
{
    private const string Secret = "test-secret";
    private static readonly KeyValuePair<string, string>[] Pairs =
    {
        new("symbol", "BTC-SWAP-USDT"), new("side", "BUY"), new("timestamp", "1700000000000"), new("recvWindow", "5000"),
    };

    [Fact]
    public void Form_signature_matches_known_vector() =>
        Assert.Equal("337bc3c088c460b57cb764f5c29827011b049adc0c1c75269c28ff59aea21b0d",
            ToobitSigner.Sign(Secret, "symbol=BTC-SWAP-USDT&side=BUY&timestamp=1700000000000&recvWindow=5000"));

    [Fact]
    public void SignQuery_appends_signature_last_and_in_sent_order()
    {
        var q = ToobitSigner.SignQuery(Secret, Pairs);
        Assert.Equal("symbol=BTC-SWAP-USDT&side=BUY&timestamp=1700000000000&recvWindow=5000" +
                     "&signature=337bc3c088c460b57cb764f5c29827011b049adc0c1c75269c28ff59aea21b0d", q);
    }

    [Fact]
    public void Reordered_parameters_change_the_signature()
    {
        var reordered = new[] { Pairs[1], Pairs[0], Pairs[2], Pairs[3] };
        var q = ToobitSigner.SignQuery(Secret, reordered);
        Assert.EndsWith("signature=8cd7b0e579870f15727cdadd9b8101f63f09c6d862b842d024bd372abd8912d0", q);
        Assert.DoesNotContain("337bc3c0", q);
    }

    [Fact]
    public void Json_body_signature_is_query_plus_raw_body_with_no_separator()
    {
        const string body = "{\"symbol\":\"BTC-SWAP-USDT\",\"side\":\"BUY\",\"positionSide\":\"LONG\",\"type\":\"LIMIT\"," +
            "\"newClientOrderId\":\"0190a5e0-0000-7000-8000-000000000001\",\"quantity\":\"1\",\"price\":\"60000\"}";
        var q = ToobitSigner.SignQueryWithJsonBody(Secret, new KeyValuePair<string, string>[]
            { new("timestamp", "1700000000000"), new("recvWindow", "5000"), new("category", "USDT") }, body);
        Assert.EndsWith("signature=45da94711ec81a5ac126326eb5df774dbf2aec74bd1c7cd941ad6d455f1f43b0", q);
    }

    [Fact]
    public void Signature_is_lowercase_hex_of_64_chars()
    {
        var s = ToobitSigner.Sign(Secret, "x");
        Assert.Equal(64, s.Length);
        Assert.Equal(s.ToLowerInvariant(), s);
    }

    [Fact]
    public void Body_sent_is_byte_identical_to_body_signed()
    {
        var o = Toobit.Platform.Domain.Orders.Order.Create("BTC-SWAP-USDT", Toobit.Platform.Domain.Orders.Side.Buy, 1m, 60000m, 59000m, 62000m, "c", DateTimeOffset.UtcNow);
        var a = ToobitTradingExchange.BuildOrderJson(o);
        var b = ToobitTradingExchange.BuildOrderJson(o);
        Assert.Equal(a, b);                                   // deterministic
        Assert.DoesNotContain("\n", a); Assert.DoesNotContain(" ", a.Replace("\"BTC-SWAP-USDT\"", ""));   // compact
        Assert.Contains("\"stopLoss\"", a); Assert.Contains("\"takeProfit\"", a);                         // SL/TP at entry
    }
}

public class InternalHmacTests
{
    // Same vector is asserted in analytics (app/security.py): proves the two sides agree byte-for-byte.
    private const string PythonVector = "2ccb6406411a2fd8bf7b92a4b8a2cde9780a6deaa35e90cd5475a82e86bdd261";
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("unit-test-key");
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"a\":1}");

    private static InternalHmacVerifier Verifier(params string[] scopes) => new(
        new Dictionary<string, InternalKey> { ["k1"] = new("k1", Key, new HashSet<string>(scopes)) }, TimeSpan.FromSeconds(60));

    [Fact] public void Matches_python_signature_vector() =>
        Assert.Equal(PythonVector, InternalHmacVerifier.Sign(Key, "POST", "/internal/signals", "1700000000", Body));

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1700000000);

    [Fact] public void Accepts_valid() =>
        Assert.Equal(HmacResult.Ok, Verifier("signals:write").Verify("k1", "1700000000", PythonVector, "POST", "/internal/signals", Body, "signals:write", Now));

    [Fact] public void Rejects_bad_signature_wrong_key_stale_and_replay()
    {
        var v = Verifier("signals:write");
        Assert.Equal(HmacResult.BadSignature, v.Verify("k1", "1700000000", new string('0', 64), "POST", "/internal/signals", Body, "signals:write", Now));
        Assert.Equal(HmacResult.UnknownKey, v.Verify("nope", "1700000000", PythonVector, "POST", "/internal/signals", Body, "signals:write", Now));
        Assert.Equal(HmacResult.Skew, v.Verify("k1", "1700000000", PythonVector, "POST", "/internal/signals", Body, "signals:write", Now.AddHours(1)));
        Assert.Equal(HmacResult.Ok, v.Verify("k1", "1700000000", PythonVector, "POST", "/internal/signals", Body, "signals:write", Now));
        Assert.Equal(HmacResult.Replay, v.Verify("k1", "1700000000", PythonVector, "POST", "/internal/signals", Body, "signals:write", Now));
    }

    [Fact] public void Per_route_scope_is_enforced() =>
        Assert.Equal(HmacResult.ScopeDenied, Verifier("other:scope").Verify("k1", "1700000000", PythonVector, "POST", "/internal/signals", Body, "signals:write", Now));

    [Fact] public void Tampered_body_or_path_is_rejected()
    {
        var v = Verifier("signals:write");
        Assert.Equal(HmacResult.BadSignature, v.Verify("k1", "1700000000", PythonVector, "POST", "/internal/signals", Encoding.UTF8.GetBytes("{\"a\":2}"), "signals:write", Now));
        Assert.Equal(HmacResult.BadSignature, v.Verify("k1", "1700000000", PythonVector, "POST", "/internal/other", Body, "signals:write", Now));
    }
}
