using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Toobit.Platform.Application;
using Toobit.Platform.Domain.Market;

namespace Toobit.Platform.Infrastructure.Toobit;

/// <summary>
/// Scanner data source: exactly GET /quote/v1/contract/ticker/24hr (public, UNSIGNED, no API key header).
/// Uses its own named HttpClient + resilience pipeline so a scanner storm cannot starve trading requests.
/// Field names are matched tolerantly (short and long forms) because the response schema must be confirmed
/// against a live/recorded sample; see the WireMock fixture in the integration tests.
/// </summary>
public sealed class ToobitScannerExchange(IHttpClientFactory httpFactory, ILogger<ToobitScannerExchange> log) : IScannerExchange
{
    public const string HttpClientName = "toobit-scanner";
    public const string Path = "/quote/v1/contract/ticker/24hr";

    public async Task<IReadOnlyList<TickerSnapshot>> GetTickersAsync(CancellationToken ct)
    {
        using var resp = await httpFactory.CreateClient(HttpClientName).GetAsync(Path, ct);
        var rl = RateLimitInfo.From(resp);
        if (!resp.IsSuccessStatusCode)
        {
            if ((int)resp.StatusCode is 429 or 418)
                PlatformMetrics.RateLimitHits.Add(1, new KeyValuePair<string, object?>("status", ((int)resp.StatusCode).ToString()));
            log.LogWarning("Scanner HTTP {Status}; limit-status={LimitStatus} reset={Reset}", (int)resp.StatusCode, rl.Status, rl.ResetAt);
            resp.EnsureSuccessStatusCode();
        }
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return Parse(doc.RootElement);
    }

    public static IReadOnlyList<TickerSnapshot> Parse(JsonElement root)
    {
        var arr = root.ValueKind == JsonValueKind.Array ? root
                : root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array ? d : default;
        var list = new List<TickerSnapshot>();
        if (arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in arr.EnumerateArray())
        {
            string? sym = Str(e, "s", "symbol");
            if (sym is null || !sym.EndsWith("-SWAP-USDT", StringComparison.Ordinal)) continue;   // filter *-SWAP-USDT
            if (Dec(e, "qv", "quoteVolume") is not { } qv) continue;
            decimal last = Dec(e, "c", "lastPrice") ?? 0m;
            decimal pct = Dec(e, "pcp", "priceChangePercent") ?? 0m;
            list.Add(new TickerSnapshot(sym, last, qv, pct));
        }
        return list;
    }

    private static string? Str(JsonElement e, params string[] names)
    {
        foreach (var n in names) if (e.TryGetProperty(n, out var v)) return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
        return null;
    }
    private static decimal? Dec(JsonElement e, params string[] names) =>
        decimal.TryParse(Str(e, names), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
}
