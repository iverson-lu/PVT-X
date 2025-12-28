using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts.Models;

public sealed class TestSuiteManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = string.Empty;

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
    public JsonElement? Controls { get; set; }

    [JsonPropertyName("environment")]
    public SuiteEnvironment? Environment { get; set; }

    [JsonPropertyName("testCases")]
    public List<TestCaseNode> TestCases { get; set; } = new();
}

public sealed class SuiteEnvironment
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }

    [JsonPropertyName("workingDir")]
    public string? WorkingDir { get; set; }

    [JsonPropertyName("runnerHints")]
    public JsonElement? RunnerHints { get; set; }
}

public sealed class TestCaseNode
{
    [JsonPropertyName("nodeId")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("ref")]
    public string Ref { get; set; } = string.Empty;

    [JsonPropertyName("inputs")]
    public Dictionary<string, JsonElement>? Inputs { get; set; }
}
