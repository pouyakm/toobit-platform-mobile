namespace Toobit.Platform.Application;

/// <summary>Jittered exponential backoff ("full jitter"). Honors the exchange's reset hint as a floor.</summary>
public static class Backoff
{
    public static TimeSpan Compute(int attempt, TimeSpan baseDelay, TimeSpan max, TimeSpan? retryAfter, Func<double> rng)
    {
        if (attempt < 1) throw new ArgumentOutOfRangeException(nameof(attempt));
        double exp = Math.Min(max.TotalMilliseconds, baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        double jittered = exp * (0.5 + 0.5 * rng());          // 50%..100% of the exponential step
        double floor = retryAfter?.TotalMilliseconds ?? 0;
        return TimeSpan.FromMilliseconds(Math.Max(jittered, floor));
    }
}
