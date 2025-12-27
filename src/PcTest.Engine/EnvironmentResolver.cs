using PcTest.Contracts;

namespace PcTest.Engine;

public sealed class EnvironmentResolver
{
    public Dictionary<string, string> ResolveForSuite(TestSuiteManifest suite, EnvironmentOverrides? overrides)
    {
        var env = LoadOsEnvironment();
        ApplyEnv(env, suite.Environment?.Env, "Suite.Environment");
        ApplyEnv(env, overrides?.Env, "RunRequest.EnvironmentOverrides");
        return env;
    }

    public Dictionary<string, string> ResolveForPlan(TestPlanManifest plan, TestSuiteManifest suite, EnvironmentOverrides? overrides)
    {
        var env = LoadOsEnvironment();
        ApplyEnv(env, suite.Environment?.Env, "Suite.Environment");
        ApplyEnv(env, plan.Environment?.Env, "Plan.Environment");
        ApplyEnv(env, overrides?.Env, "RunRequest.EnvironmentOverrides");
        return env;
    }

    public Dictionary<string, string> ResolveForPlanRun(TestPlanManifest plan, EnvironmentOverrides? overrides)
    {
        var env = LoadOsEnvironment();
        ApplyEnv(env, plan.Environment?.Env, "Plan.Environment");
        ApplyEnv(env, overrides?.Env, "RunRequest.EnvironmentOverrides");
        return env;
    }

    public Dictionary<string, string> ResolveForStandalone(EnvironmentOverrides? overrides)
    {
        var env = LoadOsEnvironment();
        ApplyEnv(env, overrides?.Env, "RunRequest.EnvironmentOverrides");
        return env;
    }

    private static Dictionary<string, string> LoadOsEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                env[key] = value;
            }
        }

        return env;
    }

    private static void ApplyEnv(Dictionary<string, string> target, Dictionary<string, string>? source, string name)
    {
        if (source == null)
        {
            return;
        }

        foreach (var (key, value) in source)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new EngineException("Environment.InvalidKey", $"{name} contains empty key.", new Dictionary<string, object?>
                {
                    ["source"] = name
                });
            }

            target[key] = value;
        }
    }
}
