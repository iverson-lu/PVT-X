using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public enum RunType
{
    TestCase,
    TestSuite,
    TestPlan
}

public enum RunStatus
{
    Passed,
    Failed,
    Error,
    Timeout,
    Aborted
}

public enum ErrorType
{
    Timeout,
    ScriptError,
    RunnerError,
    Aborted
}

public sealed record Identity(string Id, string Version)
{
    public override string ToString() => $"{Id}@{Version}";
}

public sealed class ParameterDefinition
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("required")]
    public required bool Required { get; init; }

    [JsonPropertyName("default")]
    public JsonElement? Default { get; init; }

    [JsonPropertyName("min")]
    public double? Min { get; init; }

    [JsonPropertyName("max")]
    public double? Max { get; init; }

    [JsonPropertyName("enumValues")]
    public string[]? EnumValues { get; init; }

    [JsonPropertyName("unit")]
    public string? Unit { get; init; }

    [JsonPropertyName("uiHint")]
    public string? UiHint { get; init; }

    [JsonPropertyName("pattern")]
    public string? Pattern { get; init; }

    [JsonPropertyName("help")]
    public string? Help { get; init; }
}

public sealed class TestCaseManifest
{
    [JsonPropertyName("schemaVersion")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("category")]
    public required string Category { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("privilege")]
    public string? Privilege { get; init; }

    [JsonPropertyName("timeoutSec")]
    public int? TimeoutSec { get; init; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    [JsonPropertyName("parameters")]
    public ParameterDefinition[]? Parameters { get; init; }
}

public sealed class EnvironmentDefinition
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; init; }

    [JsonPropertyName("workingDir")]
    public string? WorkingDir { get; init; }
}

public sealed class SuiteTestCaseNode
{
    [JsonPropertyName("nodeId")]
    public required string NodeId { get; init; }

    [JsonPropertyName("ref")]
    public required string Ref { get; init; }

    [JsonPropertyName("inputs")]
    public Dictionary<string, JsonElement>? Inputs { get; init; }
}

public sealed class TestSuiteManifest
{
    [JsonPropertyName("schemaVersion")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    [JsonPropertyName("controls")]
    public JsonElement? Controls { get; init; }

    [JsonPropertyName("environment")]
    public EnvironmentDefinition? Environment { get; init; }

    [JsonPropertyName("testCases")]
    public required SuiteTestCaseNode[] TestCases { get; init; }
}

public sealed class TestPlanManifest
{
    [JsonPropertyName("schemaVersion")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    [JsonPropertyName("suites")]
    public required string[] Suites { get; init; }

    [JsonPropertyName("environment")]
    public EnvironmentDefinition? Environment { get; init; }
}

public sealed class EnvRef
{
    [JsonPropertyName("$env")]
    public required string Env { get; init; }

    [JsonPropertyName("default")]
    public JsonElement? Default { get; init; }

    [JsonPropertyName("required")]
    public bool Required { get; init; }

    [JsonPropertyName("secret")]
    public bool Secret { get; init; }
}

public sealed class NodeOverride
{
    [JsonPropertyName("inputs")]
    public Dictionary<string, JsonElement>? Inputs { get; init; }
}

public sealed class EnvironmentOverrides
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; init; }
}

public sealed class RunRequest
{
    [JsonPropertyName("suite")]
    public string? Suite { get; init; }

    [JsonPropertyName("testCase")]
    public string? TestCase { get; init; }

    [JsonPropertyName("plan")]
    public string? Plan { get; init; }

    [JsonPropertyName("nodeOverrides")]
    public Dictionary<string, NodeOverride>? NodeOverrides { get; init; }

    [JsonPropertyName("caseInputs")]
    public Dictionary<string, JsonElement>? CaseInputs { get; init; }

    [JsonPropertyName("environmentOverrides")]
    public EnvironmentOverrides? EnvironmentOverrides { get; init; }
}

public sealed class ManifestSnapshot
{
    public required object SourceManifest { get; init; }
    public required string ResolvedRef { get; init; }
    public required Identity ResolvedIdentity { get; init; }
    public required Dictionary<string, string> EffectiveEnvironment { get; init; }
    public required Dictionary<string, object?> EffectiveInputs { get; init; }
    public Dictionary<string, JsonElement>? InputTemplates { get; init; }
    public DateTimeOffset ResolvedAt { get; init; }
    public string? EngineVersion { get; init; }
}

public sealed class RunnerResult
{
    public required string RunId { get; init; }
    public required RunStatus Status { get; init; }
    public required DateTimeOffset StartTime { get; init; }
    public required DateTimeOffset EndTime { get; init; }
    public int? ExitCode { get; init; }
    public ErrorDetail? Error { get; init; }
    public string? WorkingDirectory { get; init; }
}

public sealed class ErrorDetail
{
    public required ErrorType Type { get; init; }
    public required string Source { get; init; }
    public string? Message { get; init; }
    public string? Stack { get; init; }
}

public sealed class TestCaseResult
{
    public required string SchemaVersion { get; init; }
    public string RunType { get; init; } = "TestCase";
    public string? NodeId { get; init; }
    public required string TestId { get; init; }
    public required string TestVersion { get; init; }
    public string? SuiteId { get; init; }
    public string? SuiteVersion { get; init; }
    public string? PlanId { get; init; }
    public string? PlanVersion { get; init; }
    public required RunStatus Status { get; init; }
    public required DateTimeOffset StartTime { get; init; }
    public required DateTimeOffset EndTime { get; init; }
    public Dictionary<string, object?>? Metrics { get; init; }
    public string? Message { get; init; }
    public int? ExitCode { get; init; }
    public required Dictionary<string, object?> EffectiveInputs { get; init; }
    public ErrorDetail? Error { get; init; }
    public object? Runner { get; init; }
}

public sealed class SummaryResult
{
    public required string SchemaVersion { get; init; }
    public required string RunType { get; init; }
    public string? SuiteId { get; init; }
    public string? SuiteVersion { get; init; }
    public string? PlanId { get; init; }
    public string? PlanVersion { get; init; }
    public required RunStatus Status { get; init; }
    public required DateTimeOffset StartTime { get; init; }
    public required DateTimeOffset EndTime { get; init; }
    public Dictionary<string, int>? Counts { get; init; }
    public required string[] ChildRunIds { get; init; }
    public string? Message { get; init; }
}

public sealed class IndexEntry
{
    public required string RunId { get; init; }
    public required string RunType { get; init; }
    public string? NodeId { get; init; }
    public string? TestId { get; init; }
    public string? TestVersion { get; init; }
    public string? SuiteId { get; init; }
    public string? SuiteVersion { get; init; }
    public string? PlanId { get; init; }
    public string? PlanVersion { get; init; }
    public string? ParentRunId { get; init; }
    public required DateTimeOffset StartTime { get; init; }
    public required DateTimeOffset EndTime { get; init; }
    public required RunStatus Status { get; init; }
}
