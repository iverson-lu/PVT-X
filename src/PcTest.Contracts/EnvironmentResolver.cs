namespace PcTest.Contracts;

public static class EnvironmentResolver
{
    public static Dictionary<string, string> ResolveEffectiveEnvironment(
        IReadOnlyDictionary<string, string> osEnvironment,
        IReadOnlyDictionary<string, string>? suiteEnv,
        IReadOnlyDictionary<string, string>? planEnv,
        IReadOnlyDictionary<string, string>? runOverrides)
    {
        Dictionary<string, string> effective = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> pair in osEnvironment)
        {
            effective[pair.Key] = pair.Value;
        }

        if (suiteEnv is not null)
        {
            foreach (KeyValuePair<string, string> pair in suiteEnv)
            {
                effective[pair.Key] = pair.Value;
            }
        }

        if (planEnv is not null)
        {
            foreach (KeyValuePair<string, string> pair in planEnv)
            {
                effective[pair.Key] = pair.Value;
            }
        }

        if (runOverrides is not null)
        {
            foreach (KeyValuePair<string, string> pair in runOverrides)
            {
                effective[pair.Key] = pair.Value;
            }
        }

        return effective;
    }

    public static Dictionary<string, string> SnapshotOsEnvironment()
    {
        Dictionary<string, string> os = new(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            string? key = entry.Key?.ToString();
            string? value = entry.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(key) && value is not null)
            {
                os[key] = value;
            }
        }

        return os;
    }
}
