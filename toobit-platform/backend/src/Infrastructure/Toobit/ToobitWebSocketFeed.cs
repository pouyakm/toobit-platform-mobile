using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Toobit.Platform.Application;

namespace Toobit.Platform.Infrastructure.Toobit;

public sealed class WsOptions
{
    public string Url { get; set; } = "wss://stream.toobit.com/quote/ws/v1";
    public string KlineInterval { get; set; } = "1h";
    public int PingSeconds { get; set; } = 30;
    public int ResubscribeCheckSeconds { get; set; } = 30;
    public int MaxSymbols { get; set; } = 20;
}

/// <summary>Pure message parsing (unit-tested). Protocol verified against api-docs.toobit.com "USDT-M Websocket Market Streams".</summary>
public static class ToobitWsProtocol
{
    public static string Subscribe(string topic, IEnumerable<string> symbols, string ev = "sub") =>
        JsonSerializer.Serialize(new { symbol = string.Join(",", symbols), topic, @event = ev, @params = new { binary = "false" } });

    public static string Ping(long nowMs) => $"{{\"ping\":{nowMs}}}";

    public sealed record Parsed(PriceTick? Tick, (string Symbol, Candle Candle)? Kline, bool IsPong);

    public static Parsed Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.TryGetProperty("pong", out _)) return new(null, null, true);
        if (!r.TryGetProperty("topic", out var topic) || !r.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
            return new(null, null, false);
        var d = data[data.GetArrayLength() - 1];
        string sym = S(d, "s") ?? S(r, "symbol") ?? "";
        long t = d.TryGetProperty("t", out var tv) && tv.TryGetInt64(out var tl) ? tl : 0;
        switch (topic.GetString())
        {
            case "realtimes":
                return new(new PriceTick(sym, D(d, "c"), D(d, "o"), D(d, "h"), D(d, "l"), D(d, "qv"), DateTimeOffset.FromUnixTimeMilliseconds(t)), null, false);
            case "kline":
                return new(null, (sym, new Candle(t, D(d, "o"), D(d, "h"), D(d, "l"), D(d, "c"), D(d, "v"))), false);
            default: return new(null, null, false);
        }
    }
    private static string? S(JsonElement e, string n) => e.TryGetProperty(n, out var v) ? v.GetString() : null;
    private static decimal D(JsonElement e, string n) => e.TryGetProperty(n, out var v) && decimal.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0m;
}

/// <summary>
/// Public market-data feed (no credentials). Reconnects forever with jittered exponential backoff; every reconnect is
/// counted (ws_reconnect_total) and staleness is exposed via market_data_age_seconds + PriceBook.IsStale (dashboard banner).
/// Subscriptions follow the scanner's current Top-N (symbols come from <paramref name="symbolSource"/>).
/// </summary>
public sealed class ToobitWebSocketFeed(PriceBook prices, CandleBuffer candles, Func<IReadOnlyList<string>> symbolSource,
    IOptions<WsOptions> options, ILogger<ToobitWebSocketFeed> log) : BackgroundService
{
    private readonly WsOptions _o = options.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        int attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(ct);
                attempt = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogWarning("WS connection lost ({Type}); reconnecting", ex.GetType().Name); }

            prices.WsConnected = false;
            PlatformMetrics.WsReconnects.Add(1);
            attempt++;
            var wait = Backoff.Compute(attempt, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), null, Random.Shared.NextDouble);
            try { await Task.Delay(wait, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunConnectionAsync(CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(_o.Url), ct);
        prices.WsConnected = true;
        log.LogInformation("Toobit WS connected");
        var subscribed = new HashSet<string>();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var pump = Task.Run(() => ReceiveLoopAsync(ws, linked.Token), linked.Token);
        var nextPing = DateTimeOffset.UtcNow; var nextSub = DateTimeOffset.UtcNow;
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open && !pump.IsCompleted)
        {
            var now = DateTimeOffset.UtcNow;
            if (now >= nextSub)
            {
                var want = symbolSource().Take(_o.MaxSymbols).ToHashSet();
                var add = want.Except(subscribed).ToList();
                if (add.Count > 0)
                {
                    await SendAsync(ws, ToobitWsProtocol.Subscribe("realtimes", add), ct);
                    await SendAsync(ws, ToobitWsProtocol.Subscribe($"kline_{_o.KlineInterval}", add), ct);
                    subscribed.UnionWith(add);
                }
                nextSub = now.AddSeconds(_o.ResubscribeCheckSeconds);
            }
            if (now >= nextPing) { await SendAsync(ws, ToobitWsProtocol.Ping(now.ToUnixTimeMilliseconds()), ct); nextPing = now.AddSeconds(_o.PingSeconds); }
            await Task.Delay(500, ct);
        }
        linked.Cancel();
        try { await pump; } catch (OperationCanceledException) { }
        if (ws.State == WebSocketState.Open)      // graceful shutdown: close the socket
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None); } catch { }
        if (!ct.IsCancellationRequested) throw new WebSocketException("connection closed by server");
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buf = new byte[16 * 1024];
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream(); WebSocketReceiveResult r;
            do { r = await ws.ReceiveAsync(buf, ct); ms.Write(buf, 0, r.Count); } while (!r.EndOfMessage);
            if (r.MessageType == WebSocketMessageType.Close) return;
            try
            {
                var p = ToobitWsProtocol.Parse(Encoding.UTF8.GetString(ms.ToArray()));
                if (p.Tick is { } t) prices.Update(t);
                if (p.Kline is { } k) candles.Upsert(k.Symbol, k.Candle);
            }
            catch (JsonException) { log.LogDebug("Ignoring unparsable WS frame"); }
        }
    }

    private static Task SendAsync(ClientWebSocket ws, string msg, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, ct);
}
