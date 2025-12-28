using System.Text.Json;

namespace PcTest.Contracts;

public sealed record Identity(string Id, string Version)
{
    public override string ToString() => $"{Id}@{Version}";

    public static bool TryParse(string value, out Identity identity)
    {
        identity = new Identity(string.Empty, string.Empty);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        int at = value.LastIndexOf('@');
        if (at <= 0 || at == value.Length - 1)
        {
            return false;
        }

        string id = value.Substring(0, at);
        string version = value.Substring(at + 1);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        identity = new Identity(id, version);
        return true;
    }

    public static Identity Parse(string value)
    {
        if (!TryParse(value, out Identity identity))
        {
            throw new FormatException($"Invalid identity '{value}'. Expected id@version.");
        }

        return identity;
    }
}

public sealed record ParameterDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public bool Required { get; init; }
    public JsonElement? Default { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public string[]? EnumValues { get; init; }
    public string? Unit { get; init; }
    public string? UiHint { get; init; }
    public string? Pattern { get; init; }
    public string? Help { get; init; }
}

public sealed record TestCaseManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Version { get; init; } = string.Empty;
    public string? Privilege { get; init; }
    public int? TimeoutSec { get; init; }
    public string[]? Tags { get; init; }
    public List<ParameterDefinition>? Parameters { get; init; }
}

public sealed record SuiteEnvironment
{
    public Dictionary<string, string>? Env { get; init; }
    public string? WorkingDir { get; init; }
    public JsonElement? RunnerHints { get; init; }
}

public sealed record SuiteControls
{
    public int? Repeat { get; init; }
    public int? MaxParallel { get; init; }
    public bool? ContinueOnFailure { get; init; }
    public int? RetryOnError { get; init; }
    public string? TimeoutPolicy { get; init; }
}

public sealed record TestCaseNode
{
    public string NodeId { get; init; } = string.Empty;
    public string Ref { get; init; } = string.Empty;
    public Dictionary<string, JsonElement>? Inputs { get; init; }
}

public sealed record TestSuiteManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Version { get; init; } = string.Empty;
    public string[]? Tags { get; init; }
    public SuiteControls? Controls { get; init; }
    public SuiteEnvironment? Environment { get; init; }
    public List<TestCaseNode> TestCases { get; init; } = new();
}

public sealed record PlanEnvironment
{
    public Dictionary<string, string>? Env { get; init; }
}

public sealed record TestPlanManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Version { get; init; } = string.Empty;
    public string[]? Tags { get; init; }
    public PlanEnvironment? Environment { get; init; }
    public List<string> Suites { get; init; } = new();
}

public sealed record RunRequest
{
    public string? TestCase { get; init; }
    public string? Suite { get; init; }
    public string? Plan { get; init; }
    public Dictionary<string, JsonElement>? CaseInputs { get; init; }
    public Dictionary<string, NodeOverride>? NodeOverrides { get; init; }
    public EnvironmentOverrides? EnvironmentOverrides { get; init; }
}

public sealed record NodeOverride
{
    public Dictionary<string, JsonElement>? Inputs { get; init; }
}

public sealed record EnvironmentOverrides
{
    public Dictionary<string, string>? Env { get; init; }
}

public sealed record ResolvedTestCase
{
    public TestCaseManifest Manifest { get; init; } = new();
    public string ManifestPath { get; init; } = string.Empty;
    public Identity Identity { get; init; } = new(string.Empty, string.Empty);
}

public sealed record ResolvedTestSuite
{
    public TestSuiteManifest Manifest { get; init; } = new();
    public string ManifestPath { get; init; } = string.Empty;
    public Identity Identity { get; init; } = new(string.Empty, string.Empty);
}

public sealed record ResolvedTestPlan
{
    public TestPlanManifest Manifest { get; init; } = new();
    public string ManifestPath { get; init; } = string.Empty;
    public Identity Identity { get; init; } = new(string.Empty, string.Empty);
}

public sealed record ValidationError(string Code, string Message, Dictionary<string, object?> Payload);

public sealed record ValidationResult
{
    public List<ValidationError> Errors { get; } = new();
    public bool IsValid => Errors.Count == 0;
    public void Add(string code, string message, Dictionary<string, object?> payload)
    {
        Errors.Add(new ValidationError(code, message, payload));
    }
}

public sealed record EffectiveInputsResult
{
    public Dictionary<string, object> Inputs { get; init; } = new();
    public Dictionary<string, object> RedactedInputs { get; init; } = new();
    public HashSet<string> SecretKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
