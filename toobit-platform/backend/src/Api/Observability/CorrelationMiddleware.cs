using Serilog.Context;

namespace Toobit.Platform.Api.Observability;

/// <summary>X-Correlation-Id in -> logs/traces -> out (and onto calls to the Python service) so one id spans .NET -> Python -> .NET.</summary>
public sealed class CorrelationMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-Id";

    public async Task Invoke(HttpContext ctx)
    {
        var id = ctx.Request.Headers.TryGetValue(Header, out var v) && !string.IsNullOrWhiteSpace(v) && v.ToString().Length <= 64
            ? v.ToString() : Guid.NewGuid().ToString();
        ctx.Items[Header] = id;
        ctx.Response.Headers[Header] = id;
        System.Diagnostics.Activity.Current?.SetTag("correlation_id", id);
        using (LogContext.PushProperty("CorrelationId", id))
            await next(ctx);
    }

    public static string Get(HttpContext ctx) => ctx.Items[Header] as string ?? "";
}
