using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Toobit.Platform.Application;
using Toobit.Platform.Domain.Orders;

namespace Toobit.Platform.Infrastructure.Toobit;

public sealed class ToobitOptions
{
    public string BaseUrl { get; set; } = "https://api.toobit.com";
    public int RecvWindowMs { get; set; } = 5000;
    public string Category { get; set; } = "USDT";
    // Verified: POST/GET/DELETE /api/v2/futures/order, GET /api/v2/futures/open-orders (api-docs.toobit.com).
    public string OrderPath { get; set; } = "/api/v2/futures/order";
    public string OpenOrdersPath { get; set; } = "/api/v2/futures/open-orders";
    // UNVERIFIED: positions endpoint was not on the documentation page reviewed. Confirm before enabling live trading.
    public string PositionsPath { get; set; } = "/api/v2/futures/positions";
    // UNVERIFIED: error code Toobit returns for "order does not exist". Confirm against the error-codes page.
    public int OrderNotFoundCode { get; set; } = -2013;
}

/// <summary>
/// The ONLY component that sends signed requests to Toobit (trading budget; separate HttpClient from the scanner).
/// Credentials come from ISecretProvider at call time and never appear in logs or exceptions.
/// POST is never retried by an HTTP-level handler: ambiguous outcomes become <see cref="SubmitOutcome.Ambiguous"/>.
/// </summary>
public sealed class ToobitTradingExchange(
    IHttpClientFactory httpFactory, ISecretProvider secrets, IOptions<ToobitOptions> options, IClock clock,
    ILogger<ToobitTradingExchange> log) : ITradingExchange
{
    public const string HttpClientName = "toobit-trading";
    private readonly ToobitOptions _o = options.Value;

    private List<KeyValuePair<string, string>> Common() => new()
    {
        new("timestamp", clock.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)),
        new("recvWindow", _o.RecvWindowMs.ToString(CultureInfo.InvariantCulture)),
        new("category", _o.Category),
    };

    /// <summary>Builds the exact JSON body string once; the same string is signed and sent.</summary>
    internal static string BuildOrderJson(Order o)
    {
        // Entry: Buy->LONG, Sell->SHORT. Reduce-only close of LONG is a Sell on the LONG side (XOR rule).
        string positionSide = (o.Side == Side.Buy) ^ o.ReduceOnly ? "LONG" : "SHORT";
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))         // compact, deterministic property order, no whitespace
        {
            w.WriteStartObject();
            w.WriteString("symbol", o.Symbol);
            w.WriteString("side", o.Side == Side.Buy ? "BUY" : "SELL");
            w.WriteString("positionSide", positionSide);
            w.WriteString("type", o.LimitPrice is null ? "MARKET" : "LIMIT");
            w.WriteString("newClientOrderId", o.ClientOrderId.ToString());
            w.WriteString("quantity", o.Quantity.ToString(CultureInfo.InvariantCulture));
            if (o.LimitPrice is { } p) { w.WriteString("price", p.ToString(CultureInfo.InvariantCulture)); w.WriteString("timeInForce", "GTC"); }
            if (!o.ReduceOnly)    // SL/TP attached at entry, enforced exchange-side
            {
                w.WriteString("stopLoss", o.StopLoss.ToString(CultureInfo.InvariantCulture));
                w.WriteString("slTriggerBy", "MARK_PRICE");
                w.WriteString("slOrderType", "MARKET");
                w.WriteString("takeProfit", o.TakeProfit.ToString(CultureInfo.InvariantCulture));
                w.WriteString("tpTriggerBy", "MARK_PRICE");
                w.WriteString("tpOrderType", "MARKET");
            }
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    public async Task<SubmitResult> SubmitOrderAsync(Order order, CancellationToken ct)
    {
        string body = BuildOrderJson(order);
        string query = ToobitSigner.SignQueryWithJsonBody(secrets.GetRequired("TOOBIT_API_SECRET"), Common(), body);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_o.OrderPath}?{query}")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-BB-APIKEY", secrets.GetRequired("TOOBIT_API_KEY"));

        try
        {
            using var resp = await httpFactory.CreateClient(HttpClientName).SendAsync(req, ct);
            var rl = RateLimitInfo.From(resp);
            var cls = HttpClassifier.Classify((int)resp.StatusCode);
            switch (cls)
            {
                case HttpFailureClass.RateLimited:
                    return new SubmitResult(SubmitOutcome.RateLimited, RetryAfter: rl.RetryAfter(clock.UtcNow), Error: "HTTP 429");
                case HttpFailureClass.Banned:
                    return new SubmitResult(SubmitOutcome.Banned, Error: "HTTP 418");
                case HttpFailureClass.ServerError:
                    return new SubmitResult(SubmitOutcome.Ambiguous, Error: $"HTTP {(int)resp.StatusCode}");
            }
            var text = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            int code = root.TryGetProperty("code", out var c) && c.TryGetInt32(out var ci) ? ci : (cls == HttpFailureClass.None ? 200 : -1);
            if (cls == HttpFailureClass.None && code == 200)
                return new SubmitResult(SubmitOutcome.Acked, ToobitOrderId: FindString(root, "orderId") ?? "");
            return new SubmitResult(SubmitOutcome.Rejected, Error: $"exchange code {code}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            // Timeout / connection drop / unparsable body: the order MAY have been accepted. Do not mark failed.
            log.LogWarning("Submit ambiguous for {ClientOrderId}: {Type}", order.ClientOrderId, ex.GetType().Name);
            return new SubmitResult(SubmitOutcome.Ambiguous, Error: ex.GetType().Name);
        }
    }

    public async Task<QueryResult> QueryOrderAsync(Guid clientOrderId, CancellationToken ct)
    {
        var pairs = new List<KeyValuePair<string, string>> { new("origClientOrderId", clientOrderId.ToString()) };
        pairs.AddRange(Common());
        var (status, text) = await SignedGetAsync(_o.OrderPath, pairs, ct);
        if (status is null) return new QueryResult(QueryStatus.Error);
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (root.TryGetProperty("code", out var c) && c.TryGetInt32(out var code) && code == _o.OrderNotFoundCode)
            return new QueryResult(QueryStatus.NotFound);
        if (status != 200) return new QueryResult(QueryStatus.Error);
        return new QueryResult(QueryStatus.Found, ParseOrder(root, clientOrderId));
    }

    public async Task<IReadOnlyList<ExchangeOrder>> GetOpenOrdersAsync(CancellationToken ct)
    {
        var (status, text) = await SignedGetAsync(_o.OpenOrdersPath, Common(), ct);
        if (status != 200) throw new HttpRequestException($"open-orders failed: HTTP {status}");
        using var doc = JsonDocument.Parse(text);
        var list = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement
                 : doc.RootElement.TryGetProperty("data", out var d) ? d : default;
        var result = new List<ExchangeOrder>();
        if (list.ValueKind == JsonValueKind.Array)
            foreach (var e in list.EnumerateArray())
                if (Guid.TryParse(FindString(e, "clientOrderId") ?? FindString(e, "newClientOrderId"), out var id))
                    result.Add(ParseOrder(e, id));
        return result;
    }

    public async Task<IReadOnlyList<ExchangePosition>> GetPositionsAsync(CancellationToken ct)
    {
        var (status, text) = await SignedGetAsync(_o.PositionsPath, Common(), ct);
        if (status != 200) throw new HttpRequestException($"positions failed: HTTP {status}");
        using var doc = JsonDocument.Parse(text);
        var list = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement
                 : doc.RootElement.TryGetProperty("data", out var d) ? d : default;
        var result = new List<ExchangePosition>();
        if (list.ValueKind == JsonValueKind.Array)
            foreach (var e in list.EnumerateArray())
                if (decimal.TryParse(FindString(e, "position") ?? FindString(e, "quantity"), NumberStyles.Any, CultureInfo.InvariantCulture, out var q) && q != 0)
                    result.Add(new ExchangePosition(FindString(e, "symbol") ?? "", FindString(e, "side") ?? "", q));
        return result;
    }

    private async Task<(int? Status, string Body)> SignedGetAsync(string path, List<KeyValuePair<string, string>> pairs, CancellationToken ct)
    {
        string query = ToobitSigner.SignQuery(secrets.GetRequired("TOOBIT_API_SECRET"), pairs);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{path}?{query}");
        req.Headers.Add("X-BB-APIKEY", secrets.GetRequired("TOOBIT_API_KEY"));
        using var resp = await httpFactory.CreateClient(HttpClientName).SendAsync(req, ct);
        var rl = RateLimitInfo.From(resp);
        if (HttpClassifier.Classify((int)resp.StatusCode) is HttpFailureClass.RateLimited or HttpFailureClass.Banned)
        {
            PlatformMetrics.RateLimitHits.Add(1, new KeyValuePair<string, object?>("status", ((int)resp.StatusCode).ToString()));
            log.LogWarning("Rate limited on GET {Path}; status={Status} reset={Reset}", path, rl.Status, rl.ResetAt);
            return (null, "");
        }
        return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
    }

    private static ExchangeOrder ParseOrder(JsonElement e, Guid clientId)
    {
        var state = (FindString(e, "status") ?? "").ToUpperInvariant() switch
        {
            "NEW" => OrderState.Acked,
            "PARTIALLY_FILLED" => OrderState.PartiallyFilled,
            "FILLED" => OrderState.Filled,
            "CANCELED" or "CANCELLED" or "PARTIALLY_CANCELED" or "EXPIRED" => OrderState.Canceled,
            "REJECTED" => OrderState.Rejected,
            _ => OrderState.Acked,
        };
        return new ExchangeOrder(FindString(e, "orderId") ?? "", clientId, state);
    }

    private static string? FindString(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (e.TryGetProperty(name, out var v)) return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
        if (e.TryGetProperty("data", out var d)) return FindString(d, name);
        return null;
    }
}
