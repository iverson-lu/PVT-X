using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts.Models;

public sealed class TestPlanManifest
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

    [JsonPropertyName("environment")]
    public PlanEnvironment? Environment { get; set; }

    [JsonPropertyName("suites")]
    public List<string> Suites { get; set; } = new();
}

public sealed class PlanEnvironment
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
