using PcTest.Contracts.Models;

namespace PcTest.Runner;

public sealed class RunnerRequest
{
    public required string RunsRoot { get; init; }
    public required string TestCasePath { get; init; }
    public required TestCaseManifest Manifest { get; init; }
    public required Dictionary<string, object?> EffectiveInputs { get; init; }
    public required Dictionary<string, object?> RedactedInputs { get; init; }
    public required HashSet<string> SecretInputs { get; init; }
    public required Dictionary<string, string> EffectiveEnvironment { get; init; }
    public required Dictionary<string, string> RedactedEnvironment { get; init; }
    public string? WorkingDir { get; init; }
    public string? NodeId { get; init; }
    public string? SuiteId { get; init; }
    public string? SuiteVersion { get; init; }
    public string? PlanId { get; init; }
    public string? PlanVersion { get; init; }
    public string? ResolvedRef { get; init; }
    public required string EngineVersion { get; init; }
}
