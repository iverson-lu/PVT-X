using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts.Manifests;

/// <summary>
/// Parameter definition per spec section 6.2.
/// </summary>
public sealed class ParameterDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = ParameterTypes.String;

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("default")]
    public JsonElement? Default { get; set; }

    [JsonPropertyName("min")]
    public double? Min { get; set; }

    [JsonPropertyName("max")]
    public double? Max { get; set; }

    [JsonPropertyName("enumValues")]
    public List<string>? EnumValues { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("uiHint")]
    public string? UiHint { get; set; }

    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }

    [JsonPropertyName("help")]
    public string? Help { get; set; }
}

/// <summary>
/// Test Case manifest per spec section 6.1.
/// </summary>
public sealed class TestCaseManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.5.0";

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("privilege")]
    public Privilege Privilege { get; set; } = Privilege.User;

    [JsonPropertyName("timeoutSec")]
    public int? TimeoutSec { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("parameters")]
    public List<ParameterDefinition>? Parameters { get; set; }

    /// <summary>
    /// Returns the unique identity string (id@version).
    /// </summary>
    [JsonIgnore]
    public string Identity => $"{Id}@{Version}";
}

/// <summary>
/// Test Case node within a Suite per spec section 6.3.
/// </summary>
public sealed class TestCaseNode
{
    [JsonPropertyName("nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("ref")]
    public string Ref { get; set; } = string.Empty;

    [JsonPropertyName("inputs")]
    public Dictionary<string, JsonElement>? Inputs { get; set; }
}

/// <summary>
/// Controls object per spec section 6.5.
/// </summary>
public sealed class SuiteControls
{
    [JsonPropertyName("repeat")]
    public int Repeat { get; set; } = 1;

    [JsonPropertyName("maxParallel")]
    public int MaxParallel { get; set; } = 1;

    [JsonPropertyName("continueOnFailure")]
    public bool ContinueOnFailure { get; set; }

    [JsonPropertyName("retryOnError")]
    public int RetryOnError { get; set; }

    [JsonPropertyName("timeoutPolicy")]
    public string TimeoutPolicy { get; set; } = "AbortOnTimeout";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Environment object per spec section 6.5.
/// </summary>
public sealed class SuiteEnvironment
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }

    [JsonPropertyName("workingDir")]
    public string? WorkingDir { get; set; }

    [JsonPropertyName("runnerHints")]
    public Dictionary<string, JsonElement>? RunnerHints { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Test Suite manifest per spec section 6.3.
/// </summary>
public sealed class TestSuiteManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.5.0";

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("controls")]
    public SuiteControls? Controls { get; set; }

    [JsonPropertyName("environment")]
    public SuiteEnvironment? Environment { get; set; }

    [JsonPropertyName("testCases")]
    public List<TestCaseNode> TestCases { get; set; } = new();

    [JsonIgnore]
    public string Identity => $"{Id}@{Version}";
}

/// <summary>
/// Plan environment object per spec section 6.4.
/// Plan environment is env-only; other keys must fail validation.
/// </summary>
public sealed class PlanEnvironment
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Test Plan manifest per spec section 6.4.
/// </summary>
public sealed class TestPlanManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.5.0";

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("environment")]
    public PlanEnvironment? Environment { get; set; }

    [JsonPropertyName("suites")]
    public List<string> Suites { get; set; } = new();

    [JsonIgnore]
    public string Identity => $"{Id}@{Version}";
}
