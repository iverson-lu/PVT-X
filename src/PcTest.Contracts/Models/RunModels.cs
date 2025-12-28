using System.Text.Json.Serialization;

namespace PcTest.Contracts.Models;

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

public sealed class TestCaseResult
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.5.0";

    [JsonPropertyName("runType")]
    public RunType RunType { get; set; } = RunType.TestCase;

    [JsonPropertyName("nodeId")]
    public string? NodeId { get; set; }

    [JsonPropertyName("testId")]
    public string TestId { get; set; } = string.Empty;

    [JsonPropertyName("testVersion")]
    public string TestVersion { get; set; } = string.Empty;

    [JsonPropertyName("suiteId")]
    public string? SuiteId { get; set; }

    [JsonPropertyName("suiteVersion")]
    public string? SuiteVersion { get; set; }

    [JsonPropertyName("planId")]
    public string? PlanId { get; set; }

    [JsonPropertyName("planVersion")]
    public string? PlanVersion { get; set; }

    [JsonPropertyName("status")]
    public RunStatus Status { get; set; }

    [JsonPropertyName("startTime")]
    public string StartTime { get; set; } = string.Empty;

    [JsonPropertyName("endTime")]
    public string EndTime { get; set; } = string.Empty;

    [JsonPropertyName("metrics")]
    public object? Metrics { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; set; }

    [JsonPropertyName("effectiveInputs")]
    public Dictionary<string, object?> EffectiveInputs { get; set; } = new();

    [JsonPropertyName("error")]
    public RunError? Error { get; set; }

    [JsonPropertyName("runner")]
    public object? Runner { get; set; }
}

public sealed class RunError
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("stack")]
    public string? Stack { get; set; }
}

public sealed class GroupRunResult
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

    [JsonPropertyName("status")]
    public RunStatus Status { get; set; }

    [JsonPropertyName("startTime")]
    public string StartTime { get; set; } = string.Empty;

    [JsonPropertyName("endTime")]
    public string EndTime { get; set; } = string.Empty;

    [JsonPropertyName("counts")]
    public Dictionary<string, int>? Counts { get; set; }

    [JsonPropertyName("childRunIds")]
    public List<string> ChildRunIds { get; set; } = new();

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
