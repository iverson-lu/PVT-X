using System.Collections;
using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine;

public static class EnvironmentResolver
{
    public static IReadOnlyDictionary<string, string> Resolve(
        JsonElement? planEnv,
        JsonElement? suiteEnv,
        EnvironmentOverrides? overrides)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                env[key] = value;
            }
        }

        ApplyEnvLayer(env, suiteEnv);
        ApplyEnvLayer(env, planEnv);
        ApplyOverrides(env, overrides);

        return env;
    }

    public static IReadOnlyDictionary<string, string> ResolveStandalone(EnvironmentOverrides? overrides)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                env[key] = value;
            }
        }

        ApplyOverrides(env, overrides);
        return env;
    }

    private static void ApplyOverrides(IDictionary<string, string> env, EnvironmentOverrides? overrides)
    {
        if (overrides?.Env == null)
        {
            return;
        }

        foreach (var (key, value) in overrides.Env)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new PcTestException(new[]
                {
                    new PcTestError("Environment.Override.Invalid", "Environment override key must be non-empty.")
                });
            }

            env[key] = value;
        }
    }

    private static void ApplyEnvLayer(IDictionary<string, string> env, JsonElement? environment)
    {
        if (environment == null || environment.Value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (environment.Value.ValueKind != JsonValueKind.Object)
        {
            throw new PcTestException(new[]
            {
                new PcTestError("Environment.Invalid", "Environment must be an object.")
            });
        }

        if (!environment.Value.TryGetProperty("env", out var envElement))
        {
            return;
        }

        if (envElement.ValueKind != JsonValueKind.Object)
        {
            throw new PcTestException(new[]
            {
                new PcTestError("Environment.Invalid", "Environment.env must be an object.")
            });
        }

        foreach (var property in envElement.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name))
            {
                throw new PcTestException(new[]
                {
                    new PcTestError("Environment.Invalid", "Environment.env keys must be non-empty.")
                });
            }

            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new PcTestException(new[]
                {
                    new PcTestError("Environment.Invalid", "Environment.env values must be strings.")
                });
            }

            env[property.Name] = property.Value.GetString() ?? string.Empty;
        }
    }
}
