using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace Toobit.Platform.Api.Observability;

/// <summary>
/// Defense in depth: masks any log property whose NAME looks sensitive. The primary control is that secrets are
/// never passed to the logger in the first place; this catches accidents (SECURITY.md, "Log redaction").
/// </summary>
public sealed partial class SecretMaskingEnricher : ILogEventEnricher
{
    [GeneratedRegex("api[-_]?key|secret|signature|authorization|password|token|bearer|hmac", RegexOptions.IgnoreCase)]
    private static partial Regex Sensitive();

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory pf)
    {
        foreach (var name in logEvent.Properties.Keys.Where(k => Sensitive().IsMatch(k)).ToList())
            logEvent.AddOrUpdateProperty(pf.CreateProperty(name, "***"));
    }
}
