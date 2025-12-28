using System.Globalization;
using System.Text.Json;
using PcTest.Contracts;
using PcTest.Runner;

namespace PcTest.Engine;

public sealed class EngineService
{
    private readonly RunnerService _runner;

    public EngineService(RunnerService runner)
    {
        _runner = runner;
    }

    public DiscoveryResult Discover(DiscoveryOptions options)
    {
        return new DiscoveryService().Discover(options);
    }

    public async Task<CaseRunResult> RunStandaloneTestCaseAsync(
        ResolvedTestCase testCase,
        RunRequest request,
        RunConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = ValidateRunRequest(request);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException("RunRequest validation failed.");
        }

        Dictionary<string, JsonElement> defaults = InputResolver.ExtractDefaults(testCase.Manifest.Parameters);
        Dictionary<string, string> osEnvironment = EnvironmentResolver.SnapshotOsEnvironment();
        Dictionary<string, string> effectiveEnv = EnvironmentResolver.ResolveEffectiveEnvironment(osEnvironment, null, null, request.EnvironmentOverrides?.Env);

        EffectiveInputsResult inputs = InputResolver.ResolveEffectiveInputs(
            testCase.Manifest.Parameters ?? new List<ParameterDefinition>(),
            defaults,
            null,
            request.CaseInputs,
            effectiveEnv,
            out ValidationResult inputValidation);
        if (!inputValidation.IsValid)
        {
            throw new InvalidOperationException("Input validation failed.");
        }

        using JsonDocument manifestDoc = JsonFile.ReadDocument(testCase.ManifestPath);
        CaseRunRequest caseRequest = new()
        {
            RunsRoot = configuration.RunsRoot,
            ManifestPath = testCase.ManifestPath,
            ScriptPath = Path.Combine(Path.GetDirectoryName(testCase.ManifestPath) ?? string.Empty, "run.ps1"),
            TestId = testCase.Identity.Id,
            TestVersion = testCase.Identity.Version,
            EffectiveInputs = inputs.Inputs,
            RedactedInputs = inputs.RedactedInputs,
            SecretKeys = inputs.SecretKeys,
            EffectiveEnvironment = effectiveEnv,
            SourceManifest = manifestDoc.RootElement.Clone(),
            Parameters = BuildParameterSnapshot(testCase.Manifest.Parameters),
            PwshPath = configuration.PwshPath,
            TimeoutSec = testCase.Manifest.TimeoutSec
        };

        CaseRunResult result = await _runner.RunAsync(caseRequest, cancellationToken);
        await IndexWriter.AppendTestCaseAsync(configuration.RunsRoot, result, caseRequest, null, null);
        return result;
    }

    public async Task<SuiteExecutionResult> RunSuiteAsync(
        ResolvedTestSuite suite,
        IReadOnlyList<ResolvedTestCase> testCases,
        RunRequest request,
        RunConfiguration configuration,
        Identity? planIdentity,
        Dictionary<string, string>? planEnvironment,
        Dictionary<string, string>? planOverrides,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = ValidateRunRequest(request);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException("RunRequest validation failed.");
        }

        string groupRunId = RunIdGenerator.CreateRunId("G");
        string groupFolder = Path.Combine(configuration.RunsRoot, groupRunId);
        while (Directory.Exists(groupFolder))
        {
            groupRunId = RunIdGenerator.CreateRunId("G");
            groupFolder = Path.Combine(configuration.RunsRoot, groupRunId);
        }

        Directory.CreateDirectory(groupFolder);

        List<string> childRunIds = new();
        DateTimeOffset start = DateTimeOffset.UtcNow;

        Dictionary<string, string> osEnv = EnvironmentResolver.SnapshotOsEnvironment();
        Dictionary<string, string> effectiveEnv = EnvironmentResolver.ResolveEffectiveEnvironment(osEnv, suite.Manifest.Environment?.Env, planEnvironment, planOverrides ?? request.EnvironmentOverrides?.Env);

        WriteGroupArtifacts(groupFolder, suite.Manifest, suite.ManifestPath, suite.Manifest.Controls, effectiveEnv, request);

        if (suite.Manifest.Controls?.MaxParallel is int maxParallel && maxParallel > 1)
        {
            await AppendEventAsync(groupFolder, new
            {
                time = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                code = "Controls.MaxParallel.Ignored",
                location = "suite.manifest.json",
                message = $"maxParallel={maxParallel} was ignored; execution is sequential."
            });
        }

        if (request.NodeOverrides is not null)
        {
            HashSet<string> nodeIds = suite.Manifest.TestCases.Select(node => node.NodeId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string nodeId in request.NodeOverrides.Keys)
            {
                if (!nodeIds.Contains(nodeId))
                {
                    throw new InvalidOperationException($"Unknown nodeId '{nodeId}'.");
                }
            }
        }

        int repeat = suite.Manifest.Controls?.Repeat ?? 1;
        if (repeat < 1)
        {
            // Assumption: repeat values below 1 are treated as 1.
            repeat = 1;
        }

        int retryOnError = suite.Manifest.Controls?.RetryOnError ?? 0;
        if (retryOnError < 0)
        {
            // Assumption: retryOnError values below 0 are treated as 0.
            retryOnError = 0;
        }

        bool stopRequested = false;
        for (int iteration = 0; iteration < repeat && !stopRequested; iteration++)
        {
            foreach (TestCaseNode node in suite.Manifest.TestCases)
            {
                ResolvedTestCase? resolved = ResolveCaseByRef(node.Ref, testCases, configuration.TestCaseRoot);
                if (resolved is null)
                {
                    continue;
                }

                Dictionary<string, JsonElement> defaults = InputResolver.ExtractDefaults(resolved.Manifest.Parameters);
                Dictionary<string, JsonElement>? suiteInputs = node.Inputs;
                Dictionary<string, JsonElement>? overrides = request.NodeOverrides is not null && request.NodeOverrides.TryGetValue(node.NodeId, out NodeOverride? nodeOverride)
                    ? nodeOverride.Inputs
                    : null;

                EffectiveInputsResult inputs = InputResolver.ResolveEffectiveInputs(
                    resolved.Manifest.Parameters ?? new List<ParameterDefinition>(),
                    defaults,
                    suiteInputs,
                    overrides,
                    effectiveEnv,
                    out ValidationResult inputValidation);

                if (!inputValidation.IsValid)
                {
                    throw new InvalidOperationException("Input validation failed.");
                }

                int attempt = 0;
                while (true)
                {
                    using JsonDocument manifestDoc = JsonFile.ReadDocument(resolved.ManifestPath);
                    CaseRunRequest caseRequest = new()
                    {
                        RunsRoot = configuration.RunsRoot,
                        ManifestPath = resolved.ManifestPath,
                        ScriptPath = Path.Combine(Path.GetDirectoryName(resolved.ManifestPath) ?? string.Empty, "run.ps1"),
                        TestId = resolved.Identity.Id,
                        TestVersion = resolved.Identity.Version,
                        ParentRunId = groupRunId,
                        NodeId = node.NodeId,
                        SuiteId = suite.Identity.Id,
                        SuiteVersion = suite.Identity.Version,
                        PlanId = planIdentity?.Id,
                        PlanVersion = planIdentity?.Version,
                        EffectiveInputs = inputs.Inputs,
                        RedactedInputs = inputs.RedactedInputs,
                        SecretKeys = inputs.SecretKeys,
                        EffectiveEnvironment = effectiveEnv,
                        SourceManifest = manifestDoc.RootElement.Clone(),
                        Parameters = BuildParameterSnapshot(resolved.Manifest.Parameters),
                        PwshPath = configuration.PwshPath,
                        TimeoutSec = resolved.Manifest.TimeoutSec
                    };

                    CaseRunResult result = await _runner.RunAsync(caseRequest, cancellationToken);
                    childRunIds.Add(result.RunId);

                    await AppendChildAsync(groupFolder, new
                    {
                        runId = result.RunId,
                        nodeId = node.NodeId,
                        testId = resolved.Identity.Id,
                        testVersion = resolved.Identity.Version,
                        status = result.Status
                    });

                    await IndexWriter.AppendTestCaseAsync(configuration.RunsRoot, result, caseRequest, suite.Identity, planIdentity);

                    if ((result.Status == "Error" || result.Status == "Timeout") && attempt < retryOnError)
                    {
                        attempt++;
                        continue;
                    }

                    if (suite.Manifest.Controls?.ContinueOnFailure == false && result.Status != "Passed")
                    {
                        stopRequested = true;
                    }

                    break;
                }

                if (stopRequested)
                {
                    break;
                }
            }
        }

        string status = AggregateStatus(childRunIds, groupFolder);
        DateTimeOffset end = DateTimeOffset.UtcNow;

        object summary = new
        {
            schemaVersion = "1.5.0",
            runType = "TestSuite",
            suiteId = suite.Identity.Id,
            suiteVersion = suite.Identity.Version,
            planId = planIdentity?.Id,
            planVersion = planIdentity?.Version,
            status,
            startTime = start.ToString("O", CultureInfo.InvariantCulture),
            endTime = end.ToString("O", CultureInfo.InvariantCulture),
            childRunIds = childRunIds
        };

        JsonFile.Write(Path.Combine(groupFolder, "result.json"), summary);

        await IndexWriter.AppendSuiteAsync(configuration.RunsRoot, groupRunId, suite, planIdentity, start, end, status);

        return new SuiteExecutionResult
        {
            RunId = groupRunId,
            RunFolder = groupFolder,
            Status = status,
            ChildRunIds = childRunIds
        };
    }

    public async Task<PlanExecutionResult> RunPlanAsync(
        ResolvedTestPlan plan,
        IReadOnlyList<ResolvedTestSuite> suites,
        IReadOnlyList<ResolvedTestCase> testCases,
        RunRequest request,
        RunConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = ValidateRunRequest(request);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException("RunRequest validation failed.");
        }

        string groupRunId = RunIdGenerator.CreateRunId("P");
        string groupFolder = Path.Combine(configuration.RunsRoot, groupRunId);
        while (Directory.Exists(groupFolder))
        {
            groupRunId = RunIdGenerator.CreateRunId("P");
            groupFolder = Path.Combine(configuration.RunsRoot, groupRunId);
        }

        Directory.CreateDirectory(groupFolder);
        List<string> childRunIds = new();
        DateTimeOffset start = DateTimeOffset.UtcNow;

        Dictionary<string, string> osEnv = EnvironmentResolver.SnapshotOsEnvironment();
        Dictionary<string, string> effectiveEnv = EnvironmentResolver.ResolveEffectiveEnvironment(osEnv, null, plan.Manifest.Environment?.Env, request.EnvironmentOverrides?.Env);

        WriteGroupArtifacts(groupFolder, plan.Manifest, plan.ManifestPath, null, effectiveEnv, request);

        foreach (string suiteRef in plan.Manifest.Suites)
        {
            if (!Identity.TryParse(suiteRef, out Identity suiteIdentity))
            {
                throw new InvalidOperationException($"Invalid suite reference '{suiteRef}'.");
            }

            ResolvedTestSuite? suite = suites.FirstOrDefault(s => s.Identity.Id == suiteIdentity.Id && s.Identity.Version == suiteIdentity.Version);
            if (suite is null)
            {
                throw new InvalidOperationException($"Suite '{suiteIdentity}' not found.");
            }

            RunRequest suiteRequest = new()
            {
                Suite = suiteIdentity.ToString(),
                EnvironmentOverrides = request.EnvironmentOverrides
            };

            SuiteExecutionResult suiteResult = await RunSuiteAsync(suite, testCases, suiteRequest, configuration, plan.Identity, plan.Manifest.Environment?.Env, request.EnvironmentOverrides?.Env, cancellationToken);
            childRunIds.Add(suiteResult.RunId);

            await AppendChildAsync(groupFolder, new
            {
                runId = suiteResult.RunId,
                suiteId = suite.Identity.Id,
                suiteVersion = suite.Identity.Version,
                status = suiteResult.Status
            });
        }

        string status = AggregateStatus(childRunIds, groupFolder);
        DateTimeOffset end = DateTimeOffset.UtcNow;

        object summary = new
        {
            schemaVersion = "1.5.0",
            runType = "TestPlan",
            planId = plan.Identity.Id,
            planVersion = plan.Identity.Version,
            status,
            startTime = start.ToString("O", CultureInfo.InvariantCulture),
            endTime = end.ToString("O", CultureInfo.InvariantCulture),
            childRunIds = childRunIds
        };

        JsonFile.Write(Path.Combine(groupFolder, "result.json"), summary);

        await IndexWriter.AppendPlanAsync(configuration.RunsRoot, groupRunId, plan, start, end, status);

        return new PlanExecutionResult
        {
            RunId = groupRunId,
            RunFolder = groupFolder,
            Status = status,
            ChildRunIds = childRunIds
        };
    }

    private static IReadOnlyList<ParameterDefinitionSnapshot> BuildParameterSnapshot(IReadOnlyList<ParameterDefinition>? parameters)
    {
        if (parameters is null)
        {
            return Array.Empty<ParameterDefinitionSnapshot>();
        }

        return parameters.Select(p => new ParameterDefinitionSnapshot
        {
            Name = p.Name,
            Type = p.Type
        }).ToList();
    }

    private static void WriteGroupArtifacts(string groupFolder, object manifest, string manifestPath, SuiteControls? controls, Dictionary<string, string> environment, RunRequest request)
    {
        object manifestSnapshot = new
        {
            sourceManifest = manifest,
            resolvedPath = manifestPath,
            resolvedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        JsonFile.Write(Path.Combine(groupFolder, "manifest.json"), manifestSnapshot);
        if (controls is not null)
        {
            JsonFile.Write(Path.Combine(groupFolder, "controls.json"), controls);
        }
        JsonFile.Write(Path.Combine(groupFolder, "environment.json"), environment);
        JsonFile.Write(Path.Combine(groupFolder, "runRequest.json"), request);
    }

    private static async Task AppendChildAsync(string groupFolder, object entry)
    {
        string path = Path.Combine(groupFolder, "children.jsonl");
        string line = JsonSerializer.Serialize(entry, JsonDefaults.Options);
        await File.AppendAllTextAsync(path, line + Environment.NewLine);
    }

    private static async Task AppendEventAsync(string groupFolder, object entry)
    {
        string path = Path.Combine(groupFolder, "events.jsonl");
        string line = JsonSerializer.Serialize(entry, JsonDefaults.Options);
        await File.AppendAllTextAsync(path, line + Environment.NewLine);
    }

    private static string AggregateStatus(List<string> childRunIds, string groupFolder)
    {
        if (childRunIds.Count == 0)
        {
            return "Error";
        }

        List<string> statuses = new();
        string childrenPath = Path.Combine(groupFolder, "children.jsonl");
        if (File.Exists(childrenPath))
        {
            foreach (string line in File.ReadAllLines(childrenPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using JsonDocument doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("status", out JsonElement statusElement))
                {
                    statuses.Add(statusElement.GetString() ?? string.Empty);
                }
            }
        }

        if (statuses.Contains("Aborted", StringComparer.OrdinalIgnoreCase))
        {
            return "Aborted";
        }

        if (statuses.Contains("Error", StringComparer.OrdinalIgnoreCase))
        {
            return "Error";
        }

        if (statuses.Contains("Timeout", StringComparer.OrdinalIgnoreCase))
        {
            return "Timeout";
        }

        if (statuses.Contains("Failed", StringComparer.OrdinalIgnoreCase))
        {
            return "Failed";
        }

        return "Passed";
    }

    private static ValidationResult ValidateRunRequest(RunRequest request)
    {
        ValidationResult validation = new();
        int count = 0;
        if (!string.IsNullOrWhiteSpace(request.TestCase))
        {
            count++;
        }

        if (!string.IsNullOrWhiteSpace(request.Suite))
        {
            count++;
        }

        if (!string.IsNullOrWhiteSpace(request.Plan))
        {
            count++;
        }

        if (count != 1)
        {
            validation.Add("RunRequest.Invalid", "RunRequest must specify exactly one target.", new Dictionary<string, object?>());
        }

        if (!string.IsNullOrWhiteSpace(request.Plan))
        {
            if (request.NodeOverrides is not null || request.CaseInputs is not null)
            {
                validation.Add("RunRequest.Plan.Invalid", "Plan RunRequest cannot include case inputs or node overrides.", new Dictionary<string, object?>());
            }
        }

        if (request.EnvironmentOverrides?.Env is not null)
        {
            foreach (string key in request.EnvironmentOverrides.Env.Keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    validation.Add("RunRequest.Environment.Invalid", "Environment override keys must be non-empty.", new Dictionary<string, object?>());
                }
            }
        }

        return validation;
    }

    private static ResolvedTestCase? ResolveCaseByRef(string reference, IReadOnlyList<ResolvedTestCase> testCases, string testCaseRoot)
    {
        string resolvedPath = PathUtils.CombineNormalized(testCaseRoot, reference);
        string manifestPath = Path.Combine(resolvedPath, "test.manifest.json");
        return testCases.FirstOrDefault(tc => PathUtils.NormalizePath(tc.ManifestPath).Equals(PathUtils.NormalizePath(manifestPath), StringComparison.OrdinalIgnoreCase));
    }
}
