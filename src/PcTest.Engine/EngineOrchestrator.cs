using PcTest.Contracts;
using PcTest.Runner;
using System.Text.Json;

namespace PcTest.Engine;

public sealed class EngineOptions
{
    public required string TestCaseRoot { get; init; }
    public required string TestSuiteRoot { get; init; }
    public required string TestPlanRoot { get; init; }
    public required string RunsRoot { get; init; }
}

public sealed class EngineOrchestrator
{
    private readonly DiscoveryService _discovery;
    private readonly TestCaseRunner _runner;

    public EngineOrchestrator(DiscoveryService? discovery = null, TestCaseRunner? runner = null)
    {
        _discovery = discovery ?? new DiscoveryService();
        _runner = runner ?? new TestCaseRunner();
    }

    public DiscoveryResult Discover(EngineOptions options)
        => _discovery.Discover(options.TestCaseRoot, options.TestSuiteRoot, options.TestPlanRoot);

    public RunnerResult RunTestCase(EngineOptions options, RunRequest runRequest)
    {
        ValidateRunRequest(runRequest, RunRequestType.TestCase);
        var discovery = Discover(options);
        var identity = IdentityParser.Parse(runRequest.TestCase ?? throw new PcTestException("RunRequest.Invalid", "RunRequest.testCase required."));
        var manifestPath = IdentityResolver.ResolvePath(discovery.TestCases, identity, "TestCase");
        var testCaseManifest = JsonUtilities.ReadFile<TestCaseManifest>(manifestPath);

        var effectiveEnvironment = EnvironmentResolver.Resolve(null, runRequest.EnvironmentOverrides);
        var (effectiveInputs, inputTemplates, secretInputs) = InputResolver.ResolveInputs(testCaseManifest, null, runRequest.CaseInputs, effectiveEnvironment, null);

        var snapshot = new ManifestSnapshot
        {
            SourceManifest = testCaseManifest,
            ResolvedRef = manifestPath,
            ResolvedIdentity = identity,
            EffectiveEnvironment = effectiveEnvironment,
            EffectiveInputs = effectiveInputs,
            InputTemplates = inputTemplates,
            ResolvedAt = DateTimeOffset.UtcNow,
            EngineVersion = "1.0.0"
        };

        var context = new TestCaseRunContext
        {
            RunId = GenerateRunId(),
            RunsRoot = options.RunsRoot,
            TestCaseManifestPath = manifestPath,
            TestCaseManifest = testCaseManifest,
            ManifestSnapshot = snapshot,
            EffectiveInputs = effectiveInputs,
            SecretInputs = secretInputs,
            EffectiveEnvironment = effectiveEnvironment,
            WorkingDir = null
        };

        var result = _runner.Run(context);
        AppendIndex(options.RunsRoot, new IndexEntry
        {
            RunId = result.RunId,
            RunType = "TestCase",
            TestId = testCaseManifest.Id,
            TestVersion = testCaseManifest.Version,
            StartTime = result.StartTime,
            EndTime = result.EndTime,
            Status = result.Status
        });

        return result;
    }

    public SuiteRunResult RunSuite(EngineOptions options, RunRequest runRequest, Identity? planIdentity = null, string? planRunId = null, EnvironmentDefinition? planEnvironment = null)
    {
        ValidateRunRequest(runRequest, RunRequestType.Suite);
        var discovery = Discover(options);
        var suiteIdentity = IdentityParser.Parse(runRequest.Suite ?? throw new PcTestException("RunRequest.Invalid", "RunRequest.suite required."));
        var suitePath = IdentityResolver.ResolvePath(discovery.TestSuites, suiteIdentity, "TestSuite");
        var suiteManifest = JsonUtilities.ReadFile<TestSuiteManifest>(suitePath);
        if (runRequest.NodeOverrides is not null)
        {
            var nodeIds = suiteManifest.TestCases.Select(node => node.NodeId).ToHashSet(StringComparer.Ordinal);
            foreach (var key in runRequest.NodeOverrides.Keys)
            {
                if (!nodeIds.Contains(key))
                {
                    throw new PcTestException("RunRequest.Invalid", $"Unknown nodeId override {key}.");
                }
            }
        }

        var groupRunId = GenerateRunId();
        var groupFolder = EnsureGroupFolder(options.RunsRoot, groupRunId);
        JsonUtilities.WriteFile(Path.Combine(groupFolder, "manifest.json"), suiteManifest);
        if (suiteManifest.Controls.HasValue)
        {
            JsonUtilities.WriteFile(Path.Combine(groupFolder, "controls.json"), suiteManifest.Controls.Value);
        }

        if (suiteManifest.Environment is not null)
        {
            JsonUtilities.WriteFile(Path.Combine(groupFolder, "environment.json"), suiteManifest.Environment);
        }

        if (runRequest is not null)
        {
            JsonUtilities.WriteFile(Path.Combine(groupFolder, "runRequest.json"), runRequest);
        }

        var childrenPath = Path.Combine(groupFolder, "children.jsonl");
        var childRunIds = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var suiteStart = DateTimeOffset.UtcNow;
        var suiteStatus = RunStatus.Passed;
        var effectiveEnvironment = ResolveEnvironment(suiteManifest.Environment, planEnvironment, runRequest.EnvironmentOverrides);

        foreach (var node in suiteManifest.TestCases)
        {
            var resolvedRef = SuiteRefResolver.ResolveTestCaseRef(suitePath, options.TestCaseRoot, node.Ref);
            var testCaseManifest = JsonUtilities.ReadFile<TestCaseManifest>(resolvedRef);
            var nodeOverrides = runRequest.NodeOverrides != null && runRequest.NodeOverrides.TryGetValue(node.NodeId, out var overrideValue)
                ? overrideValue.Inputs
                : null;

            var (effectiveInputs, inputTemplates, secretInputs) = InputResolver.ResolveInputs(testCaseManifest, node.Inputs, nodeOverrides, effectiveEnvironment, node.NodeId);
            var snapshot = new ManifestSnapshot
            {
                SourceManifest = testCaseManifest,
                ResolvedRef = resolvedRef,
                ResolvedIdentity = new Identity(testCaseManifest.Id, testCaseManifest.Version),
                EffectiveEnvironment = effectiveEnvironment,
                EffectiveInputs = effectiveInputs,
                InputTemplates = inputTemplates,
                ResolvedAt = DateTimeOffset.UtcNow,
                EngineVersion = "1.0.0"
            };

            var context = new TestCaseRunContext
            {
                RunId = GenerateRunId(),
                RunsRoot = options.RunsRoot,
                TestCaseManifestPath = resolvedRef,
                TestCaseManifest = testCaseManifest,
                ManifestSnapshot = snapshot,
                EffectiveInputs = effectiveInputs,
                SecretInputs = secretInputs,
                EffectiveEnvironment = effectiveEnvironment,
                NodeId = node.NodeId,
                SuiteId = suiteManifest.Id,
                SuiteVersion = suiteManifest.Version,
                PlanId = planIdentity?.Id,
                PlanVersion = planIdentity?.Version,
                WorkingDir = suiteManifest.Environment?.WorkingDir
            };

            var runnerResult = _runner.Run(context);
            childRunIds.Add(runnerResult.RunId);
            UpdateCounts(counts, runnerResult.Status);
            suiteStatus = AggregateStatus(suiteStatus, runnerResult.Status);

            JsonUtilities.AppendJsonLine(childrenPath, new
            {
                runId = runnerResult.RunId,
                nodeId = node.NodeId,
                testId = testCaseManifest.Id,
                testVersion = testCaseManifest.Version,
                status = runnerResult.Status
            });

            AppendIndex(options.RunsRoot, new IndexEntry
            {
                RunId = runnerResult.RunId,
                RunType = "TestCase",
                NodeId = node.NodeId,
                TestId = testCaseManifest.Id,
                TestVersion = testCaseManifest.Version,
                SuiteId = suiteManifest.Id,
                SuiteVersion = suiteManifest.Version,
                PlanId = planIdentity?.Id,
                PlanVersion = planIdentity?.Version,
                ParentRunId = groupRunId,
                StartTime = runnerResult.StartTime,
                EndTime = runnerResult.EndTime,
                Status = runnerResult.Status
            });
        }

        var suiteEnd = DateTimeOffset.UtcNow;
        var summary = new SummaryResult
        {
            SchemaVersion = "1.5.0",
            RunType = "TestSuite",
            SuiteId = suiteManifest.Id,
            SuiteVersion = suiteManifest.Version,
            PlanId = planIdentity?.Id,
            PlanVersion = planIdentity?.Version,
            Status = suiteStatus,
            StartTime = suiteStart,
            EndTime = suiteEnd,
            Counts = counts,
            ChildRunIds = childRunIds.ToArray()
        };

        JsonUtilities.WriteFile(Path.Combine(groupFolder, "result.json"), summary);
        AppendIndex(options.RunsRoot, new IndexEntry
        {
            RunId = groupRunId,
            RunType = "TestSuite",
            SuiteId = suiteManifest.Id,
            SuiteVersion = suiteManifest.Version,
            PlanId = planIdentity?.Id,
            PlanVersion = planIdentity?.Version,
            ParentRunId = planRunId,
            StartTime = suiteStart,
            EndTime = suiteEnd,
            Status = suiteStatus
        });

        return new SuiteRunResult(summary, groupRunId);
    }

    public SummaryResult RunPlan(EngineOptions options, RunRequest runRequest)
    {
        ValidateRunRequest(runRequest, RunRequestType.Plan);
        var discovery = Discover(options);
        var planIdentity = IdentityParser.Parse(runRequest.Plan ?? throw new PcTestException("RunRequest.Invalid", "RunRequest.plan required."));
        var planPath = IdentityResolver.ResolvePath(discovery.TestPlans, planIdentity, "TestPlan");
        var planManifest = JsonUtilities.ReadFile<TestPlanManifest>(planPath);

        var groupRunId = GenerateRunId();
        var groupFolder = EnsureGroupFolder(options.RunsRoot, groupRunId);
        JsonUtilities.WriteFile(Path.Combine(groupFolder, "manifest.json"), planManifest);
        if (planManifest.Environment is not null)
        {
            JsonUtilities.WriteFile(Path.Combine(groupFolder, "environment.json"), planManifest.Environment);
        }

        JsonUtilities.WriteFile(Path.Combine(groupFolder, "runRequest.json"), runRequest);

        var childrenPath = Path.Combine(groupFolder, "children.jsonl");
        var childRunIds = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var planStart = DateTimeOffset.UtcNow;
        var planStatus = RunStatus.Passed;

        foreach (var suiteRef in planManifest.Suites)
        {
            var suiteIdentity = IdentityParser.Parse(suiteRef);
            var suitePath = IdentityResolver.ResolvePath(discovery.TestSuites, suiteIdentity, "TestSuite");
            var suiteRunRequest = new RunRequest
            {
                Suite = suiteRef,
                EnvironmentOverrides = runRequest.EnvironmentOverrides
            };

            var suiteRun = RunSuite(options, suiteRunRequest, planIdentity, groupRunId, planManifest.Environment);
            childRunIds.Add(suiteRun.GroupRunId);
            UpdateCounts(counts, suiteRun.Summary.Status);
            planStatus = AggregateStatus(planStatus, suiteRun.Summary.Status);

            JsonUtilities.AppendJsonLine(childrenPath, new
            {
                runId = suiteRun.GroupRunId,
                suiteId = suiteRun.Summary.SuiteId,
                suiteVersion = suiteRun.Summary.SuiteVersion,
                status = suiteRun.Summary.Status
            });
        }

        var planEnd = DateTimeOffset.UtcNow;
        var summary = new SummaryResult
        {
            SchemaVersion = "1.5.0",
            RunType = "TestPlan",
            PlanId = planManifest.Id,
            PlanVersion = planManifest.Version,
            Status = planStatus,
            StartTime = planStart,
            EndTime = planEnd,
            Counts = counts,
            ChildRunIds = childRunIds.ToArray()
        };

        JsonUtilities.WriteFile(Path.Combine(groupFolder, "result.json"), summary);
        AppendIndex(options.RunsRoot, new IndexEntry
        {
            RunId = groupRunId,
            RunType = "TestPlan",
            PlanId = planManifest.Id,
            PlanVersion = planManifest.Version,
            StartTime = planStart,
            EndTime = planEnd,
            Status = planStatus
        });

        return summary;
    }

    private static RunStatus AggregateStatus(RunStatus current, RunStatus next)
    {
        if (current == RunStatus.Aborted || next == RunStatus.Aborted)
        {
            return RunStatus.Aborted;
        }

        if (current == RunStatus.Error || next == RunStatus.Error)
        {
            return RunStatus.Error;
        }

        if (current == RunStatus.Timeout || next == RunStatus.Timeout)
        {
            return RunStatus.Timeout;
        }

        if (current == RunStatus.Failed || next == RunStatus.Failed)
        {
            return RunStatus.Failed;
        }

        return RunStatus.Passed;
    }

    private static void UpdateCounts(Dictionary<string, int> counts, RunStatus status)
    {
        var key = status.ToString();
        counts[key] = counts.TryGetValue(key, out var value) ? value + 1 : 1;
    }

    private static void ValidateRunRequest(RunRequest runRequest, RunRequestType type)
    {
        var specified = new[] { runRequest.Suite, runRequest.TestCase, runRequest.Plan }.Count(v => !string.IsNullOrWhiteSpace(v));
        if (specified != 1)
        {
            throw new PcTestException("RunRequest.Invalid", "RunRequest must specify exactly one target.");
        }

        if (type == RunRequestType.Plan)
        {
            if (runRequest.NodeOverrides is not null || runRequest.CaseInputs is not null)
            {
                throw new PcTestException("RunRequest.Invalid", "Plan RunRequest cannot contain inputs or nodeOverrides.");
            }
        }

        if (type == RunRequestType.Suite && runRequest.NodeOverrides is not null && runRequest.NodeOverrides.Keys.Any(string.IsNullOrWhiteSpace))
        {
            throw new PcTestException("RunRequest.Invalid", "RunRequest nodeOverrides keys cannot be empty.");
        }
    }

    private static string GenerateRunId() => $"R-{Guid.NewGuid():N}";

    private static string EnsureGroupFolder(string runsRoot, string runId)
    {
        Directory.CreateDirectory(runsRoot);
        var folder = Path.Combine(runsRoot, runId);
        var attempt = 0;
        while (Directory.Exists(folder))
        {
            attempt++;
            folder = Path.Combine(runsRoot, $"{runId}-{attempt}");
        }

        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void AppendIndex(string runsRoot, IndexEntry entry)
    {
        var indexPath = Path.Combine(runsRoot, "index.jsonl");
        Directory.CreateDirectory(runsRoot);
        using var stream = new FileStream(indexPath, FileMode.Append, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, JsonUtilities.Utf8NoBom);
        var json = JsonSerializer.Serialize(entry, JsonUtilities.SerializerOptions);
        writer.WriteLine(json);
    }

    private static Dictionary<string, string> ResolveEnvironment(EnvironmentDefinition? suiteEnvironment, EnvironmentDefinition? planEnvironment, EnvironmentOverrides? overrides)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                resolved[key] = value;
            }
        }

        ApplyEnv(resolved, suiteEnvironment?.Env);
        ApplyEnv(resolved, planEnvironment?.Env);
        ApplyEnv(resolved, overrides?.Env);
        return resolved;
    }

    private static void ApplyEnv(Dictionary<string, string> target, Dictionary<string, string>? env)
    {
        if (env is null)
        {
            return;
        }

        foreach (var (key, value) in env)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new PcTestException("Environment.InvalidKey", "Environment key cannot be empty.");
            }

            target[key] = value;
        }
    }
}

internal enum RunRequestType
{
    TestCase,
    Suite,
    Plan
}

public sealed record SuiteRunResult(SummaryResult Summary, string GroupRunId);
