using Toobit.Platform.Application;

namespace Toobit.Platform.Infrastructure.Security;

/// <summary>Dev: environment variables. Prod: swap in a Vault / AWS Secrets Manager implementation of ISecretProvider.</summary>
public sealed class EnvSecretProvider : ISecretProvider
{
    public string GetRequired(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        // The message names the variable but never its value.
        return string.IsNullOrWhiteSpace(v) ? throw new InvalidOperationException($"Required secret '{name}' is not set") : v;
    }
}

/// <summary>Fail-fast startup validation: call once in Program.cs so a missing secret stops the process immediately.</summary>
public static class SecretValidation
{
    public static readonly string[] Required =
    {
        "TOOBIT_API_KEY", "TOOBIT_API_SECRET", "JWT_SIGNING_KEY", "INTERNAL_HMAC_KEY_ID", "INTERNAL_HMAC_KEY",
        "POSTGRES_CONNECTION", "REDIS_CONNECTION", "PII_ENCRYPTION_KEY",
    };

    public static void ValidateOrThrow(ISecretProvider secrets, IEnumerable<string>? names = null)
    {
        var missing = new List<string>();
        foreach (var n in names ?? Required)
            try { secrets.GetRequired(n); } catch (InvalidOperationException) { missing.Add(n); }
        if (missing.Count > 0) throw new InvalidOperationException($"Missing required secrets: {string.Join(", ", missing)}");
    }
}
