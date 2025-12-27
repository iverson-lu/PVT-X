using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public sealed record TestCaseManifest
{
    [JsonPropertyName("schemaVersion")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("category")]
    public required string Category { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("privilege")]
    public string? Privilege { get; init; }

    [JsonPropertyName("timeoutSec")]
    public int? TimeoutSec { get; init; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    [JsonPropertyName("parameters")]
    public ParameterDefinition[]? Parameters { get; init; }
}

public sealed record TestCaseNode
{
    [JsonPropertyName("nodeId")]
    public required string NodeId { get; init; }

    [JsonPropertyName("ref")]
    public required string Ref { get; init; }

    [JsonPropertyName("inputs")]
    public Dictionary<string, object?>? Inputs { get; init; }
}

public sealed record SuiteEnvironment
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; init; }

    [JsonPropertyName("workingDir")]
    public string? WorkingDir { get; init; }

    [JsonPropertyName("runnerHints")]
    public Dictionary<string, object?>? RunnerHints { get; init; }
}

public sealed record TestSuiteManifest
{
    [JsonPropertyName("schemaVersion")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    [JsonPropertyName("controls")]
    public Dictionary<string, object?>? Controls { get; init; }

    [JsonPropertyName("environment")]
    public SuiteEnvironment? Environment { get; init; }

    [JsonPropertyName("testCases")]
    public required TestCaseNode[] TestCases { get; init; }
}

public sealed record PlanEnvironment
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; init; }
}

public sealed record TestPlanManifest
{
    [JsonPropertyName("schemaVersion")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    [JsonPropertyName("environment")]
    public PlanEnvironment? Environment { get; init; }

    [JsonPropertyName("suites")]
    public required string[] Suites { get; init; }
}
