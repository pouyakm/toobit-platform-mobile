namespace Toobit.Platform.Infrastructure.Toobit;

/// <summary>Reads X-Api-Limit-Status / X-Api-Limit-Reset-Timestamp. Both are advisory; absence is tolerated.</summary>
public sealed record RateLimitInfo(string? Status, DateTimeOffset? ResetAt)
{
    public static RateLimitInfo From(HttpResponseMessage r)
    {
        string? status = r.Headers.TryGetValues("X-Api-Limit-Status", out var s) ? s.FirstOrDefault() : null;
        DateTimeOffset? reset = null;
        if (r.Headers.TryGetValues("X-Api-Limit-Reset-Timestamp", out var t) && long.TryParse(t.FirstOrDefault(), out var ts))
            reset = ts > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(ts) : DateTimeOffset.FromUnixTimeSeconds(ts);
        return new RateLimitInfo(status, reset);
    }

    public TimeSpan? RetryAfter(DateTimeOffset now) =>
        ResetAt is { } r ? (r > now ? r - now : TimeSpan.Zero) : null;
}

/// <summary>429 vs 418 vs 5XX are different failure classes and must never be conflated.</summary>
public enum HttpFailureClass { None, RateLimited, Banned, ServerError, ClientError }

public static class HttpClassifier
{
    public static HttpFailureClass Classify(int status) => status switch
    {
        >= 200 and < 300 => HttpFailureClass.None,
        429 => HttpFailureClass.RateLimited,
        418 => HttpFailureClass.Banned,
        >= 500 => HttpFailureClass.ServerError,
        _ => HttpFailureClass.ClientError,
    };
}
