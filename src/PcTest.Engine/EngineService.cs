using System.Text;
using System.Text.Json;
using PcTest.Contracts;
using PcTest.Runner;

namespace PcTest.Engine;

public sealed class EngineService
{
    private readonly DiscoveryService _discoveryService = new();
    private readonly InputResolver _inputResolver = new();
    private readonly RunnerService _runnerService = new();

    public ValidationResult<DiscoveryResult> Discover(string testCaseRoot, string suiteRoot, string planRoot)
        => _discoveryService.Discover(testCaseRoot, suiteRoot, planRoot);

    public async Task<ValidationResult<string>> RunAsync(
        DiscoveryResult discovery,
        RunRequest request,
        string runsRoot,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(runsRoot))
        {
            Directory.CreateDirectory(runsRoot);
        }

        if (!ValidateRunRequest(request, out ValidationError? error))
        {
            return ValidationResult<string>.Failure(error ?? new ValidationError("RunRequest.Invalid", "Invalid run request."));
        }

        if (!string.IsNullOrWhiteSpace(request.TestCase))
        {
            return await RunStandaloneTestCaseAsync(discovery, request, runsRoot, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(request.Suite))
        {
            ValidationResult<SuiteRunResult> suiteResult = await RunSuiteAsync(discovery, request, runsRoot, null, null, cancellationToken).ConfigureAwait(false);
            if (!suiteResult.IsSuccess)
            {
                return ValidationResult<string>.Failure(suiteResult.Errors);
            }

            return ValidationResult<string>.Success(suiteResult.Value!.RunId);
        }

        if (!string.IsNullOrWhiteSpace(request.Plan))
        {
            return await RunPlanAsync(discovery, request, runsRoot, cancellationToken).ConfigureAwait(false);
        }

        return ValidationResult<string>.Failure(new ValidationError("RunRequest.Invalid", "RunRequest must specify suite, plan, or testCase."));
    }

    private static bool ValidateRunRequest(RunRequest request, out ValidationError? error)
    {
        error = null;
        int count = 0;
        if (!string.IsNullOrWhiteSpace(request.Suite)) count++;
        if (!string.IsNullOrWhiteSpace(request.Plan)) count++;
        if (!string.IsNullOrWhiteSpace(request.TestCase)) count++;
        if (count != 1)
        {
            error = new ValidationError("RunRequest.Invalid", "RunRequest must specify exactly one of suite, plan, or testCase.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.Plan))
        {
            if (request.NodeOverrides is not null || request.CaseInputs is not null)
            {
                error = new ValidationError("RunRequest.Invalid", "Plan RunRequest cannot include nodeOverrides or caseInputs.");
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.TestCase) && request.NodeOverrides is not null)
        {
            error = new ValidationError("RunRequest.Invalid", "Standalone TestCase RunRequest cannot include nodeOverrides.");
            return false;
        }

        if (request.EnvironmentOverrides?.Extra is not null && request.EnvironmentOverrides.Extra.Count > 0)
        {
            error = new ValidationError("RunRequest.Environment.Invalid", "Environment overrides contain unsupported keys.");
            return false;
        }

        if (request.EnvironmentOverrides?.Env is not null)
        {
            foreach (KeyValuePair<string, string> pair in request.EnvironmentOverrides.Env)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    error = new ValidationError("RunRequest.Environment.Invalid", "Environment override key is empty.");
                    return false;
                }
            }
        }

        return true;
    }

    private async Task<ValidationResult<string>> RunStandaloneTestCaseAsync(
        DiscoveryResult discovery,
        RunRequest request,
        string runsRoot,
        CancellationToken cancellationToken)
    {
        if (!Identity.TryParse(request.TestCase, out Identity identity, out string? error))
        {
            return ValidationResult<string>.Failure(new ValidationError("RunRequest.Identity.Invalid", error ?? "Invalid identity."));
        }

        if (!discovery.TestCases.TryGetValue(identity, out ManifestRecord<TestCaseManifest>? testCase))
        {
            return ValidationResult<string>.Failure(new ValidationError("RunRequest.Identity.NotFound", "TestCase identity not found.", new Dictionary<string, object?>
            {
                ["entityType"] = "TestCase",
                ["id"] = identity.Id,
                ["version"] = identity.Version,
                ["reason"] = "NotFound"
            }));
        }

        Dictionary<string, string>? envOverride = request.EnvironmentOverrides?.Env;
        Dictionary<string, string> effectiveEnv = EnvironmentResolver.BuildEffectiveEnvironment(null, null, envOverride);

        ValidationResult<ResolvedInputs> resolved = _inputResolver.Resolve(
            testCase.Manifest.Parameters,
            request.CaseInputs,
            effectiveEnv,
            nodeId: null);
        if (!resolved.IsSuccess)
        {
            return ValidationResult<string>.Failure(resolved.Errors);
        }

        RunnerResult result = await _runnerService.RunTestCaseAsync(new RunnerRequest
        {
            RunsRoot = runsRoot,
            TestCasePath = Path.GetDirectoryName(testCase.Path) ?? discovery.TestCaseRoot,
            Manifest = testCase.Manifest,
            ResolvedRef = testCase.Path,
            Identity = testCase.Identity,
            EffectiveEnvironment = effectiveEnv,
            EffectiveInputs = resolved.Value!.ResolvedValues,
            EffectiveInputsJson = resolved.Value!.ResolvedJson,
            InputTemplates = resolved.Value!.InputTemplates,
            SecretInputs = resolved.Value!.SecretInputs,
            TimeoutSec = testCase.Manifest.TimeoutSec
        }, cancellationToken).ConfigureAwait(false);

        await AppendIndexAsync(runsRoot, new IndexEntry
        {
            RunId = result.RunId,
            RunType = "TestCase",
            TestId = testCase.Identity.Id,
            TestVersion = testCase.Identity.Version,
            StartTime = result.StartTime,
            EndTime = result.EndTime,
            Status = result.Status.ToString()
        }, cancellationToken).ConfigureAwait(false);

        return ValidationResult<string>.Success(result.RunId);
    }

    private async Task<ValidationResult<SuiteRunResult>> RunSuiteAsync(
        DiscoveryResult discovery,
        RunRequest request,
        string runsRoot,
        Identity? planIdentity,
        string? planRunId,
        CancellationToken cancellationToken)
    {
        if (!Identity.TryParse(request.Suite, out Identity suiteIdentity, out string? error))
        {
            return ValidationResult<SuiteRunResult>.Failure(new ValidationError("RunRequest.Identity.Invalid", error ?? "Invalid identity."));
        }

        if (!discovery.Suites.TryGetValue(suiteIdentity, out ManifestRecord<SuiteManifest>? suite))
        {
            return ValidationResult<SuiteRunResult>.Failure(new ValidationError("RunRequest.Identity.NotFound", "Suite identity not found.", new Dictionary<string, object?>
            {
                ["entityType"] = "suite",
                ["id"] = suiteIdentity.Id,
                ["version"] = suiteIdentity.Version,
                ["reason"] = "NotFound"
            }));
        }

        if (request.NodeOverrides is not null)
        {
            HashSet<string> nodeIds = suite.Manifest.TestCases.Select(node => node.NodeId).ToHashSet(StringComparer.Ordinal);
            foreach (string nodeId in request.NodeOverrides.Keys)
            {
                if (!nodeIds.Contains(nodeId))
                {
                    return ValidationResult<SuiteRunResult>.Failure(new ValidationError("RunRequest.NodeOverride.Unknown", $"Unknown nodeId {nodeId}.", new Dictionary<string, object?>
                    {
                        ["nodeId"] = nodeId
                    }));
                }
            }
        }

        string groupRunId = GenerateGroupRunId(runsRoot);
        string groupFolder = Path.Combine(runsRoot, groupRunId);
        Directory.CreateDirectory(groupFolder);

        SuiteControls controls = SuiteControls.FromElement(suite.Manifest.Controls);
        if (controls.MaxParallel > 1)
        {
            await WriteEventAsync(groupFolder, new { code = "Controls.MaxParallel.Ignored", location = "suite.manifest.json", message = $"maxParallel {controls.MaxParallel} ignored." }, cancellationToken).ConfigureAwait(false);
        }

        await JsonHelpers.WriteJsonFileAsync(Path.Combine(groupFolder, "manifest.json"), new { sourceManifest = suite.Manifest }, cancellationToken).ConfigureAwait(false);
        await JsonHelpers.WriteJsonFileAsync(Path.Combine(groupFolder, "controls.json"), controls, cancellationToken).ConfigureAwait(false);
        if (request.EnvironmentOverrides is not null || request.NodeOverrides is not null)
        {
            await JsonHelpers.WriteJsonFileAsync(Path.Combine(groupFolder, "runRequest.json"), request, cancellationToken).ConfigureAwait(false);
        }

        DateTimeOffset startTime = DateTimeOffset.UtcNow;
        List<string> childRunIds = new();
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        RunStatus? finalStatus = null;

        Dictionary<string, string>? planEnv = null;
        if (planIdentity is not null && discovery.Plans.TryGetValue(planIdentity.Value, out ManifestRecord<PlanManifest>? planRecord))
        {
            planEnv = planRecord.Manifest.Environment?.Env;
        }

        Dictionary<string, string>? suiteEnv = suite.Manifest.Environment?.Env;
        Dictionary<string, string>? runEnvOverride = request.EnvironmentOverrides?.Env;
        Dictionary<string, string> effectiveEnv = EnvironmentResolver.BuildEffectiveEnvironment(suiteEnv, planEnv, runEnvOverride);
        Dictionary<string, string> injectionEnv = BuildEnvironmentInjection(suiteEnv, planEnv, runEnvOverride);
        await JsonHelpers.WriteJsonFileAsync(Path.Combine(groupFolder, "environment.json"), new { env = injectionEnv, workingDir = suite.Manifest.Environment?.WorkingDir }, cancellationToken).ConfigureAwait(false);

        string childrenPath = Path.Combine(groupFolder, "children.jsonl");
        await using FileStream childrenStream = new(childrenPath, FileMode.Create, FileAccess.Write, FileShare.None);

        bool stopExecution = false;
        for (int iteration = 0; iteration < controls.Repeat; iteration++)
        {
            foreach (SuiteTestCaseNode node in suite.Manifest.TestCases)
            {
                if (finalStatus is RunStatus.Aborted or RunStatus.Timeout or RunStatus.Error or RunStatus.Failed)
                {
                    if (!controls.ContinueOnFailure)
                    {
                        stopExecution = true;
                        break;
                    }
                }

                RunNodeOverride? overrideNode = null;
                if (request.NodeOverrides is not null && request.NodeOverrides.TryGetValue(node.NodeId, out RunNodeOverride? overrideValue))
                {
                    overrideNode = overrideValue;
                }

                if (!discovery.TestCases.TryGetValue(ResolveSuiteNodeIdentity(discovery.TestCaseRoot, node.Ref), out ManifestRecord<TestCaseManifest>? testCase))
                {
                    return ValidationResult<SuiteRunResult>.Failure(new ValidationError("Suite.TestCaseRef.Invalid", "Suite ref did not resolve to a test case.", new Dictionary<string, object?>
                    {
                        ["suitePath"] = suite.Path,
                        ["ref"] = node.Ref
                    }));
                }

                Dictionary<string, JsonElement> mergedInputs = MergeInputs(node.Inputs, overrideNode?.Inputs);
                ValidationResult<ResolvedInputs> resolved = _inputResolver.Resolve(testCase.Manifest.Parameters, mergedInputs, effectiveEnv, node.NodeId);
                if (!resolved.IsSuccess)
                {
                    return ValidationResult<SuiteRunResult>.Failure(resolved.Errors);
                }

                int attempts = 0;
                RunnerResult? nodeResult = null;
                do
                {
                    attempts++;
                    nodeResult = await _runnerService.RunTestCaseAsync(new RunnerRequest
                    {
                        RunsRoot = runsRoot,
                        TestCasePath = Path.GetDirectoryName(testCase.Path) ?? discovery.TestCaseRoot,
                        Manifest = testCase.Manifest,
                        ResolvedRef = testCase.Path,
                        Identity = testCase.Identity,
                        EffectiveEnvironment = effectiveEnv,
                        EffectiveInputs = resolved.Value!.ResolvedValues,
                        EffectiveInputsJson = resolved.Value!.ResolvedJson,
                        InputTemplates = resolved.Value!.InputTemplates,
                        SecretInputs = resolved.Value!.SecretInputs,
                        NodeId = node.NodeId,
                        SuiteId = suiteIdentity.Id,
                        SuiteVersion = suiteIdentity.Version,
                        PlanId = planIdentity?.Id,
                        PlanVersion = planIdentity?.Version,
                        WorkingDir = suite.Manifest.Environment?.WorkingDir,
                        TimeoutSec = testCase.Manifest.TimeoutSec
                    }, cancellationToken).ConfigureAwait(false);

                    if (nodeResult.Status is RunStatus.Error or RunStatus.Timeout && attempts <= controls.RetryOnError)
                    {
                        await WriteEventAsync(groupFolder, new { code = "Controls.Retry", nodeId = node.NodeId, attempt = attempts + 1 }, cancellationToken).ConfigureAwait(false);
                    }
                }
                while (nodeResult.Status is RunStatus.Error or RunStatus.Timeout && attempts <= controls.RetryOnError);

                childRunIds.Add(nodeResult.RunId);
                await WriteJsonLineAsync(childrenStream, new
                {
                    runId = nodeResult.RunId,
                    nodeId = node.NodeId,
                    testId = testCase.Identity.Id,
                    testVersion = testCase.Identity.Version,
                    status = nodeResult.Status.ToString()
                }, cancellationToken).ConfigureAwait(false);

                await AppendIndexAsync(runsRoot, new IndexEntry
                {
                    RunId = nodeResult.RunId,
                    RunType = "TestCase",
                    NodeId = node.NodeId,
                    TestId = testCase.Identity.Id,
                    TestVersion = testCase.Identity.Version,
                    SuiteId = suiteIdentity.Id,
                    SuiteVersion = suiteIdentity.Version,
                    PlanId = planIdentity?.Id,
                    PlanVersion = planIdentity?.Version,
                    ParentRunId = groupRunId,
                    StartTime = nodeResult.StartTime,
                    EndTime = nodeResult.EndTime,
                    Status = nodeResult.Status.ToString()
                }, cancellationToken).ConfigureAwait(false);

                finalStatus = AggregateStatus(finalStatus, nodeResult.Status);
                IncrementCounts(counts, nodeResult.Status.ToString());
            }

            if (stopExecution)
            {
                break;
            }
        }

        DateTimeOffset endTime = DateTimeOffset.UtcNow;
        RunStatus summaryStatus = finalStatus ?? RunStatus.Passed;
        SummaryResult summary = new()
        {
            SchemaVersion = "1.5.0",
            RunType = "TestSuite",
            SuiteId = suiteIdentity.Id,
            SuiteVersion = suiteIdentity.Version,
            PlanId = planIdentity?.Id,
            PlanVersion = planIdentity?.Version,
            Status = summaryStatus.ToString(),
            StartTime = startTime.ToString("O"),
            EndTime = endTime.ToString("O"),
            Counts = counts,
            ChildRunIds = childRunIds.ToArray()
        };

        await JsonHelpers.WriteJsonFileAsync(Path.Combine(groupFolder, "result.json"), summary, cancellationToken).ConfigureAwait(false);

        await AppendIndexAsync(runsRoot, new IndexEntry
        {
            RunId = groupRunId,
            RunType = "TestSuite",
            SuiteId = suiteIdentity.Id,
            SuiteVersion = suiteIdentity.Version,
            PlanId = planIdentity?.Id,
            PlanVersion = planIdentity?.Version,
            ParentRunId = planRunId,
            StartTime = startTime,
            EndTime = endTime,
            Status = summaryStatus.ToString()
        }, cancellationToken).ConfigureAwait(false);

        return ValidationResult<SuiteRunResult>.Success(new SuiteRunResult
        {
            RunId = groupRunId,
            Status = summaryStatus
        });
    }

    private async Task<ValidationResult<string>> RunPlanAsync(
        DiscoveryResult discovery,
        RunRequest request,
        string runsRoot,
        CancellationToken cancellationToken)
    {
        if (!Identity.TryParse(request.Plan, out Identity planIdentity, out string? error))
        {
            return ValidationResult<string>.Failure(new ValidationError("RunRequest.Identity.Invalid", error ?? "Invalid identity."));
        }

        if (!discovery.Plans.TryGetValue(planIdentity, out ManifestRecord<PlanManifest>? plan))
        {
            return ValidationResult<string>.Failure(new ValidationError("RunRequest.Identity.NotFound", "Plan identity not found.", new Dictionary<string, object?>
            {
                ["entityType"] = "plan",
                ["id"] = planIdentity.Id,
                ["version"] = planIdentity.Version,
                ["reason"] = "NotFound"
            }));
        }

        string groupRunId = GenerateGroupRunId(runsRoot);
        string groupFolder = Path.Combine(runsRoot, groupRunId);
        Directory.CreateDirectory(groupFolder);

        await JsonHelpers.WriteJsonFileAsync(Path.Combine(groupFolder, "manifest.json"), new { sourceManifest = plan.Manifest }, cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> planInjection = BuildEnvironmentInjection(null, plan.Manifest.Environment?.Env, request.EnvironmentOverrides?.Env);
        await JsonHelpers.WriteJsonFileAsync(Path.Combine(groupFolder, "environment.json"), new { env = planInjection }, cancellationToken).ConfigureAwait(false);
        if (request.EnvironmentOverrides is not null)
        {
            await JsonHelpers.WriteJsonFileAsync(Path.Combine(groupFolder, "runRequest.json"), request, cancellationToken).ConfigureAwait(false);
        }

        DateTimeOffset startTime = DateTimeOffset.UtcNow;
        List<string> childRunIds = new();
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        RunStatus? finalStatus = null;

        string childrenPath = Path.Combine(groupFolder, "children.jsonl");
        await using FileStream childrenStream = new(childrenPath, FileMode.Create, FileAccess.Write, FileShare.None);

        foreach (string suiteRef in plan.Manifest.Suites)
        {
            RunRequest suiteRequest = new()
            {
                Suite = suiteRef,
                EnvironmentOverrides = request.EnvironmentOverrides
            };

            ValidationResult<SuiteRunResult> suiteRun = await RunSuiteAsync(discovery, suiteRequest, runsRoot, planIdentity, groupRunId, cancellationToken).ConfigureAwait(false);
            if (!suiteRun.IsSuccess)
            {
                return ValidationResult<string>.Failure(suiteRun.Errors);
            }

            SuiteRunResult suiteRunResult = suiteRun.Value!;
            string suiteRunId = suiteRunResult.RunId;
            childRunIds.Add(suiteRunId);

            await WriteJsonLineAsync(childrenStream, new
            {
                runId = suiteRunId,
                suiteId = suiteRef.Split('@')[0],
                suiteVersion = suiteRef.Split('@')[1],
                status = suiteRunResult.Status.ToString()
            }, cancellationToken).ConfigureAwait(false);

            finalStatus = AggregateStatus(finalStatus, suiteRunResult.Status);
            IncrementCounts(counts, suiteRunResult.Status.ToString());
        }

        DateTimeOffset endTime = DateTimeOffset.UtcNow;
        RunStatus summaryStatus = finalStatus ?? RunStatus.Passed;
        SummaryResult summary = new()
        {
            SchemaVersion = "1.5.0",
            RunType = "TestPlan",
            PlanId = planIdentity.Id,
            PlanVersion = planIdentity.Version,
            Status = summaryStatus.ToString(),
            StartTime = startTime.ToString("O"),
            EndTime = endTime.ToString("O"),
            Counts = counts,
            ChildRunIds = childRunIds.ToArray()
        };

        await JsonHelpers.WriteJsonFileAsync(Path.Combine(groupFolder, "result.json"), summary, cancellationToken).ConfigureAwait(false);

        await AppendIndexAsync(runsRoot, new IndexEntry
        {
            RunId = groupRunId,
            RunType = "TestPlan",
            PlanId = planIdentity.Id,
            PlanVersion = planIdentity.Version,
            StartTime = startTime,
            EndTime = endTime,
            Status = summaryStatus.ToString()
        }, cancellationToken).ConfigureAwait(false);

        return ValidationResult<string>.Success(groupRunId);
    }

    private static Dictionary<string, JsonElement> MergeInputs(
        Dictionary<string, JsonElement>? suiteInputs,
        Dictionary<string, JsonElement>? overrideInputs)
    {
        Dictionary<string, JsonElement> merged = new(StringComparer.Ordinal);
        if (suiteInputs is not null)
        {
            foreach (KeyValuePair<string, JsonElement> pair in suiteInputs)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        if (overrideInputs is not null)
        {
            foreach (KeyValuePair<string, JsonElement> pair in overrideInputs)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        return merged;
    }

    private static Identity ResolveSuiteNodeIdentity(string testCaseRoot, string suiteRef)
    {
        string resolvedPath = Path.GetFullPath(Path.Combine(testCaseRoot, suiteRef));
        string manifestPath = Path.Combine(resolvedPath, "test.manifest.json");
        TestCaseManifest manifest = JsonHelpers.ReadJsonFile<TestCaseManifest>(manifestPath);
        return new Identity(manifest.Id, manifest.Version);
    }

    private static string GenerateGroupRunId(string runsRoot)
    {
        string id;
        do
        {
            id = $"G-{Guid.NewGuid():N}";
        }
        while (Directory.Exists(Path.Combine(runsRoot, id)));
        return id;
    }

    private static RunStatus AggregateStatus(RunStatus? current, RunStatus next)
    {
        if (current is null)
        {
            return next;
        }

        int severity = GetSeverity(current.Value);
        int nextSeverity = GetSeverity(next);
        return nextSeverity > severity ? next : current.Value;
    }

    private static int GetSeverity(RunStatus status)
    {
        return status switch
        {
            RunStatus.Error => 4,
            RunStatus.Timeout => 3,
            RunStatus.Failed => 2,
            RunStatus.Aborted => 5,
            _ => 1
        };
    }

    private static void IncrementCounts(Dictionary<string, int> counts, string status)
    {
        if (!counts.TryGetValue(status, out int current))
        {
            counts[status] = 1;
            return;
        }

        counts[status] = current + 1;
    }

    private static Dictionary<string, string> BuildEnvironmentInjection(
        Dictionary<string, string>? suiteEnv,
        Dictionary<string, string>? planEnv,
        Dictionary<string, string>? runOverride)
    {
        Dictionary<string, string> env = new(StringComparer.OrdinalIgnoreCase);
        if (suiteEnv is not null)
        {
            foreach (KeyValuePair<string, string> pair in suiteEnv)
            {
                env[pair.Key] = pair.Value;
            }
        }

        if (planEnv is not null)
        {
            foreach (KeyValuePair<string, string> pair in planEnv)
            {
                env[pair.Key] = pair.Value;
            }
        }

        if (runOverride is not null)
        {
            foreach (KeyValuePair<string, string> pair in runOverride)
            {
                env[pair.Key] = pair.Value;
            }
        }

        return env;
    }

    private static async Task WriteEventAsync(string groupFolder, object evt, CancellationToken cancellationToken)
    {
        string path = Path.Combine(groupFolder, "events.jsonl");
        await using FileStream stream = new(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        string json = JsonSerializer.Serialize(evt, JsonHelpers.SerializerOptions);
        byte[] line = Encoding.UTF8.GetBytes(json + "\n");
        await stream.WriteAsync(line, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteJsonLineAsync(FileStream stream, object payload, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(payload, JsonHelpers.SerializerOptions);
        byte[] line = Encoding.UTF8.GetBytes(json + "\n");
        await stream.WriteAsync(line, cancellationToken).ConfigureAwait(false);
    }

    private static async Task AppendIndexAsync(string runsRoot, IndexEntry entry, CancellationToken cancellationToken)
    {
        string path = Path.Combine(runsRoot, "index.jsonl");
        await using FileStream stream = new(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        string json = JsonSerializer.Serialize(entry.ToPayload(), JsonHelpers.SerializerOptions);
        byte[] line = Encoding.UTF8.GetBytes(json + "\n");
        await stream.WriteAsync(line, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class SuiteControls
{
    public int Repeat { get; init; } = 1;
    public int MaxParallel { get; init; } = 1;
    public bool ContinueOnFailure { get; init; }
    public int RetryOnError { get; init; }

    public static SuiteControls FromElement(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind != JsonValueKind.Object)
        {
            return new SuiteControls();
        }

        int repeat = 1;
        int maxParallel = 1;
        bool continueOnFailure = false;
        int retryOnError = 0;

        JsonElement value = element.Value;
        if (value.TryGetProperty("repeat", out JsonElement repeatElement) && repeatElement.TryGetInt32(out int repeatValue))
        {
            repeat = Math.Max(1, repeatValue);
        }

        if (value.TryGetProperty("maxParallel", out JsonElement maxParallelElement) && maxParallelElement.TryGetInt32(out int maxParallelValue))
        {
            maxParallel = Math.Max(1, maxParallelValue);
        }

        if (value.TryGetProperty("continueOnFailure", out JsonElement continueElement) && continueElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            continueOnFailure = continueElement.GetBoolean();
        }

        if (value.TryGetProperty("retryOnError", out JsonElement retryElement) && retryElement.TryGetInt32(out int retryValue))
        {
            retryOnError = Math.Max(0, retryValue);
        }

        return new SuiteControls
        {
            Repeat = repeat,
            MaxParallel = maxParallel,
            ContinueOnFailure = continueOnFailure,
            RetryOnError = retryOnError
        };
    }
}

public sealed class SuiteRunResult
{
    public required string RunId { get; init; }
    public required RunStatus Status { get; init; }
}

public readonly struct IndexEntry
{
    public string RunId { get; init; }
    public string RunType { get; init; }
    public string? NodeId { get; init; }
    public string? TestId { get; init; }
    public string? TestVersion { get; init; }
    public string? SuiteId { get; init; }
    public string? SuiteVersion { get; init; }
    public string? PlanId { get; init; }
    public string? PlanVersion { get; init; }
    public string? ParentRunId { get; init; }
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }
    public string Status { get; init; }

    public object ToPayload()
    {
        Dictionary<string, object?> payload = new(StringComparer.Ordinal)
        {
            ["runId"] = RunId,
            ["runType"] = RunType,
            ["startTime"] = StartTime.ToString("O"),
            ["endTime"] = EndTime.ToString("O"),
            ["status"] = Status
        };

        if (!string.IsNullOrEmpty(NodeId)) payload["nodeId"] = NodeId;
        if (!string.IsNullOrEmpty(TestId)) payload["testId"] = TestId;
        if (!string.IsNullOrEmpty(TestVersion)) payload["testVersion"] = TestVersion;
        if (!string.IsNullOrEmpty(SuiteId)) payload["suiteId"] = SuiteId;
        if (!string.IsNullOrEmpty(SuiteVersion)) payload["suiteVersion"] = SuiteVersion;
        if (!string.IsNullOrEmpty(PlanId)) payload["planId"] = PlanId;
        if (!string.IsNullOrEmpty(PlanVersion)) payload["planVersion"] = PlanVersion;
        if (!string.IsNullOrEmpty(ParentRunId)) payload["parentRunId"] = ParentRunId;

        return payload;
    }
}
