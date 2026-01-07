using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts.Results;

/// <summary>
/// Error details per spec section 13.3.
/// </summary>
public sealed class ErrorInfo
{
    [JsonPropertyName("type")]
    public ErrorType Type { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("stack")]
    public string? Stack { get; set; }
}

/// <summary>
/// Runner metadata in result.
/// </summary>
public sealed class RunnerInfo
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("pwshVersion")]
    public string? PwshVersion { get; set; }
}

/// <summary>
/// Reboot metadata for runs that request resume.
/// </summary>
public sealed class RebootInfo
{
    [JsonPropertyName("nextPhase")]
    public int NextPhase { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("delaySec")]
    public int? DelaySec { get; set; }

    [JsonPropertyName("originTestId")]
    public string? OriginTestId { get; set; }
}

/// <summary>
/// Test Case result per spec section 13.2.
/// </summary>
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
    public Dictionary<string, JsonElement>? Metrics { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("reboot")]
    public RebootInfo? Reboot { get; set; }

    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; set; }

    [JsonPropertyName("effectiveInputs")]
    public Dictionary<string, object?> EffectiveInputs { get; set; } = new();

    [JsonPropertyName("error")]
    public ErrorInfo? Error { get; set; }

    [JsonPropertyName("runner")]
    public RunnerInfo? Runner { get; set; }
}

/// <summary>
/// Status counts for summary results.
/// </summary>
public sealed class StatusCounts
{
    [JsonPropertyName("passed")]
    public int Passed { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("error")]
    public int Error { get; set; }

    [JsonPropertyName("timeout")]
    public int Timeout { get; set; }

    [JsonPropertyName("aborted")]
    public int Aborted { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

/// <summary>
/// Test Suite / Test Plan summary result per spec section 13.4.
/// </summary>
public sealed class GroupResult
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.5.0";

    [JsonPropertyName("runType")]
    public RunType RunType { get; set; }

    [JsonPropertyName("runId")]
    public string? RunId { get; set; }

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
    public StatusCounts? Counts { get; set; }

    [JsonPropertyName("childRunIds")]
    public List<string> ChildRunIds { get; set; } = new();

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("reboot")]
    public RebootInfo? Reboot { get; set; }
}

/// <summary>
/// Index entry per spec section 12.3.
/// </summary>
public sealed class IndexEntry
{
    [JsonPropertyName("runId")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("runType")]
    public RunType RunType { get; set; }

    [JsonPropertyName("nodeId")]
    public string? NodeId { get; set; }

    [JsonPropertyName("testId")]
    public string? TestId { get; set; }

    [JsonPropertyName("testVersion")]
    public string? TestVersion { get; set; }

    [JsonPropertyName("suiteId")]
    public string? SuiteId { get; set; }

    [JsonPropertyName("suiteVersion")]
    public string? SuiteVersion { get; set; }

    [JsonPropertyName("planId")]
    public string? PlanId { get; set; }

    [JsonPropertyName("planVersion")]
    public string? PlanVersion { get; set; }

    [JsonPropertyName("parentRunId")]
    public string? ParentRunId { get; set; }

    [JsonPropertyName("startTime")]
    public string StartTime { get; set; } = string.Empty;

    [JsonPropertyName("endTime")]
    public string EndTime { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public RunStatus Status { get; set; }
}

/// <summary>
/// Child entry for children.jsonl per spec section 12.5.
/// </summary>
public sealed class ChildEntry
{
    [JsonPropertyName("runId")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("nodeId")]
    public string? NodeId { get; set; }

    [JsonPropertyName("testId")]
    public string? TestId { get; set; }

    [JsonPropertyName("testVersion")]
    public string? TestVersion { get; set; }

    [JsonPropertyName("suiteId")]
    public string? SuiteId { get; set; }

    [JsonPropertyName("suiteVersion")]
    public string? SuiteVersion { get; set; }

    [JsonPropertyName("status")]
    public RunStatus Status { get; set; }
}
