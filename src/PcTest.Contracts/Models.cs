using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public static class ErrorCodes
{
    public const string DiscoveryDuplicateId = "Discovery.DuplicateId";
    public const string ManifestInvalid = "Manifest.Invalid";
    public const string SuiteTestCaseRefInvalid = "Suite.TestCaseRef.Invalid";
    public const string EnvRefResolveFailed = "EnvRef.ResolveFailed";
    public const string RunnerError = "RunnerError";
    public const string ScriptError = "ScriptError";
}

public static class WarningCodes
{
    public const string ControlsMaxParallelIgnored = "Controls.MaxParallel.Ignored";
    public const string EnvRefSecretOnCommandLine = "EnvRef.SecretOnCommandLine";
}

public static class EventCodes
{
    public const string RunnerStarted = "Runner.Started";
    public const string RunnerCompleted = "Runner.Completed";
}

public static class IdVersion
{
    public static bool TryParse(string value, out string id, out string version)
    {
        id = string.Empty;
        version = string.Empty;
        var parts = value.Split('@', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        id = parts[0];
        version = parts[1];
        return !(string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version));
    }

    public static string Format(string id, string version) => $"{id}@{version}";
}

public sealed class EnvRef
{
    [JsonPropertyName("$env")]
    public string? Name { get; set; }

    [JsonPropertyName("default")]
    public JsonElement? Default { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("secret")]
    public bool Secret { get; set; }
}

public sealed class ParameterDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("default")]
    public JsonElement? Default { get; set; }

    [JsonPropertyName("enumValues")]
    public JsonElement? EnumValues { get; set; }
}

public sealed class TestCaseManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("script")]
    public string Script { get; set; } = "";

    [JsonPropertyName("parameters")]
    public Dictionary<string, ParameterDefinition> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("environment")]
    public Dictionary<string, string>? Environment { get; set; }
}

public sealed class TestSuiteNode
{
    [JsonPropertyName("nodeId")]
    public string NodeId { get; set; } = "";

    [JsonPropertyName("ref")]
    public string Ref { get; set; } = "";

    [JsonPropertyName("inputs")]
    public Dictionary<string, JsonElement>? Inputs { get; set; }

    [JsonPropertyName("continueOnFailure")]
    public bool ContinueOnFailure { get; set; }

    [JsonPropertyName("retryOnError")]
    public int RetryOnError { get; set; }

    [JsonPropertyName("repeat")]
    public int Repeat { get; set; } = 1;
}

public sealed class TestSuiteManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("nodes")]
    public List<TestSuiteNode> Nodes { get; set; } = [];

    [JsonPropertyName("environment")]
    public Dictionary<string, string>? Environment { get; set; }

    [JsonPropertyName("controls")]
    public SuiteControls? Controls { get; set; }
}

public sealed class SuiteControls
{
    [JsonPropertyName("maxParallel")]
    public int? MaxParallel { get; set; }
}

public sealed class TestPlanManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("suites")]
    public List<string> Suites { get; set; } = [];

    [JsonPropertyName("environment")]
    public Dictionary<string, string>? Environment { get; set; }
}

public sealed class RunRequest
{
    [JsonPropertyName("envOverride")]
    public Dictionary<string, string>? EnvOverride { get; set; }

    [JsonPropertyName("caseInputs")]
    public Dictionary<string, JsonElement>? CaseInputs { get; set; }

    [JsonPropertyName("nodeOverrides")]
    public Dictionary<string, NodeOverride>? NodeOverrides { get; set; }
}

public sealed class NodeOverride
{
    [JsonPropertyName("inputs")]
    public Dictionary<string, JsonElement>? Inputs { get; set; }
}

public sealed class CaseResult
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "Error";

    [JsonPropertyName("error")]
    public ResultError? Error { get; set; }
}

public sealed class ResultError
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("payload")]
    public object? Payload { get; set; }
}

public sealed class SuiteResult
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "Error";

    [JsonPropertyName("children")]
    public List<ChildResult> Children { get; set; } = [];
}

public sealed class ChildResult
{
    [JsonPropertyName("nodeId")]
    public string NodeId { get; set; } = "";

    [JsonPropertyName("caseRunId")]
    public string CaseRunId { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
}

public sealed class RunIndexEntry
{
    [JsonPropertyName("runId")]
    public string RunId { get; set; } = "";

    [JsonPropertyName("entity")]
    public string Entity { get; set; } = "";

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
}

public sealed class RedactionMetadata
{
    [JsonPropertyName("secretInputs")]
    public List<string> SecretInputs { get; set; } = [];

    [JsonPropertyName("secretEnv")]
    public List<string> SecretEnv { get; set; } = [];
}

public sealed class RunnerRequest
{
    [JsonPropertyName("caseRunId")]
    public string CaseRunId { get; set; } = "";

    [JsonPropertyName("caseRunFolder")]
    public string CaseRunFolder { get; set; } = "";

    [JsonPropertyName("manifestPath")]
    public string ManifestPath { get; set; } = "";

    [JsonPropertyName("scriptPath")]
    public string ScriptPath { get; set; } = "";

    [JsonPropertyName("workingDir")]
    public string WorkingDir { get; set; } = "";

    [JsonPropertyName("inputs")]
    public Dictionary<string, object?> Inputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("environment")]
    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("redaction")]
    public RedactionMetadata Redaction { get; set; } = new();
}

public sealed class RunnerResult
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "Error";

    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; set; }

    [JsonPropertyName("error")]
    public ResultError? Error { get; set; }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
