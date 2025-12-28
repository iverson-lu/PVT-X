using System.Text.Json;
using PcTest.Contracts;
using PcTest.Runner;

namespace PcTest.Engine;

public sealed class EngineService
{
    private readonly DiscoveryService _discovery;
    private readonly IRunner _runner;

    public EngineService(DiscoveryService discovery, IRunner runner)
    {
        _discovery = discovery;
        _runner = runner;
    }

    public DiscoveryResult Discover(ResolvedRoots roots) => _discovery.Discover(roots.TestCaseRoot, roots.TestSuiteRoot, roots.TestPlanRoot);

    public async Task RunAsync(RunRequest request, ResolvedRoots roots, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(roots.RunsRoot);
        DiscoveryResult discovery = Discover(roots);
        ValidateRunRequest(request);

        if (request.TestCase is not null)
        {
            Identity identity = Identity.Parse(request.TestCase);
            if (!discovery.TestCases.TryGetValue(identity, out var testCase))
            {
                throw new PcTestException("RunRequest.Target.NotFound", $"TestCase {identity} not found.");
            }

            await RunStandaloneTestCaseAsync(testCase, request, roots, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.Suite is not null)
        {
            Identity identity = Identity.Parse(request.Suite);
            if (!discovery.TestSuites.TryGetValue(identity, out var suite))
            {
                throw new PcTestException("RunRequest.Target.NotFound", $"Suite {identity} not found.");
            }

            await RunSuiteAsync(suite, request, roots, discovery, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.Plan is not null)
        {
            Identity identity = Identity.Parse(request.Plan);
            if (!discovery.TestPlans.TryGetValue(identity, out var plan))
            {
                throw new PcTestException("RunRequest.Target.NotFound", $"Plan {identity} not found.");
            }

            await RunPlanAsync(plan, request, roots, discovery, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidateRunRequest(RunRequest request)
    {
        int targets = 0;
        if (request.TestCase is not null) targets++;
        if (request.Suite is not null) targets++;
        if (request.Plan is not null) targets++;
        if (targets != 1)
        {
            throw new PcTestException("RunRequest.Invalid", "RunRequest must specify exactly one target.");
        }

        if (request.Plan is not null)
        {
            if (request.NodeOverrides is not null || request.CaseInputs is not null)
            {
                throw new PcTestException("RunRequest.Invalid", "Plan RunRequest cannot include nodeOverrides or caseInputs.");
            }
        }

        if (request.TestCase is not null && request.NodeOverrides is not null)
        {
            throw new PcTestException("RunRequest.Invalid", "Standalone TestCase cannot include nodeOverrides.");
        }
    }

    private async Task RunStandaloneTestCaseAsync(TestCaseEntry testCase, RunRequest request, ResolvedRoots roots, CancellationToken cancellationToken)
    {
        var effectiveEnvironment = EnvironmentResolver.BuildEffectiveEnvironment(null, null, request.EnvironmentOverrides?.Env);
        var inputs = InputResolver.ResolveInputs(testCase.Manifest, null, request.CaseInputs, effectiveEnvironment);
        string resolvedRef = Path.GetDirectoryName(testCase.ManifestPath) ?? testCase.ManifestPath;

        var manifestSnapshot = new CaseRunManifestSnapshot(
            testCase.Manifest,
            resolvedRef,
            testCase.Identity,
            effectiveEnvironment,
            inputs.RedactedInputs,
            inputs.InputTemplates,
            "Engine/1.0.0");

        var runnerRequest = new RunnerRequest(
            testCase.Manifest,
            testCase.ManifestPath,
            inputs.EffectiveInputs,
            inputs.RedactedInputs,
            inputs.SecretInputs,
            effectiveEnvironment,
            request.EnvironmentOverrides?.Env,
            null,
            null,
            null,
            manifestSnapshot,
            roots.RunsRoot,
            null,
            null);

        RunnerResult runnerResult = await _runner.RunTestCaseAsync(runnerRequest, cancellationToken).ConfigureAwait(false);
        JsonUtilities.AppendJsonLine(Path.Combine(roots.RunsRoot, "index.jsonl"), new IndexEntry
        {
            RunId = runnerResult.RunId,
            RunType = "TestCase",
            TestId = testCase.Identity.Id,
            TestVersion = testCase.Identity.Version,
            StartTime = runnerResult.StartTime,
            EndTime = runnerResult.EndTime,
            Status = runnerResult.Status
        });
    }

    private async Task RunPlanAsync(
        TestPlanEntry plan,
        RunRequest request,
        ResolvedRoots roots,
        DiscoveryResult discovery,
        CancellationToken cancellationToken)
    {
        string planRunId = RunIdFactory.CreateGroupRunId(roots.RunsRoot);
        string planRunFolder = Path.Combine(roots.RunsRoot, planRunId);
        Directory.CreateDirectory(planRunFolder);

        JsonUtilities.WriteJsonFile(Path.Combine(planRunFolder, "manifest.json"), plan.Manifest);
        JsonUtilities.WriteJsonFile(Path.Combine(planRunFolder, "environment.json"), plan.Environment ?? new PlanEnvironment());
        JsonUtilities.WriteJsonFile(Path.Combine(planRunFolder, "runRequest.json"), request);

        var planChildren = new List<ChildRunEntry>();
        var suiteRunIds = new List<string>();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        foreach (var suiteRef in plan.Manifest.Suites)
        {
            Identity suiteId = Identity.Parse(suiteRef);
            if (!discovery.TestSuites.TryGetValue(suiteId, out var suiteEntry))
            {
                throw new PcTestException("Plan.Suite.NotFound", $"Suite {suiteRef} not found.");
            }

            var suiteResult = await RunSuiteAsync(
                suiteEntry,
                new RunRequest { Suite = suiteRef, EnvironmentOverrides = request.EnvironmentOverrides },
                roots,
                discovery,
                new PlanContext(plan, planRunId),
                cancellationToken).ConfigureAwait(false);

            suiteRunIds.Add(suiteResult.RunId);
            planChildren.Add(new ChildRunEntry(suiteResult.RunId, suiteEntry.Identity.Id, suiteEntry.Identity.Version, suiteResult.Status));
        }

        string childrenPath = Path.Combine(planRunFolder, "children.jsonl");
        foreach (var child in planChildren)
        {
            JsonUtilities.AppendJsonLine(childrenPath, child);
        }

        DateTimeOffset end = DateTimeOffset.UtcNow;
        string status = StatusAggregator.Aggregate(planChildren.Select(c => c.Status));
        var summary = new SummaryResult
        {
            SchemaVersion = "1.5.0",
            RunType = "TestPlan",
            PlanId = plan.Identity.Id,
            PlanVersion = plan.Identity.Version,
            Status = status,
            StartTime = start.ToString("O"),
            EndTime = end.ToString("O"),
            ChildRunIds = suiteRunIds
        };

        JsonUtilities.WriteJsonFile(Path.Combine(planRunFolder, "result.json"), summary);
        JsonUtilities.AppendJsonLine(Path.Combine(roots.RunsRoot, "index.jsonl"), new IndexEntry
        {
            RunId = planRunId,
            RunType = "TestPlan",
            PlanId = plan.Identity.Id,
            PlanVersion = plan.Identity.Version,
            StartTime = summary.StartTime,
            EndTime = summary.EndTime,
            Status = summary.Status
        });
    }

    private async Task<SuiteRunResult> RunSuiteAsync(
        TestSuiteEntry suite,
        RunRequest request,
        ResolvedRoots roots,
        DiscoveryResult discovery,
        PlanContext? planContext,
        CancellationToken cancellationToken)
    {
        string suiteRunId = RunIdFactory.CreateGroupRunId(roots.RunsRoot);
        string suiteRunFolder = Path.Combine(roots.RunsRoot, suiteRunId);
        Directory.CreateDirectory(suiteRunFolder);

        JsonUtilities.WriteJsonFile(Path.Combine(suiteRunFolder, "manifest.json"), suite.Manifest);
        JsonUtilities.WriteJsonFile(Path.Combine(suiteRunFolder, "controls.json"), suite.Manifest.Controls ?? new object());
        JsonUtilities.WriteJsonFile(Path.Combine(suiteRunFolder, "environment.json"), suite.Manifest.Environment ?? new SuiteEnvironment());
        JsonUtilities.WriteJsonFile(Path.Combine(suiteRunFolder, "runRequest.json"), request);

        SuiteControls controls = SuiteControls.FromJson(suite.Manifest.Controls);
        if (controls.MaxParallel > 1)
        {
            var warning = new EngineEvent("Controls.MaxParallel.Ignored", "suite.manifest.json", $"maxParallel {controls.MaxParallel} ignored.");
            JsonUtilities.AppendJsonLine(Path.Combine(suiteRunFolder, "events.jsonl"), warning);
        }

        var children = new List<ChildRunEntry>();
        var childRunIds = new List<string>();
        DateTimeOffset start = DateTimeOffset.UtcNow;

        for (int iteration = 0; iteration < Math.Max(1, controls.Repeat); iteration++)
        {
            foreach (var node in suite.Manifest.TestCases)
            {
                if (request.NodeOverrides is not null && !request.NodeOverrides.ContainsKey(node.NodeId))
                {
                    // allow missing, but validate unknown below
                }

                if (request.NodeOverrides is not null)
                {
                    foreach (var overrideKey in request.NodeOverrides.Keys)
                    {
                        if (!suite.Manifest.TestCases.Any(n => string.Equals(n.NodeId, overrideKey, StringComparison.OrdinalIgnoreCase)))
                        {
                            throw new PcTestException("RunRequest.Invalid", $"Unknown nodeId {overrideKey}.");
                        }
                    }
                }

                var resolved = SuiteRefResolver.ResolveSuiteTestCaseRef(roots.TestCaseRoot, suite.ManifestPath, node.Ref);
                if (!discovery.TestCases.Values.Any(tc => string.Equals(tc.ManifestPath, resolved.ManifestPath, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new PcTestException("Suite.TestCaseRef.Invalid", "Missing manifest.");
                }

                TestCaseEntry testCase = discovery.TestCases.Values.First(tc => string.Equals(tc.ManifestPath, resolved.ManifestPath, StringComparison.OrdinalIgnoreCase));

                var effectiveEnvironment = EnvironmentResolver.BuildEffectiveEnvironment(
                    planContext?.Plan.Manifest.Environment?.Env,
                    suite.Manifest.Environment?.Env,
                    request.EnvironmentOverrides?.Env);

                var inputs = InputResolver.ResolveInputs(
                    testCase.Manifest,
                    node.Inputs,
                    request.NodeOverrides?.GetValueOrDefault(node.NodeId)?.Inputs,
                    effectiveEnvironment);

                var manifestSnapshot = new CaseRunManifestSnapshot(
                    testCase.Manifest,
                    resolved.ResolvedPath,
                    testCase.Identity,
                    effectiveEnvironment,
                    inputs.RedactedInputs,
                    inputs.InputTemplates,
                    "Engine/1.0.0");

                var runnerRequest = new RunnerRequest(
                    testCase.Manifest,
                    resolved.ManifestPath,
                    inputs.EffectiveInputs,
                    inputs.RedactedInputs,
                    inputs.SecretInputs,
                    effectiveEnvironment,
                    request.EnvironmentOverrides?.Env,
                    node.NodeId,
                    suite.Identity,
                    planContext?.Plan.Identity,
                    manifestSnapshot,
                    roots.RunsRoot,
                    suiteRunId,
                    suite.Manifest.Environment?.WorkingDir);

                RunnerResult runnerResult = await _runner.RunTestCaseAsync(runnerRequest, cancellationToken).ConfigureAwait(false);

                children.Add(new ChildRunEntry(runnerResult.RunId, node.NodeId, testCase.Identity.Id, testCase.Identity.Version, runnerResult.Status));
                childRunIds.Add(runnerResult.RunId);

                JsonUtilities.AppendJsonLine(Path.Combine(suiteRunFolder, "children.jsonl"), new
                {
                    runId = runnerResult.RunId,
                    nodeId = node.NodeId,
                    testId = testCase.Identity.Id,
                    testVersion = testCase.Identity.Version,
                    status = runnerResult.Status
                });

                JsonUtilities.AppendJsonLine(Path.Combine(roots.RunsRoot, "index.jsonl"), IndexEntry.ForTestCase(runnerResult, testCase.Identity, suite.Identity, planContext?.Plan.Identity));

                if (!controls.ContinueOnFailure && runnerResult.Status != "Passed")
                {
                    goto SuiteEnd;
                }
            }
        }

        SuiteEnd:
        DateTimeOffset end = DateTimeOffset.UtcNow;
        string suiteStatus = StatusAggregator.Aggregate(children.Select(c => c.Status));
        var summary = new SummaryResult
        {
            SchemaVersion = "1.5.0",
            RunType = "TestSuite",
            SuiteId = suite.Identity.Id,
            SuiteVersion = suite.Identity.Version,
            PlanId = planContext?.Plan.Identity.Id,
            PlanVersion = planContext?.Plan.Identity.Version,
            Status = suiteStatus,
            StartTime = start.ToString("O"),
            EndTime = end.ToString("O"),
            ChildRunIds = childRunIds
        };

        JsonUtilities.WriteJsonFile(Path.Combine(suiteRunFolder, "result.json"), summary);
        JsonUtilities.AppendJsonLine(Path.Combine(roots.RunsRoot, "index.jsonl"), new IndexEntry
        {
            RunId = suiteRunId,
            RunType = "TestSuite",
            SuiteId = suite.Identity.Id,
            SuiteVersion = suite.Identity.Version,
            PlanId = planContext?.Plan.Identity.Id,
            PlanVersion = planContext?.Plan.Identity.Version,
            StartTime = summary.StartTime,
            EndTime = summary.EndTime,
            Status = summary.Status,
            ParentRunId = planContext?.RunId
        });

        return new SuiteRunResult(suiteRunId, suiteStatus);
    }
}

public sealed record SuiteRunResult(string RunId, string Status);

public sealed record PlanContext(TestPlanEntry Plan, string RunId);

public sealed record ChildRunEntry(string RunId, string? NodeId, string? TestId, string? TestVersion, string Status);

public sealed record EngineEvent(string Code, string Location, string Message);

public static class StatusAggregator
{
    public static string Aggregate(IEnumerable<string> statuses)
    {
        var ordered = new[] { "Error", "Timeout", "Aborted", "Failed", "Passed" };
        foreach (var status in ordered)
        {
            if (statuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            {
                return status;
            }
        }

        return "Passed";
    }
}

public sealed class IndexEntry
{
    public string RunId { get; init; } = string.Empty;
    public string RunType { get; init; } = string.Empty;
    public string? NodeId { get; init; }
    public string? TestId { get; init; }
    public string? TestVersion { get; init; }
    public string? SuiteId { get; init; }
    public string? SuiteVersion { get; init; }
    public string? PlanId { get; init; }
    public string? PlanVersion { get; init; }
    public string? ParentRunId { get; init; }
    public string StartTime { get; init; } = string.Empty;
    public string EndTime { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;

    public static IndexEntry ForTestCase(RunnerResult result, Identity testCase, Identity suite, Identity? plan)
    {
        return new IndexEntry
        {
            RunId = result.RunId,
            RunType = "TestCase",
            NodeId = result.NodeId,
            TestId = testCase.Id,
            TestVersion = testCase.Version,
            SuiteId = suite.Id,
            SuiteVersion = suite.Version,
            PlanId = plan?.Id,
            PlanVersion = plan?.Version,
            ParentRunId = result.ParentRunId,
            StartTime = result.StartTime,
            EndTime = result.EndTime,
            Status = result.Status
        };
    }
}
