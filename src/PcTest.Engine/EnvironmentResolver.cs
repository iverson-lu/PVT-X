using PcTest.Contracts.Models;

namespace PcTest.Engine;

public static class EnvironmentResolver
{
    public static Dictionary<string, string> ResolveForSuite(TestSuiteManifest suite, RunRequest? runRequest)
    {
        var map = GetOsEnvironment();
        ApplyEnv(map, suite.Environment?.Env);
        ApplyEnv(map, runRequest?.EnvironmentOverrides?.Env);
        return map;
    }

    public static Dictionary<string, string> ResolveForPlan(TestPlanManifest plan, TestSuiteManifest suite, RunRequest? runRequest)
    {
        var map = GetOsEnvironment();
        ApplyEnv(map, suite.Environment?.Env);
        ApplyEnv(map, plan.Environment?.Env);
        ApplyEnv(map, runRequest?.EnvironmentOverrides?.Env);
        return map;
    }

    public static Dictionary<string, string> ResolveForPlanOnly(TestPlanManifest plan, RunRequest? runRequest)
    {
        var map = GetOsEnvironment();
        ApplyEnv(map, plan.Environment?.Env);
        ApplyEnv(map, runRequest?.EnvironmentOverrides?.Env);
        return map;
    }

    public static Dictionary<string, string> ResolveForStandalone(RunRequest? runRequest)
    {
        var map = GetOsEnvironment();
        ApplyEnv(map, runRequest?.EnvironmentOverrides?.Env);
        return map;
    }

    private static Dictionary<string, string> GetOsEnvironment()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in Environment.GetEnvironmentVariables().Keys)
        {
            if (key is string name)
            {
                map[name] = Environment.GetEnvironmentVariable(name) ?? string.Empty;
            }
        }

        return map;
    }

    private static void ApplyEnv(Dictionary<string, string> target, Dictionary<string, string>? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (var (key, value) in source)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new EngineException("Environment.Invalid", new { reason = "EmptyKey" });
            }

            target[key] = value;
        }
    }
}
