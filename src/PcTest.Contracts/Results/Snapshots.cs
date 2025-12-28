using System.Text.Json;
using System.Text.Json.Serialization;
using PcTest.Contracts.Manifests;

namespace PcTest.Contracts.Results;

/// <summary>
/// Manifest snapshot for Case Run Folder per spec section 12.2.
/// </summary>
public sealed class CaseManifestSnapshot
{
    [JsonPropertyName("sourceManifest")]
    public TestCaseManifest SourceManifest { get; set; } = new();

    [JsonPropertyName("resolvedRef")]
    public string? ResolvedRef { get; set; }

    [JsonPropertyName("resolvedIdentity")]
    public IdentityInfo ResolvedIdentity { get; set; } = new();

    [JsonPropertyName("effectiveEnvironment")]
    public Dictionary<string, string> EffectiveEnvironment { get; set; } = new();

    [JsonPropertyName("effectiveInputs")]
    public Dictionary<string, object?> EffectiveInputs { get; set; } = new();

    [JsonPropertyName("inputTemplates")]
    public Dictionary<string, JsonElement>? InputTemplates { get; set; }

    [JsonPropertyName("resolvedAt")]
    public string? ResolvedAt { get; set; }

    [JsonPropertyName("engineVersion")]
    public string? EngineVersion { get; set; }
}

/// <summary>
/// Identity info for snapshots.
/// </summary>
public sealed class IdentityInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

/// <summary>
/// Environment snapshot (env.json) per spec section 12.4.
/// </summary>
public sealed class EnvironmentSnapshot
{
    [JsonPropertyName("osVersion")]
    public string OsVersion { get; set; } = string.Empty;

    [JsonPropertyName("runnerVersion")]
    public string RunnerVersion { get; set; } = string.Empty;

    [JsonPropertyName("pwshVersion")]
    public string PwshVersion { get; set; } = string.Empty;

    [JsonPropertyName("isElevated")]
    public bool IsElevated { get; set; }
}

/// <summary>
/// Suite/Plan group manifest snapshot.
/// </summary>
public sealed class GroupManifestSnapshot
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.5.0";

    [JsonPropertyName("runType")]
    public RunType RunType { get; set; }

    [JsonPropertyName("suiteId")]
    public string? SuiteId { get; set; }

    [JsonPropertyName("suiteVersion")]
    public string? SuiteVersion { get; set; }

    [JsonPropertyName("planId")]
    public string? PlanId { get; set; }

    [JsonPropertyName("planVersion")]
    public string? PlanVersion { get; set; }

    [JsonPropertyName("originalManifest")]
    public JsonElement? OriginalManifest { get; set; }

    [JsonPropertyName("resolvedRefs")]
    public Dictionary<string, string>? ResolvedRefs { get; set; }

    [JsonPropertyName("resolvedAt")]
    public string? ResolvedAt { get; set; }

    [JsonPropertyName("engineVersion")]
    public string? EngineVersion { get; set; }
}

/// <summary>
/// Event entry for events.jsonl per spec section 12.6.
/// </summary>
public sealed class EventEntry
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public string Level { get; set; } = "info";

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("data")]
    public Dictionary<string, object?>? Data { get; set; }
}
