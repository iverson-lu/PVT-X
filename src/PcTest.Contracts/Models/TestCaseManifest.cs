using System.Text.Json.Serialization;

namespace PcTest.Contracts.Models;

public sealed class TestCaseManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = string.Empty;

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
    public string? Privilege { get; set; }

    [JsonPropertyName("timeoutSec")]
    public int? TimeoutSec { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("parameters")]
    public List<ParameterDefinition>? Parameters { get; set; }
}
