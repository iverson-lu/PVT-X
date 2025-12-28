using System.Text.Json;

namespace PcTest.Runner;

public sealed record CaseRunRequest
{
    public string RunsRoot { get; init; } = string.Empty;
    public string ManifestPath { get; init; } = string.Empty;
    public string ScriptPath { get; init; } = string.Empty;
    public string TestId { get; init; } = string.Empty;
    public string TestVersion { get; init; } = string.Empty;
    public string? ParentRunId { get; init; }
    public string? NodeId { get; init; }
    public string? SuiteId { get; init; }
    public string? SuiteVersion { get; init; }
    public string? PlanId { get; init; }
    public string? PlanVersion { get; init; }
    public string? WorkingDir { get; init; }
    public Dictionary<string, object> EffectiveInputs { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object> RedactedInputs { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SecretKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> EffectiveEnvironment { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public JsonElement SourceManifest { get; init; }
    public JsonElement? InputTemplates { get; init; }
    public IReadOnlyList<ParameterDefinitionSnapshot> Parameters { get; init; } = Array.Empty<ParameterDefinitionSnapshot>();
    public string PwshPath { get; init; } = "pwsh";
    public int? TimeoutSec { get; init; }
}

public sealed record ParameterDefinitionSnapshot
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
}

public sealed record CaseRunResult
{
    public string RunId { get; init; } = string.Empty;
    public string RunFolder { get; init; } = string.Empty;
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }
    public string Status { get; init; } = string.Empty;
    public int? ExitCode { get; init; }
    public string? ErrorType { get; init; }
    public string? ErrorMessage { get; init; }
}
