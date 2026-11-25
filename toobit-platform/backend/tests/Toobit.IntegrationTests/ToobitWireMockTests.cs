using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Toobit.Platform.Application;
using Toobit.Platform.Domain.Orders;
using Toobit.Platform.Infrastructure.Toobit;
using Toobit.UnitTests;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Toobit.IntegrationTests;

/// <summary>
/// Exercises the REAL ToobitTradingExchange / ToobitScannerExchange over HTTP against a mock Toobit (WireMock.Net).
/// This is the simulated exchange adapter for automated tests (no live trading, ADR default #3).
/// </summary>
public sealed class ToobitWireMockTests : IDisposable
{
    private readonly WireMockServer _srv = WireMockServer.Start();
    private readonly ServiceProvider _sp;

    private sealed class Secrets : ISecretProvider
    {
        public string GetRequired(string n) => n switch { "TOOBIT_API_KEY" => "test-key", "TOOBIT_API_SECRET" => "test-secret", _ => throw new InvalidOperationException(n) };
    }

    public ToobitWireMockTests()
    {
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<ISecretProvider, Secrets>();
        s.AddSingleton<IClock>(new FakeClock());
        s.Configure<ToobitOptions>(o => o.BaseUrl = _srv.Url!);
        s.AddHttpClient(ToobitTradingExchange.HttpClientName, c => c.BaseAddress = new Uri(_srv.Url!));
        s.AddHttpClient(ToobitScannerExchange.HttpClientName, c => c.BaseAddress = new Uri(_srv.Url!));
        s.AddSingleton<ToobitTradingExchange>();
        s.AddSingleton<ToobitScannerExchange>();
        _sp = s.BuildServiceProvider();
    }

    private static Order NewOrder() => Order.Create("BTC-SWAP-USDT", Side.Buy, 0.01m, 60_000m, 59_400m, 61_200m, "corr", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Scanner_calls_exactly_the_public_ticker_endpoint_with_no_signature_or_api_key()
    {
        _srv.Given(Request.Create().WithPath("/quote/v1/contract/ticker/24hr").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""[{"s":"BTC-SWAP-USDT","c":"60000","qv":"5000000","pcp":"2"},{"s":"BTCUSDT","qv":"1"}]"""));

        var t = await _sp.GetRequiredService<ToobitScannerExchange>().GetTickersAsync(default);

        Assert.Single(t);
        var req = Assert.Single(_srv.LogEntries).RequestMessage;
        Assert.DoesNotContain("signature", req.Url, StringComparison.OrdinalIgnoreCase);
        Assert.False(req.Headers?.ContainsKey("X-BB-APIKEY") ?? false);
    }

    [Fact]
    public async Task Submit_sends_apikey_header_signed_query_and_json_body_and_acks()
    {
        _srv.Given(Request.Create().WithPath("/api/v2/futures/order").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"code":200,"data":{"orderId":"777"}}"""));
        var order = NewOrder();

        var r = await _sp.GetRequiredService<ToobitTradingExchange>().SubmitOrderAsync(order, default);

        Assert.Equal(SubmitOutcome.Acked, r.Outcome); Assert.Equal("777", r.ToobitOrderId);
        var e = Assert.Single(_srv.LogEntries).RequestMessage;
        Assert.Equal("test-key", e.Headers!["X-BB-APIKEY"].Single());
        // Recompute the signature independently from what was actually sent on the wire.
        var query = e.RawQuery!.TrimStart('?'); var sig = query[(query.IndexOf("&signature=", StringComparison.Ordinal) + 11)..];
        var signedPart = query[..query.IndexOf("&signature=", StringComparison.Ordinal)];
        Assert.Equal(ToobitSigner.Sign("test-secret", signedPart + e.Body), sig);
        using var body = JsonDocument.Parse(e.Body!);
        Assert.Equal(order.ClientOrderId.ToString(), body.RootElement.GetProperty("newClientOrderId").GetString());
        Assert.True(body.RootElement.TryGetProperty("stopLoss", out _) && body.RootElement.TryGetProperty("takeProfit", out _));
    }

    [Theory]
    [InlineData(429, SubmitOutcome.RateLimited)]
    [InlineData(418, SubmitOutcome.Banned)]
    [InlineData(500, SubmitOutcome.Ambiguous)]
    [InlineData(503, SubmitOutcome.Ambiguous)]
    public async Task Http_failure_classes_are_never_conflated(int status, SubmitOutcome expected)   // acceptance #6
    {
        _srv.Given(Request.Create().WithPath("/api/v2/futures/order").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("X-Api-Limit-Reset-Timestamp", DateTimeOffset.UtcNow.AddSeconds(3).ToUnixTimeMilliseconds().ToString()));
        var r = await _sp.GetRequiredService<ToobitTradingExchange>().SubmitOrderAsync(NewOrder(), default);
        Assert.Equal(expected, r.Outcome);
        if (status == 429) Assert.NotNull(r.RetryAfter);
        Assert.Single(_srv.LogEntries);                                   // exactly one POST: never auto-retried
    }

    [Fact]
    public async Task Connection_timeout_is_ambiguous_not_failed()
    {
        _srv.Given(Request.Create().WithPath("/api/v2/futures/order").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(3)));
        var http = _sp.GetRequiredService<IHttpClientFactory>();
        using var cts = new CancellationTokenSource();
        var svc = new ToobitTradingExchange(new TimeoutFactory(http), new Secrets(), Options.Create(new ToobitOptions { BaseUrl = _srv.Url! }), new FakeClock(), NullLogger<ToobitTradingExchange>.Instance);
        var r = await svc.SubmitOrderAsync(NewOrder(), default);
        Assert.Equal(SubmitOutcome.Ambiguous, r.Outcome);
    }

    [Fact]
    public async Task Query_by_ClientOrderId_maps_found_and_not_found()
    {
        var order = NewOrder();
        _srv.Given(Request.Create().WithPath("/api/v2/futures/order").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"code":200,"data":{"orderId":"9","status":"FILLED"}}"""));
        var found = await _sp.GetRequiredService<ToobitTradingExchange>().QueryOrderAsync(order.ClientOrderId, default);
        Assert.Equal(QueryStatus.Found, found.Status); Assert.Equal(OrderState.Filled, found.Order!.State);

        _srv.Reset();
        _srv.Given(Request.Create().WithPath("/api/v2/futures/order").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(400).WithBody("""{"code":-2013,"msg":"Order does not exist"}"""));
        Assert.Equal(QueryStatus.NotFound, (await _sp.GetRequiredService<ToobitTradingExchange>().QueryOrderAsync(order.ClientOrderId, default)).Status);
    }

    /// <summary>HttpClient with a 500 ms timeout to simulate the resilience-layer timeout.</summary>
    private sealed class TimeoutFactory(IHttpClientFactory inner) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) { var c = inner.CreateClient(name); c.Timeout = TimeSpan.FromMilliseconds(500); return c; }
    }

    public void Dispose() { _srv.Stop(); _sp.Dispose(); }
}
