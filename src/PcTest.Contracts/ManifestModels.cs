using System.Text.Json;

namespace PcTest.Contracts;

public sealed class TestCaseManifest
{
    public string SchemaVersion { get; set; } = SpecConstants.SchemaVersion;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? Privilege { get; set; }
    public int? TimeoutSec { get; set; }
    public string[]? Tags { get; set; }
    public ParameterDefinition[]? Parameters { get; set; }
}

public sealed class ParameterDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public JsonElement? Default { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public string[]? EnumValues { get; set; }
    public string? Unit { get; set; }
    public string? UiHint { get; set; }
    public string? Pattern { get; set; }
    public string? Help { get; set; }
}

public sealed class SuiteManifest
{
    public string SchemaVersion { get; set; } = SpecConstants.SchemaVersion;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Version { get; set; } = string.Empty;
    public string[]? Tags { get; set; }
    public JsonElement? Controls { get; set; }
    public JsonElement? Environment { get; set; }
    public SuiteNode[] TestCases { get; set; } = Array.Empty<SuiteNode>();
}

public sealed class SuiteNode
{
    public string NodeId { get; set; } = string.Empty;
    public string Ref { get; set; } = string.Empty;
    public Dictionary<string, JsonElement>? Inputs { get; set; }
}

public sealed class PlanManifest
{
    public string SchemaVersion { get; set; } = SpecConstants.SchemaVersion;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Version { get; set; } = string.Empty;
    public string[]? Tags { get; set; }
    public JsonElement? Environment { get; set; }
    public string[] Suites { get; set; } = Array.Empty<string>();
}
