using System.Collections;
using PcTest.Contracts;

namespace PcTest.Engine;

public static class EnvironmentResolver
{
    public static Dictionary<string, string> ResolveForPlan(Dictionary<string, string>? planEnv, Dictionary<string, string>? suiteEnv, Dictionary<string, string>? overrides)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            env[(string)entry.Key] = entry.Value?.ToString() ?? string.Empty;
        }
        Merge(env, suiteEnv);
        Merge(env, planEnv);
        Merge(env, overrides);
        return env;
    }

    public static Dictionary<string, string> ResolveForSuite(Dictionary<string, string>? suiteEnv, Dictionary<string, string>? overrides)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            env[(string)entry.Key] = entry.Value?.ToString() ?? string.Empty;
        }
        Merge(env, suiteEnv);
        Merge(env, overrides);
        return env;
    }

    public static Dictionary<string, string> ResolveForTestCase(Dictionary<string, string>? overrides)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            env[(string)entry.Key] = entry.Value?.ToString() ?? string.Empty;
        }
        Merge(env, overrides);
        return env;
    }

    private static void Merge(Dictionary<string, string> env, Dictionary<string, string>? overrides)
    {
        if (overrides is null)
        {
            return;
        }
        foreach (var kvp in overrides)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key))
            {
                throw new InvalidOperationException("Environment key must be non-empty.");
            }
            env[kvp.Key] = kvp.Value;
        }
    }
}
