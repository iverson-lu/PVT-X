using System.Text.Json;

namespace PcTest.Contracts;

public enum RunStatus
{
    Passed,
    Failed,
    Error,
    Timeout,
    Aborted
}

public sealed record IdentityReference(string Id, string Version)
{
    public static IdentityReference FromIdentity(Identity identity) => new(identity.Id, identity.Version);
}

public sealed record CaseRunResult(
    string RunId,
    Identity TestCaseIdentity,
    RunStatus Status,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int? ExitCode,
    CaseRunError? Error,
    string? NodeId,
    Identity? SuiteIdentity,
    Identity? PlanIdentity,
    IReadOnlyDictionary<string, object?> EffectiveInputs,
    IReadOnlyDictionary<string, object?> EffectiveInputsRedacted
);

public sealed record CaseRunError(string Type, string Source, string Message, string? Stack);

public sealed record GroupRunResult(
    string RunId,
    string RunType,
    RunStatus Status,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    IReadOnlyList<string> ChildRunIds,
    Identity? SuiteIdentity,
    Identity? PlanIdentity
);

public sealed record CaseRunRequest(
    string RunsRoot,
    string TestCaseFolder,
    string ManifestPath,
    string ResolvedRef,
    Identity TestCaseIdentity,
    JsonElement SourceManifest,
    IReadOnlyDictionary<string, string> ParameterTypes,
    IReadOnlyDictionary<string, object?> EffectiveInputs,
    IReadOnlyDictionary<string, object?> EffectiveInputsRedacted,
    IReadOnlyDictionary<string, string> EffectiveEnvironment,
    IReadOnlyDictionary<string, JsonElement> InputTemplates,
    IReadOnlyCollection<string> SecretInputNames,
    string? WorkingDirectory,
    int? TimeoutSec,
    string? NodeId,
    Identity? SuiteIdentity,
    Identity? PlanIdentity,
    string EngineVersion
);

public interface ICaseRunner
{
    Task<CaseRunResult> RunCaseAsync(CaseRunRequest request, CancellationToken cancellationToken);
}
