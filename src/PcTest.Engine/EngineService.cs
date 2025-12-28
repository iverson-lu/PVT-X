using System.Globalization;
using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine;

public sealed class EngineOptions
{
    public string RunsRoot { get; init; } = string.Empty;
}

public sealed class EngineService
{
    private readonly ICaseRunner _runner;
    private readonly EngineOptions _options;

    public EngineService(ICaseRunner runner, EngineOptions options)
    {
        _runner = runner;
        _options = options;
        Directory.CreateDirectory(_options.RunsRoot);
    }

    public Task<object> ExecuteAsync(DiscoveryResult discovery, RunRequest request, CancellationToken cancellationToken)
    {
        RunRequestValidator.Validate(request);

        if (!string.IsNullOrEmpty(request.TestCase))
        {
            var identity = Identity.Parse(request.TestCase);
            return RunStandaloneTestCaseAsync(discovery, identity, request, cancellationToken).ContinueWith<object>(t => t.Result, cancellationToken);
        }

        if (!string.IsNullOrEmpty(request.Suite))
        {
            var identity = Identity.Parse(request.Suite);
            return RunSuiteAsync(discovery, identity, request, null, null, null, cancellationToken).ContinueWith<object>(t => t.Result, cancellationToken);
        }

        if (!string.IsNullOrEmpty(request.Plan))
        {
            var identity = Identity.Parse(request.Plan);
            return RunPlanAsync(discovery, identity, request, cancellationToken).ContinueWith<object>(t => t.Result, cancellationToken);
        }

        throw new PcTestException(new[]
        {
            new PcTestError("RunRequest.Invalid", "RunRequest must specify suite, testCase, or plan.")
        });
    }

    public async Task<CaseRunResult> RunStandaloneTestCaseAsync(
        DiscoveryResult discovery,
        Identity testCaseIdentity,
        RunRequest runRequest,
        CancellationToken cancellationToken)
    {
        if (!discovery.TestCases.TryGetValue(testCaseIdentity, out var testCase))
        {
            throw new PcTestException(new[]
            {
                new PcTestError("RunRequest.ResolveFailed", $"TestCase {testCaseIdentity} not found.")
            });
        }

        var effectiveEnv = EnvironmentResolver.ResolveStandalone(runRequest.EnvironmentOverrides);
        var inputResolution = InputResolver.ResolveStandaloneInputs(runRequest.CaseInputs, testCase.Manifest.Parameters, effectiveEnv);
        var parameterTypes = BuildParameterTypes(testCase.Manifest);

        var caseRequest = new CaseRunRequest(
            _options.RunsRoot,
            testCase.FolderPath,
            testCase.ManifestPath,
            testCase.FolderPath,
            testCase.Identity,
            testCase.SourceManifest,
            parameterTypes,
            inputResolution.EffectiveInputs,
            inputResolution.RedactedInputs,
            effectiveEnv,
            inputResolution.InputTemplates,
            inputResolution.SecretInputs,
            GetWorkingDir(null),
            testCase.Manifest.TimeoutSec,
            null,
            null,
            null,
            EngineVersion.Current);

        var result = await _runner.RunCaseAsync(caseRequest, cancellationToken);
        IndexWriter.Append(_options.RunsRoot, BuildIndexEntry(result, null, null, null));
        return result;
    }

    public async Task<GroupRunResult> RunSuiteAsync(
        DiscoveryResult discovery,
        Identity suiteIdentity,
        RunRequest? runRequest,
        Identity? planIdentity,
        string? planRunId,
        JsonElement? planEnvironment,
        CancellationToken cancellationToken)
    {
        if (!discovery.Suites.TryGetValue(suiteIdentity, out var suite))
        {
            throw new PcTestException(new[]
            {
                new PcTestError("RunRequest.ResolveFailed", $"Suite {suiteIdentity} not found.")
            });
        }

        var groupRunId = RunIdGenerator.NextGroupRunId(_options.RunsRoot);
        var groupFolder = Path.Combine(_options.RunsRoot, groupRunId);
        Directory.CreateDirectory(groupFolder);

        var suiteControls = SuiteControls.FromManifest(suite.Manifest.Controls);
        ValidateNodeOverrides(suite.Manifest, runRequest?.NodeOverrides);
        if (suiteControls.MaxParallel > 1)
        {
            WriteEvent(groupFolder, new Dictionary<string, object?>
            {
                ["code"] = SpecConstants.ControlsMaxParallelIgnored,
                ["location"] = "suite.manifest.json",
                ["message"] = $"maxParallel={suiteControls.MaxParallel} ignored; sequential execution only."
            });
        }

        var suiteEnvironment = suite.Manifest.Environment;
        var effectiveEnvironment = EnvironmentResolver.Resolve(planEnvironment, suiteEnvironment, runRequest?.EnvironmentOverrides);
        var runRequestElement = runRequest != null ? JsonUtils.ToJsonElement(runRequest) : (JsonElement?)null;

        var resolvedWorkingDir = GetWorkingDir(suite.Manifest.Environment);
        var childrenPath = Path.Combine(groupFolder, "children.jsonl");
        var childRunIds = new List<string>();
        var childStatuses = new List<RunStatus>();
        var startTime = DateTimeOffset.UtcNow;

        for (var iteration = 0; iteration < suiteControls.Repeat; iteration++)
        {
            foreach (var node in suite.Manifest.TestCases)
            {
                var resolvedNode = ResolveSuiteNode(discovery, suite, node);
                var nodeOverride = runRequest?.NodeOverrides != null && runRequest.NodeOverrides.TryGetValue(node.NodeId, out var overrideValue)
                    ? overrideValue
                    : null;

                var inputResolution = InputResolver.ResolveInputs(
                    node.Inputs,
                    nodeOverride?.Inputs,
                    resolvedNode.TestCase.Manifest.Parameters,
                    effectiveEnvironment,
                    node.NodeId);

                var parameterTypes = BuildParameterTypes(resolvedNode.TestCase.Manifest);
                var caseRequest = new CaseRunRequest(
                    _options.RunsRoot,
                    resolvedNode.TestCase.FolderPath,
                    resolvedNode.TestCase.ManifestPath,
                    resolvedNode.ResolvedRef,
                    resolvedNode.TestCase.Identity,
                    resolvedNode.TestCase.SourceManifest,
                    parameterTypes,
                    inputResolution.EffectiveInputs,
                    inputResolution.RedactedInputs,
                    effectiveEnvironment,
                    inputResolution.InputTemplates,
                    inputResolution.SecretInputs,
                    resolvedWorkingDir,
                    resolvedNode.TestCase.Manifest.TimeoutSec,
                    node.NodeId,
                    suite.Identity,
                    planIdentity,
                    EngineVersion.Current);

                var attempts = 0;
                CaseRunResult caseResult;
                do
                {
                    attempts++;
                    caseResult = await _runner.RunCaseAsync(caseRequest, cancellationToken);
                }
                while ((caseResult.Status == RunStatus.Error || caseResult.Status == RunStatus.Timeout) && attempts <= suiteControls.RetryOnError);

                childRunIds.Add(caseResult.RunId);
                childStatuses.Add(caseResult.Status);
                AppendChild(childrenPath, BuildChildEntry(caseResult));
                IndexWriter.Append(_options.RunsRoot, BuildIndexEntry(caseResult, suite.Identity, planIdentity, groupRunId));

                if (!suiteControls.ContinueOnFailure && caseResult.Status != RunStatus.Passed)
                {
                    iteration = suiteControls.Repeat;
                    break;
                }
            }
        }

        var endTime = DateTimeOffset.UtcNow;
        var status = AggregateStatus(childStatuses);
        var groupResult = new GroupRunResult(
            groupRunId,
            "TestSuite",
            status,
            startTime,
            endTime,
            childRunIds,
            suite.Identity,
            planIdentity);

        JsonUtils.WriteFile(Path.Combine(groupFolder, "manifest.json"), new Dictionary<string, object?>
        {
            ["schemaVersion"] = SpecConstants.SchemaVersion,
            ["sourceManifest"] = suite.SourceManifest,
            ["resolvedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        });

        JsonUtils.WriteFile(Path.Combine(groupFolder, "controls.json"), suiteControls.ToJson());
        JsonUtils.WriteFile(Path.Combine(groupFolder, "environment.json"), new Dictionary<string, object?> { ["env"] = effectiveEnvironment });
        if (runRequestElement.HasValue)
        {
            JsonUtils.WriteJsonElementFile(Path.Combine(groupFolder, "runRequest.json"), runRequestElement.Value);
        }

        JsonUtils.WriteFile(Path.Combine(groupFolder, "result.json"), BuildGroupResultJson(groupResult));
        IndexWriter.Append(_options.RunsRoot, BuildGroupIndexEntry(groupResult, planRunId));

        return groupResult;
    }

    public async Task<GroupRunResult> RunPlanAsync(
        DiscoveryResult discovery,
        Identity planIdentity,
        RunRequest runRequest,
        CancellationToken cancellationToken)
    {
        if (!discovery.Plans.TryGetValue(planIdentity, out var plan))
        {
            throw new PcTestException(new[]
            {
                new PcTestError("RunRequest.ResolveFailed", $"Plan {planIdentity} not found.")
            });
        }

        var groupRunId = RunIdGenerator.NextGroupRunId(_options.RunsRoot);
        var groupFolder = Path.Combine(_options.RunsRoot, groupRunId);
        Directory.CreateDirectory(groupFolder);

        var planEnv = plan.Manifest.Environment;
        var effectiveEnvironment = EnvironmentResolver.Resolve(planEnv, null, runRequest.EnvironmentOverrides);
        var childRunIds = new List<string>();
        var childStatuses = new List<RunStatus>();
        var childrenPath = Path.Combine(groupFolder, "children.jsonl");
        var startTime = DateTimeOffset.UtcNow;

        foreach (var suiteRef in plan.Manifest.Suites)
        {
            var suiteIdentity = Identity.Parse(suiteRef);
            var suiteRunRequest = new RunRequest
            {
                Suite = suiteIdentity.ToString(),
                EnvironmentOverrides = runRequest.EnvironmentOverrides
            };

            var suiteResult = await RunSuiteAsync(discovery, suiteIdentity, suiteRunRequest, plan.Identity, groupRunId, planEnv, cancellationToken);
            childRunIds.Add(suiteResult.RunId);
            childStatuses.Add(suiteResult.Status);
            AppendChild(childrenPath, BuildPlanChildEntry(suiteResult));
        }

        var endTime = DateTimeOffset.UtcNow;
        var status = AggregateStatus(childStatuses);
        var groupResult = new GroupRunResult(groupRunId, "TestPlan", status, startTime, endTime, childRunIds, null, plan.Identity);

        JsonUtils.WriteFile(Path.Combine(groupFolder, "manifest.json"), new Dictionary<string, object?>
        {
            ["schemaVersion"] = SpecConstants.SchemaVersion,
            ["sourceManifest"] = plan.SourceManifest,
            ["resolvedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        });
        JsonUtils.WriteFile(Path.Combine(groupFolder, "environment.json"), new Dictionary<string, object?> { ["env"] = effectiveEnvironment });
        JsonUtils.WriteJsonElementFile(Path.Combine(groupFolder, "runRequest.json"), JsonUtils.ToJsonElement(runRequest));
        JsonUtils.WriteFile(Path.Combine(groupFolder, "result.json"), BuildGroupResultJson(groupResult));
        IndexWriter.Append(_options.RunsRoot, BuildGroupIndexEntry(groupResult, null));

        return groupResult;
    }

    private static void AppendChild(string path, object entry)
    {
        var json = JsonSerializer.Serialize(entry, JsonUtils.SerializerOptions);
        File.AppendAllText(path, json + Environment.NewLine);
    }

    private static void WriteEvent(string groupFolder, object entry)
    {
        var path = Path.Combine(groupFolder, "events.jsonl");
        var json = JsonSerializer.Serialize(entry, JsonUtils.SerializerOptions);
        File.AppendAllText(path, json + Environment.NewLine);
    }

    private static RunStatus AggregateStatus(IEnumerable<RunStatus> statuses)
    {
        var statusList = statuses.ToList();
        if (statusList.Contains(RunStatus.Aborted))
        {
            return RunStatus.Aborted;
        }

        if (statusList.Contains(RunStatus.Error))
        {
            return RunStatus.Error;
        }

        if (statusList.Contains(RunStatus.Timeout))
        {
            return RunStatus.Timeout;
        }

        if (statusList.Contains(RunStatus.Failed))
        {
            return RunStatus.Failed;
        }

        return RunStatus.Passed;
    }

    private static object BuildChildEntry(CaseRunResult result)
    {
        return new Dictionary<string, object?>
        {
            ["runId"] = result.RunId,
            ["nodeId"] = result.NodeId,
            ["testId"] = result.TestCaseIdentity.Id,
            ["testVersion"] = result.TestCaseIdentity.Version,
            ["status"] = result.Status.ToString()
        };
    }

    private static object BuildPlanChildEntry(GroupRunResult suiteResult)
    {
        return new Dictionary<string, object?>
        {
            ["runId"] = suiteResult.RunId,
            ["suiteId"] = suiteResult.SuiteIdentity?.Id,
            ["suiteVersion"] = suiteResult.SuiteIdentity?.Version,
            ["status"] = suiteResult.Status.ToString()
        };
    }

    private static object BuildGroupResultJson(GroupRunResult result)
    {
        var payload = new Dictionary<string, object?>
        {
            ["schemaVersion"] = SpecConstants.SchemaVersion,
            ["runType"] = result.RunType,
            ["status"] = result.Status.ToString(),
            ["startTime"] = result.StartTime.ToString("O", CultureInfo.InvariantCulture),
            ["endTime"] = result.EndTime.ToString("O", CultureInfo.InvariantCulture),
            ["childRunIds"] = result.ChildRunIds
        };

        if (result.RunType == "TestSuite" && result.SuiteIdentity != null)
        {
            payload["suiteId"] = result.SuiteIdentity.Id;
            payload["suiteVersion"] = result.SuiteIdentity.Version;
        }

        if (result.PlanIdentity != null)
        {
            payload["planId"] = result.PlanIdentity.Id;
            payload["planVersion"] = result.PlanIdentity.Version;
        }

        return payload;
    }

    private static object BuildIndexEntry(CaseRunResult result, Identity? suiteIdentity, Identity? planIdentity, string? parentRunId)
    {
        var payload = new Dictionary<string, object?>
        {
            ["runId"] = result.RunId,
            ["runType"] = "TestCase",
            ["testId"] = result.TestCaseIdentity.Id,
            ["testVersion"] = result.TestCaseIdentity.Version,
            ["startTime"] = result.StartTime.ToString("O", CultureInfo.InvariantCulture),
            ["endTime"] = result.EndTime.ToString("O", CultureInfo.InvariantCulture),
            ["status"] = result.Status.ToString()
        };

        if (!string.IsNullOrEmpty(result.NodeId))
        {
            payload["nodeId"] = result.NodeId;
        }

        if (suiteIdentity != null)
        {
            payload["suiteId"] = suiteIdentity.Id;
            payload["suiteVersion"] = suiteIdentity.Version;
            if (!string.IsNullOrEmpty(parentRunId))
            {
                payload["parentRunId"] = parentRunId;
            }
        }

        if (planIdentity != null)
        {
            payload["planId"] = planIdentity.Id;
            payload["planVersion"] = planIdentity.Version;
        }

        if (!payload.ContainsKey("parentRunId"))
        {
            payload.Remove("parentRunId");
        }
        return payload;
    }

    private static object BuildGroupIndexEntry(GroupRunResult result, string? parentRunId)
    {
        var payload = new Dictionary<string, object?>
        {
            ["runId"] = result.RunId,
            ["runType"] = result.RunType,
            ["status"] = result.Status.ToString(),
            ["startTime"] = result.StartTime.ToString("O", CultureInfo.InvariantCulture),
            ["endTime"] = result.EndTime.ToString("O", CultureInfo.InvariantCulture)
        };

        if (result.RunType == "TestSuite" && result.SuiteIdentity != null)
        {
            payload["suiteId"] = result.SuiteIdentity.Id;
            payload["suiteVersion"] = result.SuiteIdentity.Version;
        }

        if (result.RunType == "TestPlan" && result.PlanIdentity != null)
        {
            payload["planId"] = result.PlanIdentity.Id;
            payload["planVersion"] = result.PlanIdentity.Version;
        }

        if (result.PlanIdentity != null && result.RunType == "TestSuite")
        {
            payload["planId"] = result.PlanIdentity.Id;
            payload["planVersion"] = result.PlanIdentity.Version;
        }

        if (!string.IsNullOrEmpty(parentRunId))
        {
            payload["parentRunId"] = parentRunId;
        }

        return payload;
    }

    private static string? GetWorkingDir(JsonElement? environment)
    {
        if (environment == null || environment.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (environment.Value.TryGetProperty("workingDir", out var workingDirElement) && workingDirElement.ValueKind == JsonValueKind.String)
        {
            return workingDirElement.GetString();
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> BuildParameterTypes(TestCaseManifest manifest)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (manifest.Parameters == null)
        {
            return map;
        }

        foreach (var param in manifest.Parameters)
        {
            map[param.Name] = param.Type;
        }

        return map;
    }

    private static ResolvedSuiteNode ResolveSuiteNode(DiscoveryResult discovery, DiscoveredSuite suite, SuiteNode node)
    {
        var expectedRoot = PathUtils.ResolvePathWithLinks(PathUtils.NormalizePath(discovery.TestCaseRoot));
        var refPath = Path.Combine(expectedRoot, node.Ref, "test.manifest.json");
        var normalized = PathUtils.NormalizePath(refPath);
        var resolved = PathUtils.ResolvePathWithLinks(normalized);
        if (!PathUtils.IsContainedBy(expectedRoot, resolved))
        {
            ThrowSuiteRefInvalid(suite.ManifestPath, node.Ref, resolved, expectedRoot, "OutOfRoot");
        }

        if (!File.Exists(resolved))
        {
            var reason = Directory.Exists(Path.Combine(expectedRoot, node.Ref)) ? "MissingManifest" : "NotFound";
            ThrowSuiteRefInvalid(suite.ManifestPath, node.Ref, resolved, expectedRoot, reason);
        }

        var manifest = JsonUtils.ReadFile<TestCaseManifest>(resolved);
        var source = JsonUtils.ReadJsonElementFile(resolved);
        var identity = new Identity(manifest.Id, manifest.Version);
        if (!discovery.TestCases.TryGetValue(identity, out var discovered))
        {
            discovered = new DiscoveredTestCase
            {
                Identity = identity,
                Manifest = manifest,
                ManifestPath = resolved,
                FolderPath = Path.GetDirectoryName(resolved) ?? expectedRoot,
                SourceManifest = source
            };
        }

        return new ResolvedSuiteNode
        {
            Node = node,
            TestCase = discovered,
            ResolvedRef = resolved
        };
    }

    private static void ValidateNodeOverrides(SuiteManifest manifest, Dictionary<string, NodeOverride>? overrides)
    {
        if (overrides == null)
        {
            return;
        }

        var knownNodes = new HashSet<string>(manifest.TestCases.Select(n => n.NodeId), StringComparer.Ordinal);
        foreach (var nodeId in overrides.Keys)
        {
            if (!knownNodes.Contains(nodeId))
            {
                throw new PcTestException(new[]
                {
                    new PcTestError("RunRequest.Invalid", $"Unknown nodeId {nodeId} in nodeOverrides.")
                });
            }
        }
    }

    private static void ThrowSuiteRefInvalid(string suitePath, string reference, string resolvedPath, string expectedRoot, string reason)
    {
        var payload = new Dictionary<string, object?>
        {
            ["entityType"] = "TestSuite",
            ["suitePath"] = suitePath,
            ["ref"] = reference,
            ["resolvedPath"] = resolvedPath,
            ["expectedRoot"] = expectedRoot,
            ["reason"] = reason
        };

        throw new PcTestException(new[]
        {
            new PcTestError(SpecConstants.SuiteTestCaseRefInvalid, "Suite test case ref invalid.", JsonUtils.ToJsonElement(payload))
        });
    }

    private sealed class ResolvedSuiteNode
    {
        public required SuiteNode Node { get; init; }
        public required DiscoveredTestCase TestCase { get; init; }
        public required string ResolvedRef { get; init; }
    }
}

internal static class EngineVersion
{
    public const string Current = "MVP";
}

internal static class RunIdGenerator
{
    public static string NextRunId(string root, string prefix)
    {
        while (true)
        {
            var id = $"{prefix}{Guid.NewGuid():N}";
            var path = Path.Combine(root, id);
            if (!Directory.Exists(path))
            {
                return id;
            }
        }
    }

    public static string NextGroupRunId(string root) => NextRunId(root, "G-");
}

internal static class IndexWriter
{
    private static readonly object LockObj = new();

    public static void Append(string runsRoot, object entry)
    {
        var path = Path.Combine(runsRoot, "index.jsonl");
        var json = JsonSerializer.Serialize(entry, JsonUtils.SerializerOptions);
        lock (LockObj)
        {
            File.AppendAllText(path, json + Environment.NewLine);
        }
    }
}

internal sealed class SuiteControls
{
    public int Repeat { get; init; } = 1;
    public int MaxParallel { get; init; } = 1;
    public bool ContinueOnFailure { get; init; }
    public int RetryOnError { get; init; }

    public static SuiteControls FromManifest(JsonElement? controls)
    {
        if (controls == null || controls.Value.ValueKind != JsonValueKind.Object)
        {
            return new SuiteControls();
        }

        var repeat = GetInt(controls.Value, "repeat") ?? 1;
        var maxParallel = GetInt(controls.Value, "maxParallel") ?? 1;
        var continueOnFailure = GetBool(controls.Value, "continueOnFailure") ?? false;
        var retryOnError = GetInt(controls.Value, "retryOnError") ?? 0;

        return new SuiteControls
        {
            Repeat = repeat,
            MaxParallel = maxParallel,
            ContinueOnFailure = continueOnFailure,
            RetryOnError = retryOnError
        };
    }

    public object ToJson() => new Dictionary<string, object?>
    {
        ["repeat"] = Repeat,
        ["maxParallel"] = MaxParallel,
        ["continueOnFailure"] = ContinueOnFailure,
        ["retryOnError"] = RetryOnError
    };

    private static int? GetInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
    }

    private static bool? GetBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : null;
    }
}

internal static class RunRequestValidator
{
    public static void Validate(RunRequest request)
    {
        var selected = new[] { request.Suite, request.TestCase, request.Plan }.Count(value => !string.IsNullOrEmpty(value));
        if (selected != 1)
        {
            throw new PcTestException(new[]
            {
                new PcTestError("RunRequest.Invalid", "RunRequest must specify exactly one of suite, testCase, or plan.")
            });
        }

        if (!string.IsNullOrEmpty(request.Plan))
        {
            if (request.NodeOverrides != null || request.CaseInputs != null)
            {
                throw new PcTestException(new[]
                {
                    new PcTestError("RunRequest.Invalid", "Plan RunRequest cannot include nodeOverrides or caseInputs.")
                });
            }
        }

        if (!string.IsNullOrEmpty(request.TestCase) && request.NodeOverrides != null)
        {
            throw new PcTestException(new[]
            {
                new PcTestError("RunRequest.Invalid", "TestCase RunRequest cannot include nodeOverrides.")
            });
        }

        if (!string.IsNullOrEmpty(request.Suite) && request.CaseInputs != null)
        {
            throw new PcTestException(new[]
            {
                new PcTestError("RunRequest.Invalid", "Suite RunRequest cannot include caseInputs.")
            });
        }
    }
}
