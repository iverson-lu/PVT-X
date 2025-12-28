using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public sealed class RunRequest
{
    [JsonPropertyName("suite")]
    public string? Suite { get; init; }

    [JsonPropertyName("plan")]
    public string? Plan { get; init; }

    [JsonPropertyName("testCase")]
    public string? TestCase { get; init; }

    [JsonPropertyName("nodeOverrides")]
    public Dictionary<string, RunNodeOverride>? NodeOverrides { get; init; }

    [JsonPropertyName("caseInputs")]
    public Dictionary<string, JsonElement>? CaseInputs { get; init; }

    [JsonPropertyName("environmentOverrides")]
    public EnvironmentOverrides? EnvironmentOverrides { get; init; }
}

public sealed class RunNodeOverride
{
    [JsonPropertyName("inputs")]
    public Dictionary<string, JsonElement>? Inputs { get; init; }
}

public sealed class EnvironmentOverrides
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

public sealed class EnvRef
{
    [JsonPropertyName("$env")]
    public string Env { get; init; } = string.Empty;

    [JsonPropertyName("default")]
    public JsonElement? Default { get; init; }

    [JsonPropertyName("required")]
    public bool? Required { get; init; }

    [JsonPropertyName("secret")]
    public bool? Secret { get; init; }
}

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
    public JsonElement? Metrics { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; init; }

    [JsonPropertyName("effectiveInputs")]
    public Dictionary<string, JsonElement> EffectiveInputs { get; init; } = new();

    [JsonPropertyName("error")]
    public ErrorDetail? Error { get; init; }

    [JsonPropertyName("runner")]
    public JsonElement? Runner { get; init; }
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
    public Dictionary<string, int>? Counts { get; init; }

    [JsonPropertyName("childRunIds")]
    public string[] ChildRunIds { get; init; } = Array.Empty<string>();

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

public sealed class ErrorDetail
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
