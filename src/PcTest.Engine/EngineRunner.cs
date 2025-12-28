using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Models;
using PcTest.Runner;

namespace PcTest.Engine;

public sealed class EngineRunner
{
    private readonly DiscoveryResult _discovery;
    private readonly ITestCaseRunner _runner;
    private readonly string _runsRoot;
    private readonly string _engineVersion = "1.0.0";

    public EngineRunner(DiscoveryResult discovery, string runsRoot, ITestCaseRunner? runner = null)
    {
        _discovery = discovery;
        _runner = runner ?? new TestCaseRunner();
        _runsRoot = PathUtil.NormalizePath(runsRoot);
        Directory.CreateDirectory(_runsRoot);
    }

    public async Task<TestCaseRunResult> RunTestCaseAsync(RunRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRunRequest(request, RunType.TestCase);
        var identity = Identity.Parse(request.TestCase ?? string.Empty);
        var testCase = _discovery.GetTestCase(identity);
        var inputs = BuildInputs(testCase.Manifest, null, request.CaseInputs);
        var environment = EnvironmentResolver.ResolveForStandalone(request);
        var resolved = InputResolver.Resolve(testCase.Manifest, inputs, environment, null);

        var runnerRequest = new RunnerRequest
        {
            RunsRoot = _runsRoot,
            TestCasePath = Path.GetDirectoryName(testCase.ManifestPath) ?? string.Empty,
            Manifest = testCase.Manifest,
            EffectiveInputs = resolved.Values,
            RedactedInputs = resolved.RedactedValues,
            SecretInputs = resolved.SecretInputs,
            EffectiveEnvironment = environment,
            RedactedEnvironment = environment,
            WorkingDir = null,
            EngineVersion = _engineVersion,
            ResolvedRef = Path.GetDirectoryName(testCase.ManifestPath)
        };

        var run = await _runner.RunAsync(runnerRequest, cancellationToken);
        WriteIndexEntry(run.Result, run.RunId, RunType.TestCase, null, null, null);
        return run;
    }

    public async Task<GroupRunResult> RunSuiteAsync(RunRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRunRequest(request, RunType.TestSuite);
        var identity = Identity.Parse(request.Suite ?? string.Empty);
        var suite = _discovery.GetSuite(identity);
        var execution = await RunSuiteInternalAsync(suite, request, null, null, cancellationToken);
        return execution.Result;
    }

    public async Task<GroupRunResult> RunPlanAsync(RunRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRunRequest(request, RunType.TestPlan);
        var identity = Identity.Parse(request.Plan ?? string.Empty);
        var plan = _discovery.GetPlan(identity);

        var planRunId = CreateGroupRunId("P");
        var planFolder = CreateUniqueFolder(planRunId);

        var start = DateTimeOffset.UtcNow;
        var childrenPath = Path.Combine(planFolder, "children.jsonl");
        var childRuns = new List<string>();
        var childStatuses = new List<RunStatus>();

        WriteGroupArtifacts(planFolder, plan.Manifest, null, EnvironmentResolver.ResolveForPlanOnly(plan.Manifest, request), request);

        foreach (var suiteIdentity in plan.SuiteIdentities)
        {
            var suiteDefinition = _discovery.GetSuite(suiteIdentity);
            var suiteExecution = await RunSuiteInternalAsync(suiteDefinition, request, plan, planRunId, cancellationToken);
            childRuns.Add(suiteExecution.RunId);
            childStatuses.Add(suiteExecution.Result.Status);
            JsonUtil.AppendJsonLine(childrenPath, new
            {
                runId = suiteExecution.RunId,
                suiteId = suiteDefinition.Identity.Id,
                suiteVersion = suiteDefinition.Identity.Version,
                status = suiteExecution.Result.Status.ToString()
            });
        }

        var end = DateTimeOffset.UtcNow;
        var result = new GroupRunResult
        {
            RunType = RunType.TestPlan,
            PlanId = plan.Identity.Id,
            PlanVersion = plan.Identity.Version,
            Status = Aggregate(childStatuses),
            StartTime = start.ToString("O"),
            EndTime = end.ToString("O"),
            ChildRunIds = childRuns
        };

        JsonUtil.WriteJsonFile(Path.Combine(planFolder, "result.json"), result);
        WriteIndexEntry(result, planRunId, RunType.TestPlan, null, plan, null);
        return result;
    }

    private async Task<GroupRunExecution> RunSuiteInternalAsync(TestSuiteDefinition suite, RunRequest request, TestPlanDefinition? plan, string? parentRunId, CancellationToken cancellationToken)
    {
        var suiteRunId = CreateGroupRunId("S");
        var suiteFolder = CreateUniqueFolder(suiteRunId);
        var start = DateTimeOffset.UtcNow;
        var controls = ParseControls(suite.Manifest.Controls);

        if (controls.MaxParallel > 1)
        {
            JsonUtil.AppendJsonLine(Path.Combine(suiteFolder, "events.jsonl"), new
            {
                timestamp = DateTimeOffset.UtcNow.ToString("O"),
                code = "Controls.MaxParallel.Ignored",
                location = "suite.manifest.json",
                message = $"maxParallel {controls.MaxParallel} ignored; running sequentially."
            });
        }

        var env = plan is null
            ? EnvironmentResolver.ResolveForSuite(suite.Manifest, request)
            : EnvironmentResolver.ResolveForPlan(plan.Manifest, suite.Manifest, request);

        WriteGroupArtifacts(suiteFolder, suite.Manifest, controls, env, request);

        var childrenPath = Path.Combine(suiteFolder, "children.jsonl");
        var childStatuses = new List<RunStatus>();
        var childRunIds = new List<string>();

        if (request.NodeOverrides is not null)
        {
            var knownNodes = suite.Manifest.TestCases.Select(node => node.NodeId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var overrideNode in request.NodeOverrides.Keys)
            {
                if (!knownNodes.Contains(overrideNode))
                {
                    throw new EngineException("RunRequest.NodeOverride.Unknown", new { nodeId = overrideNode });
                }
            }
        }

        for (var iteration = 0; iteration < controls.Repeat; iteration++)
        {
            foreach (var reference in suite.ResolvedTestCases)
            {
                var node = reference.Node;
                var overrides = request.NodeOverrides?.GetValueOrDefault(node.NodeId)?.Inputs;
                var inputs = BuildInputs(reference.Definition.Manifest, node.Inputs, overrides);
                var resolved = InputResolver.Resolve(reference.Definition.Manifest, inputs, env, node.NodeId);

                var runnerRequest = new RunnerRequest
                {
                    RunsRoot = _runsRoot,
                    TestCasePath = Path.GetDirectoryName(reference.Definition.ManifestPath) ?? string.Empty,
                    Manifest = reference.Definition.Manifest,
                    EffectiveInputs = resolved.Values,
                    RedactedInputs = resolved.RedactedValues,
                    SecretInputs = resolved.SecretInputs,
                    EffectiveEnvironment = env,
                    RedactedEnvironment = env,
                    WorkingDir = suite.Manifest.Environment?.WorkingDir,
                    NodeId = node.NodeId,
                    SuiteId = suite.Identity.Id,
                    SuiteVersion = suite.Identity.Version,
                    PlanId = plan?.Identity.Id,
                    PlanVersion = plan?.Identity.Version,
                    ResolvedRef = reference.ResolvedManifestPath,
                    EngineVersion = _engineVersion
                };

                var retryCount = 0;
                TestCaseRunResult run;
                do
                {
                    run = await _runner.RunAsync(runnerRequest, cancellationToken);
                    retryCount++;
                } while (retryCount <= controls.RetryOnError && run.Result?.Status is RunStatus.Error or RunStatus.Timeout);

                if (run.Result is null)
                {
                    continue;
                }

                childStatuses.Add(run.Result.Status);
                childRunIds.Add(run.RunId);
                JsonUtil.AppendJsonLine(childrenPath, new
                {
                    runId = run.RunId,
                    nodeId = node.NodeId,
                    testId = reference.Definition.Manifest.Id,
                    testVersion = reference.Definition.Manifest.Version,
                    status = run.Result.Status.ToString()
                });

                WriteIndexEntry(run.Result, run.RunId, RunType.TestCase, suite, plan, suiteRunId);

                if (!controls.ContinueOnFailure && run.Result.Status != RunStatus.Passed)
                {
                    iteration = controls.Repeat;
                    break;
                }
            }
        }

        var end = DateTimeOffset.UtcNow;
        var result = new GroupRunResult
        {
            RunType = RunType.TestSuite,
            SuiteId = suite.Identity.Id,
            SuiteVersion = suite.Identity.Version,
            PlanId = plan?.Identity.Id,
            PlanVersion = plan?.Identity.Version,
            Status = Aggregate(childStatuses),
            StartTime = start.ToString("O"),
            EndTime = end.ToString("O"),
            ChildRunIds = childRunIds
        };

        JsonUtil.WriteJsonFile(Path.Combine(suiteFolder, "result.json"), result);
        WriteIndexEntry(result, suiteRunId, RunType.TestSuite, suite, plan, parentRunId);
        return new GroupRunExecution(suiteRunId, result);
    }

    private Dictionary<string, JsonElement> BuildInputs(TestCaseManifest manifest, Dictionary<string, JsonElement>? suiteInputs, Dictionary<string, JsonElement>? overrideInputs)
    {
        var inputs = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (manifest.Parameters is not null)
        {
            foreach (var parameter in manifest.Parameters)
            {
                if (parameter.Default is { } defaultValue)
                {
                    inputs[parameter.Name] = defaultValue;
                }
            }
        }

        if (suiteInputs is not null)
        {
            foreach (var (key, value) in suiteInputs)
            {
                inputs[key] = value;
            }
        }

        if (overrideInputs is not null)
        {
            foreach (var (key, value) in overrideInputs)
            {
                inputs[key] = value;
            }
        }

        return inputs;
    }

    private void ValidateRunRequest(RunRequest request, RunType runType)
    {
        var selected = new[] { request.Suite, request.Plan, request.TestCase }.Count(x => !string.IsNullOrWhiteSpace(x));
        if (selected != 1)
        {
            throw new EngineException("RunRequest.Invalid", new { reason = "MustSpecifyExactlyOne" });
        }

        if (runType == RunType.TestPlan && (request.NodeOverrides is not null || request.CaseInputs is not null))
        {
            throw new EngineException("RunRequest.Invalid", new { reason = "PlanEnvOnly" });
        }

        if (runType == RunType.TestCase && request.NodeOverrides is not null)
        {
            throw new EngineException("RunRequest.Invalid", new { reason = "StandaloneNoNodeOverrides" });
        }
    }

    private static SuiteControls ParseControls(JsonElement? element)
    {
        var controls = new SuiteControls();
        if (element is null || element.Value.ValueKind != JsonValueKind.Object)
        {
            return controls;
        }

        if (element.Value.TryGetProperty("repeat", out var repeat) && repeat.TryGetInt32(out var repeatValue))
        {
            controls.Repeat = Math.Max(1, repeatValue);
        }

        if (element.Value.TryGetProperty("retryOnError", out var retry) && retry.TryGetInt32(out var retryValue))
        {
            controls.RetryOnError = Math.Max(0, retryValue);
        }

        if (element.Value.TryGetProperty("continueOnFailure", out var continueOnFailure) && continueOnFailure.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            controls.ContinueOnFailure = continueOnFailure.GetBoolean();
        }

        if (element.Value.TryGetProperty("maxParallel", out var maxParallel) && maxParallel.TryGetInt32(out var parallelValue))
        {
            controls.MaxParallel = Math.Max(1, parallelValue);
        }

        return controls;
    }

    private string CreateGroupRunId(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    private string CreateUniqueFolder(string runId)
    {
        var folder = Path.Combine(_runsRoot, runId);
        while (Directory.Exists(folder))
        {
            runId = $"{runId}-{Guid.NewGuid():N}";
            folder = Path.Combine(_runsRoot, runId);
        }

        Directory.CreateDirectory(folder);
        return folder;
    }

    private void WriteGroupArtifacts(string folder, object manifest, SuiteControls? controls, Dictionary<string, string> environment, RunRequest request)
    {
        JsonUtil.WriteJsonFile(Path.Combine(folder, "manifest.json"), manifest);
        if (controls is not null)
        {
            JsonUtil.WriteJsonFile(Path.Combine(folder, "controls.json"), controls);
        }

        JsonUtil.WriteJsonFile(Path.Combine(folder, "environment.json"), environment);
        JsonUtil.WriteJsonFile(Path.Combine(folder, "runRequest.json"), request);
    }

    private void WriteIndexEntry(TestCaseResult? result, string runId, RunType runType, TestSuiteDefinition? suite, TestPlanDefinition? plan, string? parentRunId)
    {
        if (result is null)
        {
            return;
        }

        var entry = new Dictionary<string, object?>
        {
            ["runId"] = runId,
            ["runType"] = runType.ToString(),
            ["testId"] = result.TestId,
            ["testVersion"] = result.TestVersion,
            ["startTime"] = result.StartTime,
            ["endTime"] = result.EndTime,
            ["status"] = result.Status.ToString()
        };

        if (!string.IsNullOrWhiteSpace(result.NodeId))
        {
            entry["nodeId"] = result.NodeId;
        }

        if (!string.IsNullOrWhiteSpace(result.SuiteId))
        {
            entry["suiteId"] = result.SuiteId;
            entry["suiteVersion"] = result.SuiteVersion;
        }

        if (!string.IsNullOrWhiteSpace(result.PlanId))
        {
            entry["planId"] = result.PlanId;
            entry["planVersion"] = result.PlanVersion;
        }

        if (!string.IsNullOrWhiteSpace(parentRunId))
        {
            entry["parentRunId"] = parentRunId;
        }

        JsonUtil.AppendJsonLine(Path.Combine(_runsRoot, "index.jsonl"), entry);
    }

    private void WriteIndexEntry(GroupRunResult result, string runId, RunType runType, TestSuiteDefinition? suite, TestPlanDefinition? plan, string? parentRunId)
    {
        var entry = new Dictionary<string, object?>
        {
            ["runId"] = runId,
            ["runType"] = runType.ToString(),
            ["startTime"] = result.StartTime,
            ["endTime"] = result.EndTime,
            ["status"] = result.Status.ToString()
        };

        if (runType == RunType.TestSuite && suite is not null)
        {
            entry["suiteId"] = suite.Identity.Id;
            entry["suiteVersion"] = suite.Identity.Version;
        }

        if (runType == RunType.TestPlan && plan is not null)
        {
            entry["planId"] = plan.Identity.Id;
            entry["planVersion"] = plan.Identity.Version;
        }

        if (plan is not null && runType == RunType.TestSuite)
        {
            entry["planId"] = plan.Identity.Id;
            entry["planVersion"] = plan.Identity.Version;
        }

        if (!string.IsNullOrWhiteSpace(parentRunId))
        {
            entry["parentRunId"] = parentRunId;
        }

        JsonUtil.AppendJsonLine(Path.Combine(_runsRoot, "index.jsonl"), entry);
    }

    private static RunStatus Aggregate(IEnumerable<RunStatus> statuses)
    {
        var list = statuses.ToList();
        if (list.Count == 0)
        {
            return RunStatus.Passed;
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

        if (list.Contains(RunStatus.Aborted))
        {
            return RunStatus.Aborted;
        }

        return RunStatus.Passed;
    }
}

public sealed class SuiteControls
{
    public int Repeat { get; set; } = 1;
    public int MaxParallel { get; set; } = 1;
    public bool ContinueOnFailure { get; set; }
    public int RetryOnError { get; set; }
}

public sealed record GroupRunExecution(string RunId, GroupRunResult Result);
