using System.Collections;
using System.Text.Json;
using PcTest.Contracts;
using PcTest.Runner;

namespace PcTest.Engine;

public sealed class EngineRunner
{
    private readonly TestCaseRunner _runner;
    private readonly InputResolver _inputResolver;

    public EngineRunner(TestCaseRunner runner)
    {
        _runner = runner;
        _inputResolver = new InputResolver();
    }

    public async Task<EngineRunSummary> RunAsync(EngineRunContext context, CancellationToken cancellationToken)
    {
        var discovery = new DiscoveryService().Discover(context.DiscoveryRoots);
        var runRequest = context.RunRequest;

        var targets = new[] { runRequest.Plan, runRequest.Suite, runRequest.TestCase }.Count(value => !string.IsNullOrWhiteSpace(value));
        if (targets != 1)
        {
            throw new ValidationException("RunRequest.Invalid", new Dictionary<string, object>
            {
                ["reason"] = "MultipleTargets"
            });
        }

        if (!string.IsNullOrWhiteSpace(runRequest.Plan))
        {
            if (runRequest.CaseInputs is not null || runRequest.NodeOverrides is not null)
            {
                throw new ValidationException("Plan.RunRequest.Invalid", new Dictionary<string, object>
                {
                    ["reason"] = "PlanInputsNotAllowed"
                });
            }

            return await RunPlanAsync(context, discovery, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(runRequest.Suite))
        {
            if (runRequest.CaseInputs is not null)
            {
                throw new ValidationException("RunRequest.Invalid", new Dictionary<string, object>
                {
                    ["reason"] = "SuiteCaseInputsNotAllowed"
                });
            }

            return await RunSuiteAsync(context, discovery, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(runRequest.TestCase))
        {
            if (runRequest.NodeOverrides is not null)
            {
                throw new ValidationException("RunRequest.Invalid", new Dictionary<string, object>
                {
                    ["reason"] = "CaseNodeOverridesNotAllowed"
                });
            }

            return await RunStandaloneAsync(context, discovery, cancellationToken).ConfigureAwait(false);
        }

        throw new ValidationException("RunRequest.Invalid", new Dictionary<string, object>
        {
            ["reason"] = "MissingTarget"
        });
    }

    private async Task<EngineRunSummary> RunStandaloneAsync(EngineRunContext context, DiscoveryResult discovery, CancellationToken cancellationToken)
    {
        var identity = Identity.Parse(context.RunRequest.TestCase ?? string.Empty);
        var testCase = discovery.TestCases.SingleOrDefault(tc => tc.Manifest.Identity == identity);
        if (testCase is null)
        {
            throw new ValidationException("RunRequest.ResolveFailed", new Dictionary<string, object>
            {
                ["entityType"] = "TestCase",
                ["id"] = identity.Id,
                ["version"] = identity.Version,
                ["reason"] = "NotFound"
            });
        }

        var effectiveEnvironment = ResolveEnvironment(context.RunRequest.EnvironmentOverrides, null, null);
        var resolvedInputs = _inputResolver.Resolve(testCase.Manifest, context.RunRequest.CaseInputs, effectiveEnvironment, null);
        var scriptPath = Path.Combine(Path.GetDirectoryName(testCase.Path) ?? string.Empty, "run.ps1");

        var caseContext = new CaseRunContext
        {
            RunsRoot = context.RunsRoot,
            PowerShellPath = context.PowerShellPath,
            ScriptPath = scriptPath,
            TestCaseManifest = testCase.Manifest,
            ResolvedRef = testCase.Path,
            EffectiveInputs = resolvedInputs.Values,
            EffectiveEnvironment = effectiveEnvironment,
            SecretInputValues = resolvedInputs.SecretInputs,
            SecretEnvironmentKeys = new HashSet<string>(StringComparer.Ordinal),
            ParameterDefinitions = resolvedInputs.ParameterDefinitions,
            WorkingDir = null,
            Timeout = testCase.Manifest.TimeoutSec.HasValue ? TimeSpan.FromSeconds(testCase.Manifest.TimeoutSec.Value) : null
        };

        var result = await _runner.RunAsync(caseContext, cancellationToken).ConfigureAwait(false);
        return new EngineRunSummary(result.Status, new[] { result.RunId });
    }

    private async Task<EngineRunSummary> RunSuiteAsync(EngineRunContext context, DiscoveryResult discovery, CancellationToken cancellationToken)
    {
        var identity = Identity.Parse(context.RunRequest.Suite ?? string.Empty);
        var suite = discovery.TestSuites.SingleOrDefault(s => s.Manifest.Identity == identity);
        if (suite is null)
        {
            throw new ValidationException("RunRequest.ResolveFailed", new Dictionary<string, object>
            {
                ["entityType"] = "TestSuite",
                ["id"] = identity.Id,
                ["version"] = identity.Version,
                ["reason"] = "NotFound"
            });
        }

        return await RunSuiteManifestAsync(context, discovery, suite, null, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<EngineRunSummary> RunPlanAsync(EngineRunContext context, DiscoveryResult discovery, CancellationToken cancellationToken)
    {
        var identity = Identity.Parse(context.RunRequest.Plan ?? string.Empty);
        var plan = discovery.TestPlans.SingleOrDefault(p => p.Manifest.Identity == identity);
        if (plan is null)
        {
            throw new ValidationException("RunRequest.ResolveFailed", new Dictionary<string, object>
            {
                ["entityType"] = "TestPlan",
                ["id"] = identity.Id,
                ["version"] = identity.Version,
                ["reason"] = "NotFound"
            });
        }

        var groupRunId = context.GroupRunIdFactory.NewRunId();
        var planRunFolder = Path.Combine(context.RunsRoot, groupRunId);
        Directory.CreateDirectory(planRunFolder);
        var events = new List<EngineEvent>();
        var planIndexPath = Path.Combine(planRunFolder, "index.jsonl");
        var children = new List<string>();
        var status = ResultStatus.Passed;

        foreach (var suiteRef in plan.Manifest.Suites)
        {
            var suiteIdentity = Identity.Parse(suiteRef);
            var suite = discovery.TestSuites.Single(s => s.Manifest.Identity == suiteIdentity);
            var suiteSummary = await RunSuiteManifestAsync(context, discovery, suite, plan, planIndexPath, cancellationToken).ConfigureAwait(false);
            children.AddRange(suiteSummary.RunIds);
            status = CombineStatus(status, suiteSummary.Status);
        }

        WriteGroupArtifacts(planRunFolder, plan.Manifest, plan.Manifest.Environment?.Env ?? new Dictionary<string, string>(), context.RunRequest, children, status, events);
        return new EngineRunSummary(status, children);
    }

    private async Task<EngineRunSummary> RunSuiteManifestAsync(EngineRunContext context, DiscoveryResult discovery, DiscoveredTestSuite suite, DiscoveredTestPlan? plan, string? planIndexPath, CancellationToken cancellationToken)
    {
        var groupRunId = context.GroupRunIdFactory.NewRunId();
        var suiteRunFolder = Path.Combine(context.RunsRoot, groupRunId);
        Directory.CreateDirectory(suiteRunFolder);
        var indexPath = Path.Combine(suiteRunFolder, "index.jsonl");
        var events = new List<EngineEvent>();

        if (suite.Manifest.Controls.HasValue && suite.Manifest.Controls.Value.TryGetProperty("maxParallel", out var maxParallel) && maxParallel.GetInt32() > 1)
        {
            events.Add(new EngineEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Level = "Warning",
                Code = "Controls.MaxParallel.Ignored",
                Message = "maxParallel ignored; execution is sequential.",
                Data = new Dictionary<string, string> { ["value"] = maxParallel.GetInt32().ToString() }
            });
        }

        if (context.RunRequest.NodeOverrides is not null)
        {
            var nodeIds = suite.Manifest.TestCases.Select(node => node.NodeId).ToHashSet(StringComparer.Ordinal);
            foreach (var overrideNode in context.RunRequest.NodeOverrides.Keys)
            {
                if (!nodeIds.Contains(overrideNode))
                {
                    throw new ValidationException("RunRequest.NodeOverride.Invalid", new Dictionary<string, object>
                    {
                        ["nodeId"] = overrideNode
                    });
                }
            }
        }

        var children = new List<string>();
        var status = ResultStatus.Passed;
        foreach (var node in suite.Manifest.TestCases)
        {
            var resolved = ResolveSuiteRef(context.DiscoveryRoots.ResolvedTestCaseRoot, node.Ref);
            if (resolved.Error is not null)
            {
                throw new DiscoveryException("Suite.TestCaseRef.Invalid", new Dictionary<string, object>
                {
                    ["entityType"] = "TestSuite",
                    ["suitePath"] = suite.Path,
                    ["ref"] = node.Ref,
                    ["resolvedPath"] = resolved.ResolvedPath,
                    ["expectedRoot"] = context.DiscoveryRoots.ResolvedTestCaseRoot,
                    ["reason"] = resolved.Error
                });
            }

            var testCase = discovery.TestCases.Single(tc => tc.Path.Equals(resolved.ResolvedPath, StringComparison.OrdinalIgnoreCase));
            var suiteEnv = suite.Manifest.Environment?.Env ?? new Dictionary<string, string>();
            var effectiveEnv = ResolveEnvironment(context.RunRequest.EnvironmentOverrides, suiteEnv, plan?.Manifest.Environment?.Env);

            var nodeInputs = new Dictionary<string, InputValue>(StringComparer.Ordinal);

            if (node.Inputs is not null)
            {
                foreach (var input in node.Inputs)
                {
                    nodeInputs[input.Key] = input.Value;
                }
            }

            if (context.RunRequest.NodeOverrides is not null && context.RunRequest.NodeOverrides.TryGetValue(node.NodeId, out var overrideInputs) && overrideInputs.Inputs is not null)
            {
                foreach (var input in overrideInputs.Inputs)
                {
                    nodeInputs[input.Key] = input.Value;
                }
            }

            var resolvedInputs = _inputResolver.Resolve(testCase.Manifest, nodeInputs, effectiveEnv, node.NodeId);
            var scriptPath = Path.Combine(Path.GetDirectoryName(testCase.Path) ?? string.Empty, "run.ps1");

            var caseContext = new CaseRunContext
            {
                RunsRoot = context.RunsRoot,
                PowerShellPath = context.PowerShellPath,
                ScriptPath = scriptPath,
                TestCaseManifest = testCase.Manifest,
                ResolvedRef = resolved.ResolvedPath,
                EffectiveInputs = resolvedInputs.Values,
                EffectiveEnvironment = effectiveEnv,
                SecretInputValues = resolvedInputs.SecretInputs,
                SecretEnvironmentKeys = new HashSet<string>(StringComparer.Ordinal),
                ParameterDefinitions = resolvedInputs.ParameterDefinitions,
                WorkingDir = suite.Manifest.Environment?.WorkingDir,
                NodeId = node.NodeId,
                SuiteId = suite.Manifest.Identity.ToString(),
                PlanId = plan?.Manifest.Identity.ToString(),
                Timeout = testCase.Manifest.TimeoutSec.HasValue ? TimeSpan.FromSeconds(testCase.Manifest.TimeoutSec.Value) : null
            };

            var result = await _runner.RunAsync(caseContext, cancellationToken).ConfigureAwait(false);
            children.Add(result.RunId);
            status = CombineStatus(status, result.Status);
            WriteIndexEntry(indexPath, new IndexEntry
            {
                RunId = result.RunId,
                Status = result.Status.ToString(),
                NodeId = node.NodeId,
                SuiteId = suite.Manifest.Identity.ToString(),
                PlanId = plan?.Manifest.Identity.ToString()
            });

            if (!string.IsNullOrEmpty(planIndexPath))
            {
                WriteIndexEntry(planIndexPath, new IndexEntry
                {
                    RunId = result.RunId,
                    Status = result.Status.ToString(),
                    NodeId = node.NodeId,
                    SuiteId = suite.Manifest.Identity.ToString(),
                    PlanId = plan?.Manifest.Identity.ToString()
                });
            }
        }

        WriteGroupArtifacts(suiteRunFolder, suite.Manifest, suite.Manifest.Environment?.Env ?? new Dictionary<string, string>(), context.RunRequest, children, status, events);
        return new EngineRunSummary(status, children);
    }

    private static Dictionary<string, string> ResolveEnvironment(EnvironmentOverrides? overrides, Dictionary<string, string>? suiteEnv, Dictionary<string, string>? planEnv)
    {
        var effective = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                effective[key] = value;
            }
        }

        if (suiteEnv is not null)
        {
            foreach (var kvp in suiteEnv)
            {
                effective[kvp.Key] = kvp.Value;
            }
        }

        if (planEnv is not null)
        {
            foreach (var kvp in planEnv)
            {
                effective[kvp.Key] = kvp.Value;
            }
        }

        if (overrides?.Env is not null)
        {
            foreach (var kvp in overrides.Env)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    throw new ValidationException("Environment.Invalid", new Dictionary<string, object>
                    {
                        ["reason"] = "EmptyKey"
                    });
                }

                effective[kvp.Key] = kvp.Value;
            }
        }

        return effective;
    }

    private static void WriteIndexEntry(string path, IndexEntry entry)
    {
        var json = JsonSerializer.Serialize(entry, JsonParsing.SerializerOptions);
        File.AppendAllText(path, json + Environment.NewLine);
    }

    private static void WriteGroupArtifacts(string folder, object manifest, Dictionary<string, string> environment, RunRequest runRequest, IReadOnlyList<string> children, ResultStatus status, IReadOnlyList<EngineEvent> events)
    {
        JsonParsing.WriteDeterministic(Path.Combine(folder, "manifest.json"), manifest);
        JsonParsing.WriteDeterministic(Path.Combine(folder, "environment.json"), environment);
        JsonParsing.WriteDeterministic(Path.Combine(folder, "runRequest.json"), runRequest);
        JsonParsing.WriteDeterministic(Path.Combine(folder, "children.json"), children);
        JsonParsing.WriteDeterministic(Path.Combine(folder, "result.json"), new GroupResult { Status = status.ToString() });

        using var writer = new StreamWriter(Path.Combine(folder, "events.jsonl"));
        foreach (var evt in events)
        {
            writer.WriteLine(JsonSerializer.Serialize(evt, JsonParsing.SerializerOptions));
        }
    }

    private static ResultStatus CombineStatus(ResultStatus current, ResultStatus next)
    {
        if (next == ResultStatus.Error)
        {
            return ResultStatus.Error;
        }

        if (next == ResultStatus.Timeout && current != ResultStatus.Error)
        {
            return ResultStatus.Timeout;
        }

        if (next == ResultStatus.Failed && current == ResultStatus.Passed)
        {
            return ResultStatus.Failed;
        }

        return current;
    }

    private static SuiteRefResolution ResolveSuiteRef(string caseRoot, string suiteRef)
    {
        var refRoot = Path.Combine(caseRoot, suiteRef);
        var resolvedFolder = PathUtils.ResolveFinalDirectory(refRoot);
        var resolvedManifest = Path.Combine(resolvedFolder, "test.manifest.json");
        var normalizedManifest = PathUtils.NormalizePath(resolvedManifest);

        if (!PathUtils.IsContained(caseRoot, normalizedManifest))
        {
            return new SuiteRefResolution(normalizedManifest, "OutOfRoot");
        }

        if (!Directory.Exists(resolvedFolder))
        {
            return new SuiteRefResolution(normalizedManifest, "NotFound");
        }

        if (!File.Exists(normalizedManifest))
        {
            return new SuiteRefResolution(normalizedManifest, "MissingManifest");
        }

        return new SuiteRefResolution(normalizedManifest, null);
    }

    private sealed record SuiteRefResolution(string ResolvedPath, string? Error);
}

public sealed record EngineRunContext
{
    public required DiscoveryRoots DiscoveryRoots { get; init; }

    public required RunRequest RunRequest { get; init; }

    public required string RunsRoot { get; init; }

    public required string PowerShellPath { get; init; }

    public required IRunIdGenerator GroupRunIdFactory { get; init; }
}

public sealed record EngineRunSummary(ResultStatus Status, IReadOnlyList<string> RunIds);

public sealed record IndexEntry
{
    public required string RunId { get; init; }

    public required string Status { get; init; }

    public string? NodeId { get; init; }

    public string? SuiteId { get; init; }

    public string? PlanId { get; init; }
}

public sealed record GroupResult
{
    public required string Status { get; init; }
}
