using PcTest.Contracts;

namespace PcTest.Runner;

public sealed class CaseRunManifestSnapshot
{
    public CaseRunManifestSnapshot(
        TestCaseManifest sourceManifest,
        string resolvedRef,
        Identity resolvedIdentity,
        IReadOnlyDictionary<string, string> effectiveEnvironment,
        IReadOnlyDictionary<string, object> effectiveInputs,
        IReadOnlyDictionary<string, object> inputTemplates,
        string engineVersion)
    {
        SourceManifest = sourceManifest;
        ResolvedRef = resolvedRef;
        ResolvedIdentity = resolvedIdentity;
        EffectiveEnvironment = effectiveEnvironment;
        EffectiveInputs = effectiveInputs;
        InputTemplates = inputTemplates;
        EngineVersion = engineVersion;
        ResolvedAt = DateTimeOffset.UtcNow.ToString("O");
    }

    public TestCaseManifest SourceManifest { get; }
    public string ResolvedRef { get; }
    public Identity ResolvedIdentity { get; }
    public IReadOnlyDictionary<string, string> EffectiveEnvironment { get; }
    public IReadOnlyDictionary<string, object> EffectiveInputs { get; }
    public IReadOnlyDictionary<string, object> InputTemplates { get; }
    public string EngineVersion { get; }
    public string ResolvedAt { get; }
}

public sealed class RunnerRequest
{
    public RunnerRequest(
        TestCaseManifest testCase,
        string manifestPath,
        IReadOnlyDictionary<string, object> effectiveInputs,
        IReadOnlyDictionary<string, object> redactedInputs,
        IReadOnlyDictionary<string, bool> secretInputs,
        IReadOnlyDictionary<string, string> effectiveEnvironment,
        IReadOnlyDictionary<string, string>? environmentOverrides,
        string? nodeId,
        Identity? suiteIdentity,
        Identity? planIdentity,
        CaseRunManifestSnapshot manifestSnapshot,
        string runsRoot,
        string? parentRunId,
        string? workingDirectory)
    {
        TestCase = testCase;
        ManifestPath = manifestPath;
        EffectiveInputs = effectiveInputs;
        RedactedInputs = redactedInputs;
        SecretInputs = secretInputs;
        EffectiveEnvironment = effectiveEnvironment;
        EnvironmentOverrides = environmentOverrides;
        NodeId = nodeId;
        SuiteIdentity = suiteIdentity;
        PlanIdentity = planIdentity;
        ManifestSnapshot = manifestSnapshot;
        RunsRoot = runsRoot;
        ParentRunId = parentRunId;
        WorkingDirectory = workingDirectory;
    }

    public TestCaseManifest TestCase { get; }
    public string ManifestPath { get; }
    public IReadOnlyDictionary<string, object> EffectiveInputs { get; }
    public IReadOnlyDictionary<string, object> RedactedInputs { get; }
    public IReadOnlyDictionary<string, bool> SecretInputs { get; }
    public IReadOnlyDictionary<string, string> EffectiveEnvironment { get; }
    public IReadOnlyDictionary<string, string>? EnvironmentOverrides { get; }
    public string? NodeId { get; }
    public Identity? SuiteIdentity { get; }
    public Identity? PlanIdentity { get; }
    public CaseRunManifestSnapshot ManifestSnapshot { get; }
    public string RunsRoot { get; }
    public string? ParentRunId { get; }
    public string? WorkingDirectory { get; }
}

public sealed record RunnerResult(
    string RunId,
    string Status,
    string StartTime,
    string EndTime,
    string? NodeId,
    string? ParentRunId);
