using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Toobit.Platform.Infrastructure.Toobit;

namespace Toobit.Platform.Infrastructure.Resilience;

/// <summary>
/// Two isolated pipelines (separate HttpClient, circuit breaker and bulkhead each):
///  * toobit-scanner: GET only, so retry with jittered exponential backoff is safe. Fallback = last-known-good snapshot.
///  * toobit-trading: timeout + circuit breaker + bulkhead and NO automatic retry. A POST is never retried blindly;
///    retry of 429 is done by OrderExecutor with the same ClientOrderId, and 5XX/timeouts go to Unknown + reconcile.
/// </summary>
public static class ResilienceRegistration
{
    public static IServiceCollection AddToobitClients(this IServiceCollection services)
    {
        services.AddHttpClient(ToobitScannerExchange.HttpClientName, (sp, c) =>
        {
            c.BaseAddress = new Uri(sp.GetRequiredService<IOptions<ToobitOptions>>().Value.BaseUrl);
            c.Timeout = Timeout.InfiniteTimeSpan;            // the resilience timeout is authoritative
        })
        .AddResilienceHandler("scanner", b =>
        {
            b.AddConcurrencyLimiter(permitLimit: 2, queueLimit: 2);                       // bulkhead: scanner budget
            b.AddTimeout(TimeSpan.FromSeconds(10));
            b.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3, BackoffType = DelayBackoffType.Exponential, UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(500),
            });
            b.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                SamplingDuration = TimeSpan.FromSeconds(30), MinimumThroughput = 5, FailureRatio = 0.5,
                BreakDuration = TimeSpan.FromSeconds(30),
            });
        });

        services.AddHttpClient(ToobitTradingExchange.HttpClientName, (sp, c) =>
        {
            c.BaseAddress = new Uri(sp.GetRequiredService<IOptions<ToobitOptions>>().Value.BaseUrl);
            c.Timeout = Timeout.InfiniteTimeSpan;
        })
        .AddResilienceHandler("trading", b =>
        {
            b.AddConcurrencyLimiter(permitLimit: 8, queueLimit: 16);                      // bulkhead: trading budget
            b.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                SamplingDuration = TimeSpan.FromSeconds(30), MinimumThroughput = 5, FailureRatio = 0.5,
                BreakDuration = TimeSpan.FromSeconds(15),
            });
            b.AddTimeout(TimeSpan.FromSeconds(5));                                        // no retry stage, on purpose
        });
        return services;
    }
}
