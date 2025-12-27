using PcTest.Contracts;

namespace PcTest.Runner;

public sealed record TestCaseRunRequest
{
    public required string RunsRoot { get; init; }
    public required string CaseRoot { get; init; }
    public required string ResolvedRef { get; init; }
    public required Identity Identity { get; init; }
    public required TestCaseManifest Manifest { get; init; }
    public required Dictionary<string, object?> EffectiveInputs { get; init; }
    public required Dictionary<string, object?> RedactedInputs { get; init; }
    public required Dictionary<string, string> EffectiveEnvironment { get; init; }
    public required HashSet<string> SecretInputs { get; init; }
    public string? NodeId { get; init; }
    public string? SuiteId { get; init; }
    public string? SuiteVersion { get; init; }
    public string? PlanId { get; init; }
    public string? PlanVersion { get; init; }
    public string? WorkingDir { get; init; }
    public int? TimeoutSec { get; init; }
    public IReadOnlyList<RunEvent> Events { get; init; } = Array.Empty<RunEvent>();
}

public sealed record TestCaseRunResult
{
    public required string RunId { get; init; }
    public required RunStatus Status { get; init; }
    public required string StartTime { get; init; }
    public required string EndTime { get; init; }
    public int? ExitCode { get; init; }
    public ErrorInfo? Error { get; init; }
    public required string CaseRunFolder { get; init; }
    public required TestCaseResult ResultPayload { get; init; }
}
