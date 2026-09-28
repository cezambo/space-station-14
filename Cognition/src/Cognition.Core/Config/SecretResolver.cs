namespace Cognition.Core.Config;

/// <summary>Reads API keys from environment variables only (RM-05). Error messages never include values.</summary>
public static class SecretResolver
{
    public static string Require(string envVarName, Func<string, string?>? getEnv = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;
        var value = getEnv(envVarName);
        if (string.IsNullOrWhiteSpace(value))
            throw new ConfigException($"environment variable {envVarName} is not set (RM-05)");
        return value.Trim();
    }
}
