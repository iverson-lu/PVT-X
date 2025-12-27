using System.Text.Json;
using PcTest.Contracts;
using PcTest.Runner;

namespace PcTest.Engine;

public sealed class EngineService
{
    private readonly DiscoveryService _discoveryService;
    private readonly SuiteRefResolver _suiteRefResolver;
    private readonly EnvironmentResolver _environmentResolver;
    private readonly InputResolver _inputResolver;
    private readonly TestCaseRunner _runner;
    private readonly IndexWriter _indexWriter;

    public EngineService(TestCaseRunner runner)
    {
        _discoveryService = new DiscoveryService();
        _suiteRefResolver = new SuiteRefResolver();
        _environmentResolver = new EnvironmentResolver();
        _inputResolver = new InputResolver();
        _runner = runner;
        _indexWriter = new IndexWriter();
    }

    public DiscoveryResult Discover(EngineRoots roots) => _discoveryService.Discover(roots);

    public async Task<string> RunAsync(EngineRoots roots, RunRequest runRequest, CancellationToken cancellationToken)
    {
        ValidateRunRequest(runRequest);
        var discovery = Discover(roots);

        if (runRequest.TestCase != null)
        {
            var identity = IdentityParser.Parse(runRequest.TestCase);
            var testCase = ResolveTestCase(discovery, identity);
            var result = await RunStandaloneTestCase(roots, testCase, runRequest, cancellationToken);
            await AppendTestCaseIndex(roots, result, null, null, null, null, null);
            return result.RunId;
        }

        if (runRequest.Suite != null)
        {
            var identity = IdentityParser.Parse(runRequest.Suite);
            var suite = ResolveSuite(discovery, identity);
            var result = await RunSuite(roots, suite, runRequest, null, null, cancellationToken);
            return result;
        }

        var planIdentity = IdentityParser.Parse(runRequest.Plan!);
        var plan = ResolvePlan(discovery, planIdentity);
        var planResult = await RunPlan(roots, plan, discovery, runRequest, cancellationToken);
        return planResult;
    }

    private static void ValidateRunRequest(RunRequest request)
    {
        var count = (request.Suite != null ? 1 : 0) + (request.TestCase != null ? 1 : 0) + (request.Plan != null ? 1 : 0);
        if (count != 1)
        {
            throw new EngineException("RunRequest.Invalid", "RunRequest must specify exactly one of suite, testCase, or plan.", new Dictionary<string, object?>());
        }

        if (request.Plan != null && (request.NodeOverrides != null || request.CaseInputs != null))
        {
            throw new EngineException("RunRequest.Invalid", "Plan RunRequest cannot include nodeOverrides or caseInputs.", new Dictionary<string, object?>());
        }

        if (request.TestCase != null && request.NodeOverrides != null)
        {
            throw new EngineException("RunRequest.Invalid", "Standalone TestCase RunRequest cannot include nodeOverrides.", new Dictionary<string, object?>());
        }
    }

    private static DiscoveredTestCase ResolveTestCase(DiscoveryResult discovery, Identity identity)
    {
        var matches = discovery.TestCases.Where(tc => tc.Identity == identity).ToList();
        if (matches.Count == 0)
        {
            throw new EngineException("Identity.Resolve.NotFound", "TestCase not found.", new Dictionary<string, object?>
            {
                ["entityType"] = "TestCase",
                ["id"] = identity.Id,
                ["version"] = identity.Version,
                ["reason"] = "NotFound"
            });
        }

        if (matches.Count > 1)
        {
            throw new EngineException("Identity.Resolve.NonUnique", "TestCase identity non-unique.", new Dictionary<string, object?>
            {
                ["entityType"] = "TestCase",
                ["id"] = identity.Id,
                ["version"] = identity.Version,
                ["reason"] = "NonUnique",
                ["conflictPaths"] = matches.Select(m => m.ManifestPath).ToArray()
            });
        }

        return matches[0];
    }

    private static DiscoveredSuite ResolveSuite(DiscoveryResult discovery, Identity identity)
    {
        var matches = discovery.Suites.Where(tc => tc.Identity == identity).ToList();
        if (matches.Count == 0)
        {
            throw new EngineException("Identity.Resolve.NotFound", "Suite not found.", new Dictionary<string, object?>
            {
                ["entityType"] = "Suite",
                ["id"] = identity.Id,
                ["version"] = identity.Version,
                ["reason"] = "NotFound"
            });
        }

        if (matches.Count > 1)
        {
            throw new EngineException("Identity.Resolve.NonUnique", "Suite identity non-unique.", new Dictionary<string, object?>
            {
                ["entityType"] = "Suite",
                ["id"] = identity.Id,
                ["version"] = identity.Version,
                ["reason"] = "NonUnique",
                ["conflictPaths"] = matches.Select(m => m.ManifestPath).ToArray()
            });
        }

        return matches[0];
    }

    private static DiscoveredPlan ResolvePlan(DiscoveryResult discovery, Identity identity)
    {
        var matches = discovery.Plans.Where(tc => tc.Identity == identity).ToList();
        if (matches.Count == 0)
        {
            throw new EngineException("Identity.Resolve.NotFound", "Plan not found.", new Dictionary<string, object?>
            {
                ["entityType"] = "Plan",
                ["id"] = identity.Id,
                ["version"] = identity.Version,
                ["reason"] = "NotFound"
            });
        }

        if (matches.Count > 1)
        {
            throw new EngineException("Identity.Resolve.NonUnique", "Plan identity non-unique.", new Dictionary<string, object?>
            {
                ["entityType"] = "Plan",
                ["id"] = identity.Id,
                ["version"] = identity.Version,
                ["reason"] = "NonUnique",
                ["conflictPaths"] = matches.Select(m => m.ManifestPath).ToArray()
            });
        }

        return matches[0];
    }

    private async Task<TestCaseRunResult> RunStandaloneTestCase(EngineRoots roots, DiscoveredTestCase testCase, RunRequest request, CancellationToken cancellationToken)
    {
        var environment = _environmentResolver.ResolveForStandalone(request.EnvironmentOverrides);
        var inputs = _inputResolver.ResolveInputs(testCase.Manifest, request.CaseInputs ?? new Dictionary<string, object?>(), new Dictionary<string, object?>(), environment, null);

        var runRequest = new TestCaseRunRequest
        {
            RunsRoot = roots.ResolvedRunsRoot,
            CaseRoot = Path.GetDirectoryName(testCase.ManifestPath) ?? string.Empty,
            ResolvedRef = Path.GetDirectoryName(testCase.ManifestPath) ?? string.Empty,
            Identity = testCase.Identity,
            Manifest = testCase.Manifest,
            EffectiveInputs = inputs.EffectiveInputs,
            RedactedInputs = inputs.RedactedInputs,
            EffectiveEnvironment = environment,
            SecretInputs = inputs.SecretInputs,
            NodeId = null,
            WorkingDir = null,
            TimeoutSec = testCase.Manifest.TimeoutSec,
            Events = inputs.Events
        };

        return await _runner.ExecuteAsync(runRequest, cancellationToken);
    }

    private async Task<string> RunSuite(EngineRoots roots, DiscoveredSuite suite, RunRequest request, TestPlanManifest? planManifest, string? parentRunId, CancellationToken cancellationToken)
    {
        var groupRunId = GenerateGroupRunId(roots.ResolvedRunsRoot);
        var groupFolder = Path.Combine(roots.ResolvedRunsRoot, groupRunId);
        Directory.CreateDirectory(groupFolder);
        var suiteStart = DateTime.UtcNow;

        var environment = planManifest == null
            ? _environmentResolver.ResolveForSuite(suite.Manifest, request.EnvironmentOverrides)
            : _environmentResolver.ResolveForPlan(planManifest, suite.Manifest, request.EnvironmentOverrides);

        var controlWarnings = new List<RunEvent>();
        if (suite.Manifest.Controls != null && suite.Manifest.Controls.TryGetValue("maxParallel", out var maxParallelObj))
        {
            if (maxParallelObj is JsonElement element && element.ValueKind == JsonValueKind.Number && element.GetInt32() > 1)
            {
                controlWarnings.Add(new RunEvent(DateTime.UtcNow, SchemaConstants.ControlsMaxParallelIgnored, new Dictionary<string, object?>
                {
                    ["value"] = element.GetInt32(),
                    ["location"] = "suite.manifest.json"
                }));
            }
        }

        WriteGroupArtifacts(groupFolder, suite.Manifest, suite.Identity, request, environment, suite.Manifest.Controls, controlWarnings);

        var repeat = ReadControlInt(suite.Manifest.Controls, "repeat", 1);
        var continueOnFailure = ReadControlBool(suite.Manifest.Controls, "continueOnFailure", false);
        var retryOnError = ReadControlInt(suite.Manifest.Controls, "retryOnError", 0);

        var childRunIds = new List<string>();
        var childStatuses = new List<RunStatus>();

        ValidateNodeOverrides(suite.Manifest, request.NodeOverrides);
        EnsureNodeIdUnique(suite.Manifest);

        using var childrenWriter = new StreamWriter(Path.Combine(groupFolder, "children.jsonl"));

        for (var iteration = 0; iteration < repeat; iteration++)
        {
            foreach (var node in suite.Manifest.TestCases)
            {
                var manifestPath = _suiteRefResolver.ResolveTestCaseManifest(suite.ManifestPath, roots.ResolvedTestCaseRoot, node.Ref);
                var manifest = JsonSerializer.Deserialize<TestCaseManifest>(File.ReadAllText(manifestPath), JsonUtilities.SerializerOptions)
                               ?? throw new InvalidOperationException($"Unable to load test manifest {manifestPath}");
                var identity = new Identity(manifest.Id, manifest.Version);
                var baseInputs = node.Inputs ?? new Dictionary<string, object?>();
                var overrideInputs = request.NodeOverrides?.TryGetValue(node.NodeId, out var overrideNode)
                    == true && overrideNode.Inputs != null
                    ? overrideNode.Inputs
                    : new Dictionary<string, object?>();

                var resolvedInputs = _inputResolver.ResolveInputs(manifest, baseInputs, overrideInputs, environment, node.NodeId);
                var runRequest = new TestCaseRunRequest
                {
                    RunsRoot = roots.ResolvedRunsRoot,
                    CaseRoot = Path.GetDirectoryName(manifestPath) ?? string.Empty,
                    ResolvedRef = Path.GetDirectoryName(manifestPath) ?? string.Empty,
                    Identity = identity,
                    Manifest = manifest,
                    EffectiveInputs = resolvedInputs.EffectiveInputs,
                    RedactedInputs = resolvedInputs.RedactedInputs,
                    EffectiveEnvironment = environment,
                    SecretInputs = resolvedInputs.SecretInputs,
                    NodeId = node.NodeId,
                    SuiteId = suite.Identity.Id,
                    SuiteVersion = suite.Identity.Version,
                    PlanId = planManifest?.Id,
                    PlanVersion = planManifest?.Version,
                    WorkingDir = suite.Manifest.Environment?.WorkingDir,
                    TimeoutSec = manifest.TimeoutSec,
                    Events = resolvedInputs.Events.Concat(controlWarnings).ToList()
                };

                var attempts = 0;
                TestCaseRunResult? caseResult = null;
                while (attempts <= retryOnError)
                {
                    caseResult = await _runner.ExecuteAsync(runRequest, cancellationToken);
                    if (caseResult.Status != RunStatus.Error && caseResult.Status != RunStatus.Timeout)
                    {
                        break;
                    }

                    attempts++;
                    if (attempts > retryOnError)
                    {
                        break;
                    }
                }

                if (caseResult == null)
                {
                    continue;
                }

                childRunIds.Add(caseResult.RunId);
                childStatuses.Add(caseResult.Status);

                var childLine = new
                {
                    runId = caseResult.RunId,
                    nodeId = node.NodeId,
                    testId = identity.Id,
                    testVersion = identity.Version,
                    status = caseResult.Status.ToString()
                };
                childrenWriter.WriteLine(JsonSerializer.Serialize(childLine, JsonUtilities.SerializerOptions));

                await AppendTestCaseIndex(roots, caseResult, groupRunId, suite.Identity, planManifest?.Id, planManifest?.Version, node.NodeId);

                if (!continueOnFailure && caseResult.Status != RunStatus.Passed)
                {
                    iteration = repeat;
                    break;
                }
            }
        }

        var suiteStatus = AggregateStatus(childStatuses);
        var suiteEnd = DateTime.UtcNow;
        var suiteResult = new GroupRunResult
        {
            SchemaVersion = SchemaConstants.SchemaVersion,
            RunType = RunType.TestSuite,
            SuiteId = suite.Identity.Id,
            SuiteVersion = suite.Identity.Version,
            PlanId = planManifest?.Id,
            PlanVersion = planManifest?.Version,
            Status = suiteStatus,
            StartTime = suiteStart.ToString("O"),
            EndTime = suiteEnd.ToString("O"),
            Counts = childStatuses.GroupBy(s => s.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            ChildRunIds = childRunIds.ToArray()
        };

        WriteJson(Path.Combine(groupFolder, "result.json"), suiteResult);

        var suiteIndex = new Dictionary<string, object?>
        {
            ["runId"] = groupRunId,
            ["runType"] = RunType.TestSuite.ToString(),
            ["suiteId"] = suite.Identity.Id,
            ["suiteVersion"] = suite.Identity.Version,
            ["startTime"] = suiteResult.StartTime,
            ["endTime"] = suiteResult.EndTime,
            ["status"] = suiteStatus.ToString()
        };
        if (planManifest != null)
        {
            suiteIndex["planId"] = planManifest.Id;
            suiteIndex["planVersion"] = planManifest.Version;
        }
        if (parentRunId != null)
        {
            suiteIndex["parentRunId"] = parentRunId;
        }
        await _indexWriter.AppendAsync(Path.Combine(roots.ResolvedRunsRoot, "index.jsonl"), suiteIndex);

        return groupRunId;
    }

    private async Task<string> RunPlan(EngineRoots roots, DiscoveredPlan plan, DiscoveryResult discovery, RunRequest request, CancellationToken cancellationToken)
    {
        var planRunId = GenerateGroupRunId(roots.ResolvedRunsRoot);
        var groupFolder = Path.Combine(roots.ResolvedRunsRoot, planRunId);
        Directory.CreateDirectory(groupFolder);
        var planStart = DateTime.UtcNow;

        var planEnvironment = _environmentResolver.ResolveForPlanRun(plan.Manifest, request.EnvironmentOverrides);
        WriteGroupArtifacts(groupFolder, plan.Manifest, plan.Identity, request, planEnvironment, null, new List<RunEvent>());

        var childRunIds = new List<string>();
        var childStatuses = new List<RunStatus>();

        using var childrenWriter = new StreamWriter(Path.Combine(groupFolder, "children.jsonl"));

        foreach (var suiteRef in plan.Manifest.Suites)
        {
            var suiteIdentity = IdentityParser.Parse(suiteRef);
            var suite = ResolveSuite(discovery, suiteIdentity);
            var suiteRunId = await RunSuite(roots, suite, request, plan.Manifest, planRunId, cancellationToken);
            childRunIds.Add(suiteRunId);

            var suiteResultPath = Path.Combine(roots.ResolvedRunsRoot, suiteRunId, "result.json");
            var suiteResult = JsonSerializer.Deserialize<GroupRunResult>(File.ReadAllText(suiteResultPath), JsonUtilities.SerializerOptions)
                               ?? throw new InvalidOperationException("Suite result missing.");
            childStatuses.Add(suiteResult.Status);

            childrenWriter.WriteLine(JsonSerializer.Serialize(new
            {
                runId = suiteRunId,
                suiteId = suite.Identity.Id,
                suiteVersion = suite.Identity.Version,
                status = suiteResult.Status.ToString()
            }, JsonUtilities.SerializerOptions));
        }

        var status = AggregateStatus(childStatuses);
        var planEnd = DateTime.UtcNow;
        var planResult = new GroupRunResult
        {
            SchemaVersion = SchemaConstants.SchemaVersion,
            RunType = RunType.TestPlan,
            PlanId = plan.Identity.Id,
            PlanVersion = plan.Identity.Version,
            Status = status,
            StartTime = planStart.ToString("O"),
            EndTime = planEnd.ToString("O"),
            Counts = childStatuses.GroupBy(s => s.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            ChildRunIds = childRunIds.ToArray()
        };

        WriteJson(Path.Combine(groupFolder, "result.json"), planResult);

        await _indexWriter.AppendAsync(Path.Combine(roots.ResolvedRunsRoot, "index.jsonl"), new Dictionary<string, object?>
        {
            ["runId"] = planRunId,
            ["runType"] = RunType.TestPlan.ToString(),
            ["planId"] = plan.Identity.Id,
            ["planVersion"] = plan.Identity.Version,
            ["startTime"] = planResult.StartTime,
            ["endTime"] = planResult.EndTime,
            ["status"] = status.ToString()
        });

        return planRunId;
    }

    private async Task AppendTestCaseIndex(
        EngineRoots roots,
        TestCaseRunResult caseResult,
        string? parentRunId,
        Identity? suiteIdentity,
        string? planId,
        string? planVersion,
        string? nodeId)
    {
        var entry = new Dictionary<string, object?>
        {
            ["runId"] = caseResult.RunId,
            ["runType"] = RunType.TestCase.ToString(),
            ["testId"] = caseResult.ResultPayload.TestId,
            ["testVersion"] = caseResult.ResultPayload.TestVersion,
            ["startTime"] = caseResult.StartTime,
            ["endTime"] = caseResult.EndTime,
            ["status"] = caseResult.Status.ToString()
        };
        if (nodeId != null)
        {
            entry["nodeId"] = nodeId;
        }
        if (suiteIdentity != null)
        {
            entry["suiteId"] = suiteIdentity.Id;
            entry["suiteVersion"] = suiteIdentity.Version;
        }
        if (planId != null && planVersion != null)
        {
            entry["planId"] = planId;
            entry["planVersion"] = planVersion;
        }
        if (parentRunId != null)
        {
            entry["parentRunId"] = parentRunId;
        }
        await _indexWriter.AppendAsync(Path.Combine(roots.ResolvedRunsRoot, "index.jsonl"), entry);
    }

    private static string GenerateGroupRunId(string runsRoot)
    {
        string runId;
        do
        {
            runId = $"G-{Guid.NewGuid():N}";
        } while (Directory.Exists(Path.Combine(runsRoot, runId)));

        return runId;
    }

    private static void WriteGroupArtifacts(string folder, object manifest, Identity identity, RunRequest request, Dictionary<string, string> environment, Dictionary<string, object?>? controls, List<RunEvent> events)
    {
        WriteJson(Path.Combine(folder, "manifest.json"), new
        {
            sourceManifest = manifest,
            resolvedIdentity = new { id = identity.Id, version = identity.Version },
            resolvedAt = DateTime.UtcNow.ToString("O")
        });
        WriteJson(Path.Combine(folder, "environment.json"), environment);
        if (controls != null)
        {
            WriteJson(Path.Combine(folder, "controls.json"), controls);
        }

        WriteJson(Path.Combine(folder, "runRequest.json"), request);

        if (events.Count > 0)
        {
            using var writer = new StreamWriter(Path.Combine(folder, "events.jsonl"));
            foreach (var ev in events)
            {
                writer.WriteLine(JsonSerializer.Serialize(new { ts = ev.Timestamp.ToString("O"), code = ev.Code, data = ev.Data }, JsonUtilities.SerializerOptions));
            }
        }
    }

    private static void WriteJson(string path, object payload)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonUtilities.SerializerOptions));
    }

    private static int ReadControlInt(Dictionary<string, object?>? controls, string name, int defaultValue)
    {
        if (controls == null || !controls.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var parsed))
        {
            return parsed;
        }

        return defaultValue;
    }

    private static bool ReadControlBool(Dictionary<string, object?>? controls, string name, bool defaultValue)
    {
        if (controls == null || !controls.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (value is JsonElement falseElement && falseElement.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return defaultValue;
    }

    private static RunStatus AggregateStatus(IEnumerable<RunStatus> statuses)
    {
        var list = statuses.ToList();
        if (list.Count == 0)
        {
            return RunStatus.Passed;
        }

        if (list.Contains(RunStatus.Aborted))
        {
            return RunStatus.Aborted;
        }

        if (list.Contains(RunStatus.Error))
        {
            return RunStatus.Error;
        }

        if (list.Contains(RunStatus.Timeout))
        {
            return RunStatus.Timeout;
        }

        if (list.Contains(RunStatus.Failed))
        {
            return RunStatus.Failed;
        }

        return RunStatus.Passed;
    }

    private static void EnsureNodeIdUnique(TestSuiteManifest manifest)
    {
        var groups = manifest.TestCases.GroupBy(node => node.NodeId).Where(group => group.Count() > 1).ToList();
        if (groups.Count > 0)
        {
            throw new EngineException("Suite.NodeId.NonUnique", "Suite nodeId must be unique.", new Dictionary<string, object?>
            {
                ["nodeId"] = groups[0].Key
            });
        }
    }

    private static void ValidateNodeOverrides(TestSuiteManifest manifest, Dictionary<string, NodeOverride>? overrides)
    {
        if (overrides == null)
        {
            return;
        }

        var nodeIds = manifest.TestCases.Select(node => node.NodeId).ToHashSet(StringComparer.Ordinal);
        foreach (var key in overrides.Keys)
        {
            if (!nodeIds.Contains(key))
            {
                throw new EngineException("Suite.NodeOverride.Unknown", "Unknown node override.", new Dictionary<string, object?>
                {
                    ["nodeId"] = key
                });
            }
        }
    }
}
