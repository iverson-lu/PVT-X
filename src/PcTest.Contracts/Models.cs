using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public sealed record Identity(string Id, string Version)
{
    public override string ToString() => $"{Id}@{Version}";

    public static Identity Parse(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new FormatException("Identity cannot be empty.");
        }

        var parts = trimmed.Split('@');
        if (parts.Length != 2)
        {
            throw new FormatException("Identity must contain exactly one '@'.");
        }

        var id = parts[0];
        var version = parts[1];
        if (id.Length == 0 || version.Length == 0)
        {
            throw new FormatException("Identity must include id and version.");
        }

        if (HasWhitespace(id) || HasWhitespace(version))
        {
            throw new FormatException("Identity must not contain whitespace.");
        }

        if (!IdPattern.IsMatch(id))
        {
            throw new FormatException("Identity id has invalid characters.");
        }

        return new Identity(id, version);
    }

    private static bool HasWhitespace(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly System.Text.RegularExpressions.Regex IdPattern =
        new("^[A-Za-z0-9._-]+$", System.Text.RegularExpressions.RegexOptions.Compiled);
}

public sealed record TestCaseManifest
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

    public Identity Identity => new(Id, Version);
}

public sealed record ParameterDefinition
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

public sealed record TestSuiteManifest
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
    public SuiteEnvironment? Environment { get; init; }

    [JsonPropertyName("testCases")]
    public required TestCaseNode[] TestCases { get; init; }

    public Identity Identity => new(Id, Version);
}

public sealed record TestCaseNode
{
    [JsonPropertyName("nodeId")]
    public required string NodeId { get; init; }

    [JsonPropertyName("ref")]
    public required string Ref { get; init; }

    [JsonPropertyName("inputs")]
    public Dictionary<string, InputValue>? Inputs { get; init; }
}

public sealed record SuiteEnvironment
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; init; }

    [JsonPropertyName("workingDir")]
    public string? WorkingDir { get; init; }

    [JsonPropertyName("runnerHints")]
    public JsonElement? RunnerHints { get; init; }
}

public sealed record TestPlanManifest
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

    [JsonPropertyName("environment")]
    public PlanEnvironment? Environment { get; init; }

    [JsonPropertyName("suites")]
    public required string[] Suites { get; init; }

    public Identity Identity => new(Id, Version);
}

public sealed record PlanEnvironment
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

public sealed record EnvRef
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

public sealed record InputValue
{
    public InputValue(JsonElement literal)
    {
        Literal = literal;
    }

    public InputValue(EnvRef envRef)
    {
        EnvRef = envRef;
    }

    public JsonElement? Literal { get; }

    public EnvRef? EnvRef { get; }

    public bool IsEnvRef => EnvRef is not null;
}

public sealed record RunRequest
{
    [JsonPropertyName("suite")]
    public string? Suite { get; init; }

    [JsonPropertyName("testCase")]
    public string? TestCase { get; init; }

    [JsonPropertyName("plan")]
    public string? Plan { get; init; }

    [JsonPropertyName("caseInputs")]
    public Dictionary<string, InputValue>? CaseInputs { get; init; }

    [JsonPropertyName("nodeOverrides")]
    public Dictionary<string, NodeOverride>? NodeOverrides { get; init; }

    [JsonPropertyName("environmentOverrides")]
    public EnvironmentOverrides? EnvironmentOverrides { get; init; }
}

public sealed record NodeOverride
{
    [JsonPropertyName("inputs")]
    public Dictionary<string, InputValue>? Inputs { get; init; }
}

public sealed record EnvironmentOverrides
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; init; }
}

public enum ResultStatus
{
    Passed,
    Failed,
    Error,
    Timeout,
    Aborted
}

public enum ErrorType
{
    RunnerError,
    ScriptError
}

public sealed record RunnerError
{
    [JsonPropertyName("type")]
    public required ErrorType Type { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

public sealed record CaseResult
{
    [JsonPropertyName("runId")]
    public required string RunId { get; init; }

    [JsonPropertyName("status")]
    public required ResultStatus Status { get; init; }

    [JsonPropertyName("startTimeUtc")]
    public required DateTimeOffset StartTimeUtc { get; init; }

    [JsonPropertyName("endTimeUtc")]
    public required DateTimeOffset EndTimeUtc { get; init; }

    [JsonPropertyName("durationSec")]
    public required double DurationSec { get; init; }

    [JsonPropertyName("nodeId")]
    public string? NodeId { get; init; }

    [JsonPropertyName("suiteId")]
    public string? SuiteId { get; init; }

    [JsonPropertyName("planId")]
    public string? PlanId { get; init; }

    [JsonPropertyName("error")]
    public RunnerError? Error { get; init; }
}

public sealed record EngineEvent
{
    [JsonPropertyName("timestampUtc")]
    public required DateTimeOffset TimestampUtc { get; init; }

    [JsonPropertyName("level")]
    public required string Level { get; init; }

    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("data")]
    public Dictionary<string, string>? Data { get; init; }
}
