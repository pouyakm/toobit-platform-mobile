using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Toobit.Platform.Application;
using Toobit.Platform.Infrastructure.Security;

namespace Toobit.Platform.Api.Security;

/// <summary>Guards /internal/* routes with HMAC + per-route scope. Failures are generic (never reveal which check failed).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class InternalHmacAttribute(string scope) : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext ctx)
    {
        var http = ctx.HttpContext;
        var verifier = http.RequestServices.GetRequiredService<InternalHmacVerifier>();
        var clock = http.RequestServices.GetRequiredService<IClock>();
        var log = http.RequestServices.GetRequiredService<ILogger<InternalHmacAttribute>>();

        http.Request.EnableBuffering();
        using var ms = new MemoryStream();
        await http.Request.Body.CopyToAsync(ms);
        http.Request.Body.Position = 0;

        var h = http.Request.Headers;
        var result = verifier.Verify(h["X-Key-Id"].ToString(), h["X-Timestamp"].ToString(), h["X-Signature"].ToString(),
            http.Request.Method, http.Request.Path.Value ?? "", ms.ToArray(), scope, clock.UtcNow);
        if (result != HmacResult.Ok)
        {
            log.LogWarning("Internal auth rejected: {Reason}", result);     // reason in logs only, not in the response
            ctx.Result = new StatusCodeResult(result == HmacResult.ScopeDenied ? 403 : 401);
        }
    }
}
