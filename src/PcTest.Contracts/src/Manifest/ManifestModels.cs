using System.Text.Json.Serialization;

namespace PcTest.Contracts.Manifest;

/// <summary>
/// Represents the manifest that describes a single PowerShell test.
/// </summary>
public class TestManifest
{
    /// <summary>
    /// Schema version for the manifest payload.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    /// <summary>
    /// Unique identifier for the test.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human readable test name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Category used for grouping tests.
    /// </summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Optional description explaining what the test validates.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Semantic version of the test.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Required privilege level needed to run the test.
    /// </summary>
    [JsonPropertyName("privilege")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PrivilegePolicy Privilege { get; set; } = PrivilegePolicy.User;

    /// <summary>
    /// Optional timeout in seconds for the test run.
    /// </summary>
    [JsonPropertyName("timeoutSec")]
    public int? TimeoutSec { get; set; }

    /// <summary>
    /// Optional classification tags for filtering tests.
    /// </summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string>? Tags { get; set; }

    /// <summary>
    /// Optional collection of expected parameters accepted by the test script.
    /// </summary>
    [JsonPropertyName("parameters")]
    public IReadOnlyList<ParameterDefinition>? Parameters { get; set; }
}

/// <summary>
/// Declares the privilege requirements for running a test.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PrivilegePolicy
{
    /// <summary>
    /// Standard user privileges are sufficient.
    /// </summary>
    User,
    /// <summary>
    /// Elevated rights are preferred but not required.
    /// </summary>
    AdminPreferred,
    /// <summary>
    /// Elevated rights are required.
    /// </summary>
    AdminRequired
}

/// <summary>
/// Defines the characteristics and constraints of a test parameter.
/// </summary>
public class ParameterDefinition
{
    /// <summary>
    /// Name of the parameter as expected by the script.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Declared type of the parameter (string, int, bool, enum, etc.).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the parameter must be provided.
    /// </summary>
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    /// <summary>
    /// Optional default value used when no input is provided.
    /// </summary>
    [JsonPropertyName("default")]
    public object? Default { get; set; }

    /// <summary>
    /// Minimum numeric value allowed for numeric parameters.
    /// </summary>
    [JsonPropertyName("min")]
    public double? Min { get; set; }

    /// <summary>
    /// Maximum numeric value allowed for numeric parameters.
    /// </summary>
    [JsonPropertyName("max")]
    public double? Max { get; set; }

    /// <summary>
    /// Allowed values when the parameter type is enum.
    /// </summary>
    [JsonPropertyName("enumValues")]
    public IReadOnlyList<string>? EnumValues { get; set; }

    /// <summary>
    /// Optional unit label for numeric values.
    /// </summary>
    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    /// <summary>
    /// Optional UI hint for rendering the parameter.
    /// </summary>
    [JsonPropertyName("uiHint")]
    public string? UiHint { get; set; }

    /// <summary>
    /// Optional validation pattern for string inputs.
    /// </summary>
    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }

    /// <summary>
    /// Optional help text describing the parameter.
    /// </summary>
    [JsonPropertyName("help")]
    public string? Help { get; set; }
}

/// <summary>
/// Represents a discovered test manifest and its metadata.
/// </summary>
/// <param name="Id">Unique identifier for the test.</param>
/// <param name="Name">Human readable test name.</param>
/// <param name="Version">Version of the test.</param>
/// <param name="Category">Category used to group the test.</param>
/// <param name="ManifestPath">File path to the manifest that produced this entry.</param>
/// <param name="Privilege">Privilege requirement for running the test.</param>
/// <param name="TimeoutSec">Optional timeout in seconds.</param>
/// <param name="Tags">Optional tags associated with the test.</param>
public record DiscoveredTest(
    string Id,
    string Name,
    string Version,
    string Category,
    string ManifestPath,
    PrivilegePolicy Privilege,
    int? TimeoutSec,
    IReadOnlyList<string>? Tags);
