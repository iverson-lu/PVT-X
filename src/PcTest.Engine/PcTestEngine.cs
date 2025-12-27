using System.Text.Json.Nodes;
using PcTest.Contracts;
using PcTest.Runner;

namespace PcTest.Engine;

public sealed class EngineRunOptions
{
    public string RunsRoot { get; init; } = string.Empty;
    public string TestCaseRoot { get; init; } = string.Empty;
    public DiscoveryResult Discovery { get; init; } = new();
    public ICaseRunner Runner { get; init; } = new PowerShellRunner();
}

public sealed class PcTestEngine
{
    public void DiscoverToFile(DiscoveryRoots roots, string outputPath)
    {
        var result = new DiscoveryService().Discover(roots);
        JsonUtilities.WriteJson(outputPath, new
        {
            testCases = result.TestCases.Select(tc => new { tc.Manifest.Id, tc.Manifest.Version, path = tc.Path }),
            testSuites = result.TestSuites.Select(ts => new { ts.Manifest.Id, ts.Manifest.Version, path = ts.Path }),
            testPlans = result.TestPlans.Select(tp => new { tp.Manifest.Id, tp.Manifest.Version, path = tp.Path }),
            errors = result.Errors
        });
    }

    public RunCaseResult RunStandaloneTestCase(EngineRunOptions options, RunRequest request)
    {
        if (request.TestCase is null)
        {
            throw new InvalidOperationException("RunRequest missing testCase.");
        }
        var identity = Validation.ParseIdentity(request.TestCase);
        var testCase = ResolveIdentity(options.Discovery.TestCases, identity, "TestCase");
        var parameters = BuildParameterMap(testCase.Manifest);
        var defaults = BuildDefaults(testCase.Manifest);
        var overrides = request.CaseInputs ?? new Dictionary<string, JsonNode?>();
        var environment = EnvironmentResolver.ResolveForTestCase(request.EnvironmentOverrides?.Env);

        var inputResolution = InputResolver.Resolve(parameters, defaults, overrides, environment, null);
        if (inputResolution.Errors.Count > 0)
        {
            throw new InvalidOperationException("Input resolution failed.");
        }

        var requestRunner = new RunCaseRequest
        {
            RunsRoot = options.RunsRoot,
            Manifest = testCase.Manifest,
            ManifestPath = testCase.Path,
            ResolvedRef = Path.Combine(Path.GetDirectoryName(testCase.Path) ?? string.Empty, "run.ps1"),
            EffectiveInputs = inputResolution.EffectiveInputs,
            InputTemplates = inputResolution.InputTemplates,
            EffectiveEnvironment = environment,
            SecretInputs = inputResolution.SecretInputs
        };

        var result = options.Runner.Run(requestRunner);
        AppendIndex(options.RunsRoot, new
        {
            runId = result.RunId,
            runType = "TestCase",
            testId = testCase.Manifest.Id,
            testVersion = testCase.Manifest.Version,
            startTime = result.StartTime.ToString("O"),
            endTime = result.EndTime.ToString("O"),
            status = result.Status
        });
        return result;
    }

    public SuiteRunSummary RunSuite(EngineRunOptions options, RunRequest request, Identity? planIdentity = null, Dictionary<string, string>? planEnv = null, string? parentRunId = null)
    {
        if (request.Suite is null)
        {
            throw new InvalidOperationException("RunRequest missing suite.");
        }
        var suiteIdentity = Validation.ParseIdentity(request.Suite);
        var suite = ResolveIdentity(options.Discovery.TestSuites, suiteIdentity, "TestSuite");
        var groupRunId = GenerateGroupRunId();
        var groupFolder = CreateGroupRunFolder(options.RunsRoot, groupRunId);
        var eventsPath = Path.Combine(groupFolder, "events.jsonl");

        if (suite.Manifest.Controls? ["maxParallel"] is not null)
        {
            JsonUtilities.WriteJsonLine(eventsPath, new RunnerEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = "Warning",
                Code = "Controls.MaxParallel.Ignored",
                Message = "maxParallel is ignored in sequential runner.",
                Data = new { value = suite.Manifest.Controls["maxParallel"]?.ToJsonString() }
            });
        }

        JsonUtilities.WriteJson(Path.Combine(groupFolder, "manifest.json"), suite.Manifest);
        JsonUtilities.WriteJson(Path.Combine(groupFolder, "controls.json"), suite.Manifest.Controls ?? new JsonObject());
        JsonUtilities.WriteJson(Path.Combine(groupFolder, "environment.json"), suite.Manifest.Environment ?? new SuiteEnvironment());
        if (request is not null)
        {
            JsonUtilities.WriteJson(Path.Combine(groupFolder, "runRequest.json"), request);
        }

        var environment = EnvironmentResolver.ResolveForPlan(planEnv, suite.Manifest.Environment?.Env, request.EnvironmentOverrides?.Env);
        var childIds = new List<string>();
        var childrenPath = Path.Combine(groupFolder, "children.jsonl");
        var start = DateTimeOffset.UtcNow;
        string status = "Passed";

        if (request.NodeOverrides is not null)
        {
            var knownNodes = suite.Manifest.TestCases.Select(tc => tc.NodeId).ToHashSet(StringComparer.Ordinal);
            foreach (var nodeId in request.NodeOverrides.Keys)
            {
                if (!knownNodes.Contains(nodeId))
                {
                    throw new InvalidOperationException("Unknown nodeId in overrides.");
                }
            }
        }

        var duplicateNodes = suite.Manifest.TestCases.GroupBy(tc => tc.NodeId, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicateNodes is not null)
        {
            throw new InvalidOperationException("Suite nodeId must be unique.");
        }

        var repeat = suite.Manifest.Controls?["repeat"]?.GetValue<int>() ?? 1;
        var continueOnFailure = suite.Manifest.Controls?["continueOnFailure"]?.GetValue<bool>() ?? false;
        var retryOnError = suite.Manifest.Controls?["retryOnError"]?.GetValue<int>() ?? 0;

        var shouldStop = false;
        for (var iteration = 0; iteration < repeat && !shouldStop; iteration++)
        {
            foreach (var node in suite.Manifest.TestCases)
            {
                var (manifestPath, error) = PathResolver.ResolveTestCaseRef(suite.Path, node.Ref, options.TestCaseRoot);
                if (error is not null)
                {
                    throw new InvalidOperationException(error.Message);
                }

                var testCase = JsonUtilities.ReadJson<TestCaseManifest>(manifestPath!);
                var parameters = BuildParameterMap(testCase);
                var defaults = BuildDefaults(testCase);
                var nodeInputs = node.Inputs ?? new Dictionary<string, JsonNode?>();
                var overrideInputs = request.NodeOverrides?.GetValueOrDefault(node.NodeId)?.Inputs ?? new Dictionary<string, JsonNode?>();
                var overrides = new Dictionary<string, JsonNode?>(nodeInputs, StringComparer.Ordinal);
                foreach (var kvp in overrideInputs)
                {
                    overrides[kvp.Key] = kvp.Value;
                }
                var inputResolution = InputResolver.Resolve(parameters, defaults, overrides, environment, node.NodeId);
                if (inputResolution.Errors.Count > 0)
                {
                    throw new InvalidOperationException("Input resolution failed.");
                }

                var runRequest = new RunCaseRequest
                {
                    RunsRoot = options.RunsRoot,
                    Manifest = testCase,
                    ManifestPath = manifestPath!,
                    ResolvedRef = Path.Combine(Path.GetDirectoryName(manifestPath!) ?? string.Empty, "run.ps1"),
                    EffectiveInputs = inputResolution.EffectiveInputs,
                    InputTemplates = inputResolution.InputTemplates,
                    EffectiveEnvironment = environment,
                    SecretInputs = inputResolution.SecretInputs,
                    WorkingDir = suite.Manifest.Environment?.WorkingDir,
                    NodeId = node.NodeId,
                    SuiteIdentity = suiteIdentity,
                    PlanIdentity = planIdentity
                };

                RunCaseResult result = null!;
                for (var attempt = 0; attempt <= retryOnError; attempt++)
                {
                    result = options.Runner.Run(runRequest);
                    childIds.Add(result.RunId);
                    JsonUtilities.WriteJsonLine(childrenPath, new
                    {
                        runId = result.RunId,
                        nodeId = node.NodeId,
                        testId = testCase.Id,
                        testVersion = testCase.Version,
                        status = result.Status
                    });

                    AppendIndex(options.RunsRoot, new
                    {
                        runId = result.RunId,
                        runType = "TestCase",
                        nodeId = node.NodeId,
                        testId = testCase.Id,
                        testVersion = testCase.Version,
                        suiteId = suiteIdentity.Id,
                        suiteVersion = suiteIdentity.Version,
                        planId = planIdentity?.Id,
                        planVersion = planIdentity?.Version,
                        parentRunId = groupRunId,
                        startTime = result.StartTime.ToString("O"),
                        endTime = result.EndTime.ToString("O"),
                        status = result.Status
                    });

                    if (result.Status is not ("Error" or "Timeout"))
                    {
                        break;
                    }
                }

                status = AggregateStatus(status, result!.Status);
                if (!continueOnFailure && result.Status != "Passed")
                {
                    shouldStop = true;
                    break;
                }
            }
        }

        var end = DateTimeOffset.UtcNow;
        JsonUtilities.WriteJson(Path.Combine(groupFolder, "result.json"), new
        {
            schemaVersion = SchemaVersions.Current,
            runType = "TestSuite",
            suiteId = suiteIdentity.Id,
            suiteVersion = suiteIdentity.Version,
            planId = planIdentity?.Id,
            planVersion = planIdentity?.Version,
            status,
            startTime = start.ToString("O"),
            endTime = end.ToString("O"),
            childRunIds = childIds
        });

        AppendIndex(options.RunsRoot, new
        {
            runId = groupRunId,
            runType = "TestSuite",
            suiteId = suiteIdentity.Id,
            suiteVersion = suiteIdentity.Version,
            planId = planIdentity?.Id,
            planVersion = planIdentity?.Version,
            parentRunId,
            startTime = start.ToString("O"),
            endTime = end.ToString("O"),
            status
        });

        return new SuiteRunSummary(groupRunId, status, start, DateTimeOffset.UtcNow, childIds);
    }

    public void RunPlan(EngineRunOptions options, RunRequest request)
    {
        if (request.Plan is null)
        {
            throw new InvalidOperationException("RunRequest missing plan.");
        }
        if (request.CaseInputs is not null || request.NodeOverrides is not null)
        {
            throw new InvalidOperationException("Plan run request must not include inputs overrides.");
        }
        var planIdentity = Validation.ParseIdentity(request.Plan);
        var plan = ResolveIdentity(options.Discovery.TestPlans, planIdentity, "TestPlan");
        var groupRunId = GenerateGroupRunId();
        var groupFolder = CreateGroupRunFolder(options.RunsRoot, groupRunId);
        var childrenPath = Path.Combine(groupFolder, "children.jsonl");

        JsonUtilities.WriteJson(Path.Combine(groupFolder, "manifest.json"), plan.Manifest);
        JsonUtilities.WriteJson(Path.Combine(groupFolder, "environment.json"), plan.Manifest.Environment ?? new PlanEnvironment());
        JsonUtilities.WriteJson(Path.Combine(groupFolder, "runRequest.json"), request);

        var start = DateTimeOffset.UtcNow;
        var status = "Passed";
        var childIds = new List<string>();

        foreach (var suiteRef in plan.Manifest.Suites)
        {
            var suiteIdentity = Validation.ParseIdentity(suiteRef);
            var suite = ResolveIdentity(options.Discovery.TestSuites, suiteIdentity, "TestSuite");
            var suiteRequest = new RunRequest
            {
                Suite = suiteRef,
                EnvironmentOverrides = new EnvironmentOverrides { Env = request.EnvironmentOverrides?.Env }
            };
            var suiteSummary = RunSuite(options, suiteRequest, planIdentity, plan.Manifest.Environment?.Env, groupRunId);
            JsonUtilities.WriteJsonLine(childrenPath, new
            {
                runId = suiteSummary.RunId,
                suiteId = suite.Manifest.Id,
                suiteVersion = suite.Manifest.Version,
                status = suiteSummary.Status
            });
            childIds.Add(suiteSummary.RunId);
            status = AggregateStatus(status, suiteSummary.Status);
        }

        var end = DateTimeOffset.UtcNow;
        JsonUtilities.WriteJson(Path.Combine(groupFolder, "result.json"), new
        {
            schemaVersion = SchemaVersions.Current,
            runType = "TestPlan",
            planId = planIdentity.Id,
            planVersion = planIdentity.Version,
            status,
            startTime = start.ToString("O"),
            endTime = end.ToString("O"),
            childRunIds = childIds
        });

        AppendIndex(options.RunsRoot, new
        {
            runId = groupRunId,
            runType = "TestPlan",
            planId = planIdentity.Id,
            planVersion = planIdentity.Version,
            startTime = start.ToString("O"),
            endTime = end.ToString("O"),
            status
        });
    }

    private static DiscoveredTestCase ResolveIdentity(List<DiscoveredTestCase> items, Identity identity, string entityType)
    {
        var matches = items.Where(item => item.Manifest.Id == identity.Id && item.Manifest.Version == identity.Version).ToList();
        if (matches.Count == 0)
        {
            throw new InvalidOperationException($"{entityType} not found.");
        }
        if (matches.Count > 1)
        {
            throw new InvalidOperationException($"{entityType} non-unique.");
        }
        return matches[0];
    }

    private static DiscoveredTestSuite ResolveIdentity(List<DiscoveredTestSuite> items, Identity identity, string entityType)
    {
        var matches = items.Where(item => item.Manifest.Id == identity.Id && item.Manifest.Version == identity.Version).ToList();
        if (matches.Count == 0)
        {
            throw new InvalidOperationException($"{entityType} not found.");
        }
        if (matches.Count > 1)
        {
            throw new InvalidOperationException($"{entityType} non-unique.");
        }
        return matches[0];
    }

    private static DiscoveredTestPlan ResolveIdentity(List<DiscoveredTestPlan> items, Identity identity, string entityType)
    {
        var matches = items.Where(item => item.Manifest.Id == identity.Id && item.Manifest.Version == identity.Version).ToList();
        if (matches.Count == 0)
        {
            throw new InvalidOperationException($"{entityType} not found.");
        }
        if (matches.Count > 1)
        {
            throw new InvalidOperationException($"{entityType} non-unique.");
        }
        return matches[0];
    }

    private static Dictionary<string, ParameterDefinition> BuildParameterMap(TestCaseManifest manifest)
    {
        return manifest.Parameters?.ToDictionary(p => p.Name, StringComparer.Ordinal) ?? new Dictionary<string, ParameterDefinition>(StringComparer.Ordinal);
    }

    private static Dictionary<string, JsonNode?> BuildDefaults(TestCaseManifest manifest)
    {
        var defaults = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (manifest.Parameters is null)
        {
            return defaults;
        }
        foreach (var param in manifest.Parameters)
        {
            if (param.Default is not null)
            {
                defaults[param.Name] = param.Default;
            }
        }
        return defaults;
    }

    private static string GenerateGroupRunId()
    {
        return $"G-{Guid.NewGuid():N}";
    }

    private static string CreateGroupRunFolder(string runsRoot, string runId)
    {
        Directory.CreateDirectory(runsRoot);
        var folder = Path.Combine(runsRoot, runId);
        if (Directory.Exists(folder))
        {
            folder = Path.Combine(runsRoot, $"{runId}-{Guid.NewGuid():N}");
        }
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void AppendIndex(string runsRoot, object entry)
    {
        var indexPath = Path.Combine(runsRoot, "index.jsonl");
        JsonUtilities.WriteJsonLine(indexPath, entry);
    }

    private static string AggregateStatus(string current, string next)
    {
        var order = new[] { "Passed", "Failed", "Timeout", "Error", "Aborted" };
        var currentIndex = Array.IndexOf(order, current);
        var nextIndex = Array.IndexOf(order, next);
        if (nextIndex > currentIndex)
        {
            return next;
        }
        return current;
    }
}

public sealed record SuiteRunSummary(string RunId, string Status, DateTimeOffset StartTime, DateTimeOffset EndTime, List<string> ChildRunIds);
