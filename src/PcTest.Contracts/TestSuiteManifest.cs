using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public sealed class SuiteEnvironment
{
    [JsonPropertyName("env")]
    public IReadOnlyDictionary<string, string>? Env { get; init; }

    [JsonPropertyName("workingDir")]
    public string? WorkingDir { get; init; }

    [JsonPropertyName("runnerHints")]
    public JsonElement? RunnerHints { get; init; }
}

public sealed class TestCaseNode
{
    [JsonPropertyName("nodeId")]
    public string NodeId { get; init; } = string.Empty;

    [JsonPropertyName("ref")]
    public string Ref { get; init; } = string.Empty;

    [JsonPropertyName("inputs")]
    public IReadOnlyDictionary<string, JsonElement>? Inputs { get; init; }
}

public sealed class TestSuiteManifest
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
    public IReadOnlyList<string>? Tags { get; init; }

    [JsonPropertyName("controls")]
    public JsonElement? Controls { get; init; }

    [JsonPropertyName("environment")]
    public SuiteEnvironment? Environment { get; init; }

    [JsonPropertyName("testCases")]
    public IReadOnlyList<TestCaseNode> TestCases { get; init; } = Array.Empty<TestCaseNode>();
}
