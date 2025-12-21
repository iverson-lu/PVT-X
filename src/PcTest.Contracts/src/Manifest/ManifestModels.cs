using System.Text.Json.Serialization;

namespace PcTest.Contracts.Manifest;

public class TestManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("privilege")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PrivilegePolicy Privilege { get; set; } = PrivilegePolicy.User;

    [JsonPropertyName("timeoutSec")]
    public int? TimeoutSec { get; set; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<string>? Tags { get; set; }

    [JsonPropertyName("parameters")]
    public IReadOnlyList<ParameterDefinition>? Parameters { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PrivilegePolicy
{
    User,
    AdminPreferred,
    AdminRequired
}

public class ParameterDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("default")]
    public object? Default { get; set; }

    [JsonPropertyName("min")]
    public double? Min { get; set; }

    [JsonPropertyName("max")]
    public double? Max { get; set; }

    [JsonPropertyName("enumValues")]
    public IReadOnlyList<string>? EnumValues { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("uiHint")]
    public string? UiHint { get; set; }

    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }

    [JsonPropertyName("help")]
    public string? Help { get; set; }
}

public record DiscoveredTest(
    string Id,
    string Name,
    string Version,
    string Category,
    string ManifestPath,
    PrivilegePolicy Privilege,
    int? TimeoutSec,
    IReadOnlyList<string>? Tags);
