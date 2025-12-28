using System.Collections;
using PcTest.Contracts;

namespace PcTest.Engine;

public static class EnvironmentResolver
{
    public static IReadOnlyDictionary<string, string> BuildEffectiveEnvironment(
        IReadOnlyDictionary<string, string>? planEnv,
        IReadOnlyDictionary<string, string>? suiteEnv,
        IReadOnlyDictionary<string, string>? overrides)
    {
        var result = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                result[key] = value;
            }
        }

        ApplyEnv(result, suiteEnv);
        ApplyEnv(result, planEnv);
        ApplyEnv(result, overrides);
        return result;
    }

    private static void ApplyEnv(SortedDictionary<string, string> result, IReadOnlyDictionary<string, string>? env)
    {
        if (env is null)
        {
            return;
        }

        foreach (var kvp in env)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key))
            {
                throw new PcTestException("Environment.Invalid", "Environment variable key cannot be empty.");
            }

            result[kvp.Key] = kvp.Value;
        }
    }
}
