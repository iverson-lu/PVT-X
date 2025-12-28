using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public sealed class RunError
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("stack")]
    public string? Stack { get; init; }
}

public sealed class TestCaseResult
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonPropertyName("runType")]
    public string? RunType { get; init; }

    [JsonPropertyName("nodeId")]
    public string? NodeId { get; init; }

    [JsonPropertyName("testId")]
    public string TestId { get; init; } = string.Empty;

    [JsonPropertyName("testVersion")]
    public string TestVersion { get; init; } = string.Empty;

    [JsonPropertyName("suiteId")]
    public string? SuiteId { get; init; }

    [JsonPropertyName("suiteVersion")]
    public string? SuiteVersion { get; init; }

    [JsonPropertyName("planId")]
    public string? PlanId { get; init; }

    [JsonPropertyName("planVersion")]
    public string? PlanVersion { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("startTime")]
    public string StartTime { get; init; } = string.Empty;

    [JsonPropertyName("endTime")]
    public string EndTime { get; init; } = string.Empty;

    [JsonPropertyName("metrics")]
    public object? Metrics { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; init; }

    [JsonPropertyName("effectiveInputs")]
    public IReadOnlyDictionary<string, object> EffectiveInputs { get; init; } = new Dictionary<string, object>();

    [JsonPropertyName("error")]
    public RunError? Error { get; init; }

    [JsonPropertyName("runner")]
    public object? Runner { get; init; }
}

public sealed class SummaryResult
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonPropertyName("runType")]
    public string RunType { get; init; } = string.Empty;

    [JsonPropertyName("suiteId")]
    public string? SuiteId { get; init; }

    [JsonPropertyName("suiteVersion")]
    public string? SuiteVersion { get; init; }

    [JsonPropertyName("planId")]
    public string? PlanId { get; init; }

    [JsonPropertyName("planVersion")]
    public string? PlanVersion { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("startTime")]
    public string StartTime { get; init; } = string.Empty;

    [JsonPropertyName("endTime")]
    public string EndTime { get; init; } = string.Empty;

    [JsonPropertyName("counts")]
    public IReadOnlyDictionary<string, int>? Counts { get; init; }

    [JsonPropertyName("childRunIds")]
    public IReadOnlyList<string> ChildRunIds { get; init; } = Array.Empty<string>();

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
