using System.Collections;

namespace PcTest.Engine;

public static class EnvironmentResolver
{
    public static Dictionary<string, string> BuildEffectiveEnvironment(
        Dictionary<string, string>? suiteEnv,
        Dictionary<string, string>? planEnv,
        Dictionary<string, string>? runOverrideEnv)
    {
        Dictionary<string, string> env = new(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            string? key = entry.Key?.ToString();
            string? value = entry.Value?.ToString();
            if (string.IsNullOrEmpty(key) || value is null)
            {
                continue;
            }

            env[key] = value;
        }

        Apply(env, suiteEnv);
        Apply(env, planEnv);
        Apply(env, runOverrideEnv);
        return env;
    }

    private static void Apply(Dictionary<string, string> env, Dictionary<string, string>? overrides)
    {
        if (overrides is null)
        {
            return;
        }

        foreach (KeyValuePair<string, string> pair in overrides)
        {
            env[pair.Key] = pair.Value;
        }
    }
}
