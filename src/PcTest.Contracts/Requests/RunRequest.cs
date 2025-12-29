using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts.Requests;

/// <summary>
/// Environment variable reference per spec section 7.4.
/// </summary>
public sealed class EnvRef
{
    [JsonPropertyName("$env")]
    public string EnvVarName { get; set; } = string.Empty;

    [JsonPropertyName("default")]
    public JsonElement? Default { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("secret")]
    public bool Secret { get; set; }

    /// <summary>
    /// Determines if a JsonElement represents an EnvRef.
    /// </summary>
    public static bool IsEnvRef(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        return element.TryGetProperty("$env", out _);
    }

    /// <summary>
    /// Parses an EnvRef from a JsonElement.
    /// </summary>
    public static EnvRef? FromJsonElement(JsonElement element)
    {
        if (!IsEnvRef(element))
            return null;

        var envRef = new EnvRef();

        if (element.TryGetProperty("$env", out var envProp) && envProp.ValueKind == JsonValueKind.String)
        {
            envRef.EnvVarName = envProp.GetString() ?? string.Empty;
        }

        if (element.TryGetProperty("default", out var defaultProp))
        {
            envRef.Default = defaultProp;
        }

        if (element.TryGetProperty("required", out var requiredProp) && requiredProp.ValueKind == JsonValueKind.True)
        {
            envRef.Required = true;
        }

        if (element.TryGetProperty("secret", out var secretProp) && secretProp.ValueKind == JsonValueKind.True)
        {
            envRef.Secret = true;
        }

        return envRef;
    }
}

/// <summary>
/// Node override for Suite RunRequest per spec section 8.2.
/// </summary>
public sealed class NodeOverride
{
    [JsonPropertyName("inputs")]
    public Dictionary<string, JsonElement>? Inputs { get; set; }
}

/// <summary>
/// Environment overrides per spec section 8.
/// </summary>
public sealed class EnvironmentOverrides
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }
}

/// <summary>
/// RunRequest per spec section 8.
/// Exactly one of Suite, TestCase, or Plan must be specified.
/// </summary>
public sealed class RunRequest
{
    [JsonPropertyName("suite")]
    public string? Suite { get; set; }

    [JsonPropertyName("testCase")]
    public string? TestCase { get; set; }

    [JsonPropertyName("plan")]
    public string? Plan { get; set; }

    [JsonPropertyName("nodeOverrides")]
    public Dictionary<string, NodeOverride>? NodeOverrides { get; set; }

    [JsonPropertyName("caseInputs")]
    public Dictionary<string, JsonElement>? CaseInputs { get; set; }

    [JsonPropertyName("environmentOverrides")]
    public EnvironmentOverrides? EnvironmentOverrides { get; set; }

    /// <summary>
    /// Returns the target type based on which property is set.
    /// </summary>
    [JsonIgnore]
    public RunType? TargetType
    {
        get
        {
            if (!string.IsNullOrEmpty(Suite))
                return RunType.TestSuite;
            if (!string.IsNullOrEmpty(TestCase))
                return RunType.TestCase;
            if (!string.IsNullOrEmpty(Plan))
                return RunType.TestPlan;
            return null;
        }
    }

    /// <summary>
    /// Returns the target identity string.
    /// </summary>
    [JsonIgnore]
    public string? TargetIdentity => Suite ?? TestCase ?? Plan;
}
