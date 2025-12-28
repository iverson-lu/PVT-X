using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public sealed record TestCaseManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "";

    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("category")]
    public string Category { get; init; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("privilege")]
    public string? Privilege { get; init; }

    [JsonPropertyName("timeoutSec")]
    public int? TimeoutSec { get; init; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    [JsonPropertyName("parameters")]
    public ParameterDefinition[]? Parameters { get; init; }
}

public sealed record ParameterDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("type")]
    public string Type { get; init; } = "string";

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

public sealed record SuiteManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "";

    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    [JsonPropertyName("controls")]
    public JsonElement? Controls { get; init; }

    [JsonPropertyName("environment")]
    public SuiteEnvironment? Environment { get; init; }

    [JsonPropertyName("testCases")]
    public SuiteTestCaseNode[] TestCases { get; init; } = Array.Empty<SuiteTestCaseNode>();
}

public sealed record SuiteEnvironment
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; init; }

    [JsonPropertyName("workingDir")]
    public string? WorkingDir { get; init; }
}

public sealed record SuiteTestCaseNode
{
    [JsonPropertyName("nodeId")]
    public string NodeId { get; init; } = "";

    [JsonPropertyName("ref")]
    public string Ref { get; init; } = "";

    [JsonPropertyName("inputs")]
    public Dictionary<string, JsonElement>? Inputs { get; init; }
}

public sealed record PlanManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "";

    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    [JsonPropertyName("environment")]
    public PlanEnvironment? Environment { get; init; }

    [JsonPropertyName("suites")]
    public PlanSuiteRef[] Suites { get; init; } = Array.Empty<PlanSuiteRef>();
}

public sealed record PlanEnvironment
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; init; }
}

public sealed record PlanSuiteRef
{
    [JsonPropertyName("suite")]
    public string Suite { get; init; } = "";
}
