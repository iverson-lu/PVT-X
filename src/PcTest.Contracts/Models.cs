using System.Text.Json.Nodes;

namespace PcTest.Contracts;

public static class SchemaVersions
{
    public const string Current = "1.5.0";
}

public sealed class ParameterDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public JsonNode? Default { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public string[]? EnumValues { get; set; }
    public string? Unit { get; set; }
    public string? UiHint { get; set; }
    public string? Pattern { get; set; }
    public string? Help { get; set; }
}

public sealed class TestCaseManifest
{
    public string SchemaVersion { get; set; } = SchemaVersions.Current;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? Privilege { get; set; }
    public int? TimeoutSec { get; set; }
    public string[]? Tags { get; set; }
    public List<ParameterDefinition>? Parameters { get; set; }
}

public sealed class TestCaseNode
{
    public string NodeId { get; set; } = string.Empty;
    public string Ref { get; set; } = string.Empty;
    public Dictionary<string, JsonNode?>? Inputs { get; set; }
}

public sealed class SuiteEnvironment
{
    public Dictionary<string, string>? Env { get; set; }
    public string? WorkingDir { get; set; }
    public JsonObject? RunnerHints { get; set; }
}

public sealed class TestSuiteManifest
{
    public string SchemaVersion { get; set; } = SchemaVersions.Current;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Version { get; set; } = string.Empty;
    public string[]? Tags { get; set; }
    public JsonObject? Controls { get; set; }
    public SuiteEnvironment? Environment { get; set; }
    public List<TestCaseNode> TestCases { get; set; } = new();
}

public sealed class PlanEnvironment
{
    public Dictionary<string, string>? Env { get; set; }
}

public sealed class TestPlanManifest
{
    public string SchemaVersion { get; set; } = SchemaVersions.Current;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Version { get; set; } = string.Empty;
    public string[]? Tags { get; set; }
    public PlanEnvironment? Environment { get; set; }
    public List<string> Suites { get; set; } = new();
}

public sealed class EnvRef
{
    public string Env { get; set; } = string.Empty;
    public JsonNode? Default { get; set; }
    public bool Required { get; set; }
    public bool Secret { get; set; }
}

public sealed class RunRequest
{
    public string? TestCase { get; set; }
    public string? Suite { get; set; }
    public string? Plan { get; set; }
    public Dictionary<string, JsonNode?>? CaseInputs { get; set; }
    public Dictionary<string, NodeOverride>? NodeOverrides { get; set; }
    public EnvironmentOverrides? EnvironmentOverrides { get; set; }
}

public sealed class NodeOverride
{
    public Dictionary<string, JsonNode?>? Inputs { get; set; }
}

public sealed class EnvironmentOverrides
{
    public Dictionary<string, string>? Env { get; set; }
}

public sealed class Identity
{
    public string Id { get; }
    public string Version { get; }

    public Identity(string id, string version)
    {
        Id = id;
        Version = version;
    }

    public override string ToString() => $"{Id}@{Version}";
}

public sealed record ValidationError(string Code, string Message, object? Payload = null);
