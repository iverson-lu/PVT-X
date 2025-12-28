using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public sealed class TestCaseManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("privilege")]
    public string? Privilege { get; init; }

    [JsonPropertyName("timeoutSec")]
    public int? TimeoutSec { get; init; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    [JsonPropertyName("parameters")]
    public ParameterDefinition[]? Parameters { get; init; }
}

public sealed class ParameterDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("required")]
    public bool Required { get; init; }

    [JsonPropertyName("default")]
    public JsonElement? Default { get; init; }

    [JsonPropertyName("min")]
    public double? Min { get; init; }

    [JsonPropertyName("max")]
    public double? Max { get; init; }

    [JsonPropertyName("enumValues")]
    public string[]? EnumValues { get; init; }

    [JsonPropertyName("unit")]
    public string? Unit { get; init; }

    [JsonPropertyName("uiHint")]
    public string? UiHint { get; init; }

    [JsonPropertyName("pattern")]
    public string? Pattern { get; init; }

    [JsonPropertyName("help")]
    public string? Help { get; init; }
}

public sealed class SuiteManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    [JsonPropertyName("controls")]
    public JsonElement? Controls { get; init; }

    [JsonPropertyName("environment")]
    public SuiteEnvironment? Environment { get; init; }

    [JsonPropertyName("testCases")]
    public SuiteTestCaseNode[] TestCases { get; init; } = Array.Empty<SuiteTestCaseNode>();
}

public sealed class SuiteTestCaseNode
{
    [JsonPropertyName("nodeId")]
    public string NodeId { get; init; } = string.Empty;

    [JsonPropertyName("ref")]
    public string Ref { get; init; } = string.Empty;

    [JsonPropertyName("inputs")]
    public Dictionary<string, JsonElement>? Inputs { get; init; }
}

public sealed class SuiteEnvironment
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; init; }

    [JsonPropertyName("workingDir")]
    public string? WorkingDir { get; init; }

    [JsonPropertyName("runnerHints")]
    public JsonElement? RunnerHints { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

public sealed class PlanManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    [JsonPropertyName("environment")]
    public PlanEnvironment? Environment { get; init; }

    [JsonPropertyName("suites")]
    public string[] Suites { get; init; } = Array.Empty<string>();
}

public sealed class PlanEnvironment
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
