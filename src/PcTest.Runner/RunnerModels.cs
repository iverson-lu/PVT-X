using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Runner;

public sealed class RunnerRequest
{
    public required string RunsRoot { get; init; }
    public required string TestCasePath { get; init; }
    public required TestCaseManifest Manifest { get; init; }
    public required string ResolvedRef { get; init; }
    public required Identity Identity { get; init; }
    public required Dictionary<string, string> EffectiveEnvironment { get; init; }
    public required Dictionary<string, object?> EffectiveInputs { get; init; }
    public required Dictionary<string, JsonElement> EffectiveInputsJson { get; init; }
    public required Dictionary<string, JsonElement> InputTemplates { get; init; }
    public required HashSet<string> SecretInputs { get; init; }
    public string? NodeId { get; init; }
    public string? SuiteId { get; init; }
    public string? SuiteVersion { get; init; }
    public string? PlanId { get; init; }
    public string? PlanVersion { get; init; }
    public string? WorkingDir { get; init; }
    public int? TimeoutSec { get; init; }
}

public sealed class RunnerResult
{
    public required string RunId { get; init; }
    public required RunStatus Status { get; init; }
    public required DateTimeOffset StartTime { get; init; }
    public required DateTimeOffset EndTime { get; init; }
    public int? ExitCode { get; init; }
    public string? ErrorType { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class ManifestSnapshot
{
    public required TestCaseManifest SourceManifest { get; init; }
    public required string ResolvedRef { get; init; }
    public required Identity ResolvedIdentity { get; init; }
    public required Dictionary<string, string> EffectiveEnvironment { get; init; }
    public required Dictionary<string, JsonElement> EffectiveInputs { get; init; }
    public required Dictionary<string, JsonElement> InputTemplates { get; init; }
    public required string ResolvedAt { get; init; }
    public string? EngineVersion { get; init; }
}
