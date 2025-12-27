using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public enum RunStatus
{
    Passed,
    Failed,
    Error,
    Timeout,
    Aborted
}

public enum RunType
{
    TestCase,
    TestSuite,
    TestPlan
}

public sealed record ErrorInfo
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("stack")]
    public string? Stack { get; init; }
}

public sealed record TestCaseResult
{
    [JsonPropertyName("schemaVersion")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("runType")]
    public RunType RunType { get; init; } = RunType.TestCase;

    [JsonPropertyName("nodeId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NodeId { get; init; }

    [JsonPropertyName("testId")]
    public required string TestId { get; init; }

    [JsonPropertyName("testVersion")]
    public required string TestVersion { get; init; }

    [JsonPropertyName("suiteId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SuiteId { get; init; }

    [JsonPropertyName("suiteVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SuiteVersion { get; init; }

    [JsonPropertyName("planId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlanId { get; init; }

    [JsonPropertyName("planVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlanVersion { get; init; }

    [JsonPropertyName("status")]
    public required RunStatus Status { get; init; }

    [JsonPropertyName("startTime")]
    public required string StartTime { get; init; }

    [JsonPropertyName("endTime")]
    public required string EndTime { get; init; }

    [JsonPropertyName("metrics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object?>? Metrics { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }

    [JsonPropertyName("exitCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExitCode { get; init; }

    [JsonPropertyName("effectiveInputs")]
    public required Dictionary<string, object?> EffectiveInputs { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ErrorInfo? Error { get; init; }

    [JsonPropertyName("runner")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object?>? Runner { get; init; }
}

public sealed record GroupRunResult
{
    [JsonPropertyName("schemaVersion")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("runType")]
    public required RunType RunType { get; init; }

    [JsonPropertyName("suiteId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SuiteId { get; init; }

    [JsonPropertyName("suiteVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SuiteVersion { get; init; }

    [JsonPropertyName("planId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlanId { get; init; }

    [JsonPropertyName("planVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlanVersion { get; init; }

    [JsonPropertyName("status")]
    public required RunStatus Status { get; init; }

    [JsonPropertyName("startTime")]
    public required string StartTime { get; init; }

    [JsonPropertyName("endTime")]
    public required string EndTime { get; init; }

    [JsonPropertyName("counts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, int>? Counts { get; init; }

    [JsonPropertyName("childRunIds")]
    public required string[] ChildRunIds { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }
}
