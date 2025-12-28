using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public sealed class PlanEnvironment
{
    [JsonPropertyName("env")]
    public IReadOnlyDictionary<string, string>? Env { get; init; }
}

public sealed class TestPlanManifest
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

    [JsonPropertyName("environment")]
    public PlanEnvironment? Environment { get; init; }

    [JsonPropertyName("suites")]
    public IReadOnlyList<string> Suites { get; init; } = Array.Empty<string>();
}
