using System.Text.Json;

namespace PcTest.Contracts;

public sealed record TestCaseExecutionRequest
{
    public string TestCasePath { get; init; } = "";
    public TestCaseManifest TestCase { get; init; } = new();
    public JsonElement SourceManifest { get; init; }
    public string ResolvedRef { get; init; } = "";
    public Identity Identity { get; init; }
    public string RunId { get; init; } = "";
    public string RunsRoot { get; init; } = "";
    public string? NodeId { get; init; }
    public Identity? SuiteIdentity { get; init; }
    public Identity? PlanIdentity { get; init; }
    public IReadOnlyDictionary<string, object?> EffectiveInputs { get; init; } = new Dictionary<string, object?>();
    public IReadOnlyDictionary<string, object?> RedactedInputs { get; init; } = new Dictionary<string, object?>();
    public IReadOnlyCollection<string> SecretInputs { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> EffectiveEnvironment { get; init; } = new Dictionary<string, string>();
    public string? WorkingDir { get; init; }
    public JsonElement? InputTemplates { get; init; }
    public string? EngineVersion { get; init; }
}

public sealed record TestCaseExecutionResult
{
    public string RunId { get; init; } = "";
    public string RunFolder { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }
    public int? ExitCode { get; init; }
    public string? ErrorType { get; init; }
    public string? ErrorMessage { get; init; }
}
