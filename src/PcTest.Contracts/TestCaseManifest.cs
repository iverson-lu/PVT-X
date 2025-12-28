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
    public IReadOnlyList<string>? Tags { get; init; }

    [JsonPropertyName("parameters")]
    public IReadOnlyList<ParameterDefinition>? Parameters { get; init; }
}
