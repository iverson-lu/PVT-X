using System.Text.Json.Nodes;
using PcTest.Contracts;

namespace PcTest.Runner;

public interface ICaseRunner
{
    RunCaseResult Run(RunCaseRequest request);
}

public sealed class RunCaseRequest
{
    public string RunsRoot { get; init; } = string.Empty;
    public TestCaseManifest Manifest { get; init; } = new();
    public string ManifestPath { get; init; } = string.Empty;
    public string ResolvedRef { get; init; } = string.Empty;
    public Dictionary<string, object?> EffectiveInputs { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, object?> InputTemplates { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> EffectiveEnvironment { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SecretInputs { get; init; } = new(StringComparer.Ordinal);
    public string? WorkingDir { get; init; }
    public string? NodeId { get; init; }
    public Identity? SuiteIdentity { get; init; }
    public Identity? PlanIdentity { get; init; }
}

public sealed class RunCaseResult
{
    public string RunId { get; init; } = string.Empty;
    public string RunFolder { get; init; } = string.Empty;
    public string Status { get; init; } = "Error";
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }
    public int? ExitCode { get; init; }
    public string? ErrorType { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class RunnerEvent
{
    public DateTimeOffset Timestamp { get; init; }
    public string Level { get; init; } = "Warning";
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public object? Data { get; init; }
}
