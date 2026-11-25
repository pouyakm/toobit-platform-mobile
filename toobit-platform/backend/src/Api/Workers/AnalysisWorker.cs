using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Toobit.Platform.Application;
using Toobit.Platform.Infrastructure.Security;
using Toobit.Platform.MarketScanner;

namespace Toobit.Platform.Api.Workers;

public sealed class AnalysisOptions { public int IntervalSeconds { get; set; } = 60; public int MinCandles { get; set; } = 60; }

/// <summary>
/// Every interval, for each scanner Top-N symbol with enough candles, asks the Python service to analyze and PUBLISH the signal
/// back to /internal/signals (Python -> .NET, HMAC). Python is down => logged, no signals, existing exchange-side SL/TP remain.
/// The correlation id is minted here and travels .NET -> Python -> .NET (X-Correlation-Id).
/// </summary>
public sealed class AnalysisWorker(IHttpClientFactory http, ScannerState scanner, CandleBuffer candles, ISecretProvider secrets,
    IClock clock, Microsoft.Extensions.Options.IOptions<AnalysisOptions> opt, ILogger<AnalysisWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var sym in scanner.Top.Select(t => t.Symbol).ToList())
            {
                var c = candles.Get(sym);
                if (c.Count < opt.Value.MinCandles) continue;
                try { await AnalyzeAsync(sym, c, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex) { log.LogWarning("Analysis call failed for {Symbol}: {Type}", sym, ex.GetType().Name); }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(opt.Value.IntervalSeconds), ct); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task AnalyzeAsync(string symbol, IReadOnlyList<Candle> c, CancellationToken ct)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            symbol, candles = c.Select(x => new { open = x.Open, high = x.High, low = x.Low, close = x.Close, volume = x.Volume, ts = x.OpenTimeMs }),
            in_position = false,
        });
        var path = $"/signal/{symbol}";
        var ts = clock.UtcNow.ToUnixTimeSeconds().ToString();
        var key = Encoding.UTF8.GetBytes(secrets.GetRequired("INTERNAL_HMAC_KEY"));
        using var req = new HttpRequestMessage(HttpMethod.Post, path + "?publish=true") { Content = new ByteArrayContent(body) };
        req.Content.Headers.ContentType = new("application/json");
        req.Headers.Add("X-Key-Id", secrets.GetRequired("INTERNAL_HMAC_KEY_ID"));
        req.Headers.Add("X-Timestamp", ts);
        req.Headers.Add("X-Signature", InternalHmacVerifier.Sign(key, "POST", path, ts, body));   // path only: the query string is not signed
        req.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString());
        using var resp = await http.CreateClient("analytics").SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) log.LogWarning("Analytics returned {Status} for {Symbol}", (int)resp.StatusCode, symbol);
    }
}
