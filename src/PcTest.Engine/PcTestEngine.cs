using System.Text.Json;
using PcTest.Contracts;
using PcTest.Runner;

namespace PcTest.Engine;

public sealed record EngineOptions(string TestCaseRoot, string SuiteRoot, string PlanRoot, string RunsRoot);

public sealed class PcTestEngine
{
    private readonly DiscoveryService _discoveryService = new();
    private readonly RunnerService _runnerService;

    public PcTestEngine(RunnerService runnerService)
    {
        _runnerService = runnerService;
    }

    public DiscoveryResult Discover(EngineOptions options)
    {
        return _discoveryService.Discover(new DiscoveryRequest(
            PathUtils.NormalizeAbsolute(options.TestCaseRoot),
            PathUtils.NormalizeAbsolute(options.SuiteRoot),
            PathUtils.NormalizeAbsolute(options.PlanRoot)));
    }

    public TestCaseExecutionResult RunTestCase(EngineOptions options, RunRequest runRequest)
    {
        var discovery = Discover(options);
        ValidateRunRequest(runRequest);
        if (runRequest.NodeOverrides is not null)
        {
            throw new ValidationException("RunRequest.TestCase.Invalid", new Dictionary<string, object>
            {
                ["reason"] = "NodeOverridesNotAllowed"
            });
        }

        if (runRequest.CaseInputs is null)
        {
            runRequest = runRequest with { CaseInputs = new Dictionary<string, JsonElement>() };
        }

        if (string.IsNullOrWhiteSpace(runRequest.TestCase))
        {
            throw new ValidationException("RunRequest.Target.Invalid", new Dictionary<string, object>
            {
                ["target"] = "testCase"
            });
        }

        var identity = IdentityParser.Parse(runRequest.TestCase);
        if (!discovery.TestCases.TryGetValue(identity, out var testCase))
        {
            throw new ValidationException("RunRequest.Target.NotFound", new Dictionary<string, object>
            {
                ["entityType"] = "TestCase",
                ["id"] = identity.Id,
                ["version"] = identity.Version,
                ["reason"] = "NotFound"
            });
        }

        var osEnv = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(entry => entry.Key is string)
            .ToDictionary(entry => (string)entry.Key, entry => entry.Value?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var effectiveEnv = Resolution.ComputeEffectiveEnvironment(osEnv, null, null, runRequest.EnvironmentOverrides?.Env);

        var inputResolution = Resolution.ResolveInputs(
            testCase.Manifest,
            null,
            runRequest.CaseInputs,
            effectiveEnv,
            null);

        var runId = RunIdGenerator.NewRunId();
        var request = new TestCaseExecutionRequest
        {
            TestCasePath = testCase.ManifestPath,
            TestCase = testCase.Manifest,
            SourceManifest = testCase.Source,
            ResolvedRef = PathUtils.NormalizeAbsolute(testCase.FolderPath),
            Identity = identity,
            RunId = runId,
            RunsRoot = options.RunsRoot,
            EffectiveInputs = inputResolution.EffectiveInputs,
            RedactedInputs = inputResolution.RedactedInputs,
            SecretInputs = inputResolution.SecretInputs,
            EffectiveEnvironment = effectiveEnv,
            InputTemplates = JsonSerializer.SerializeToElement(inputResolution.InputTemplates, JsonUtils.SerializerOptions),
            WorkingDir = null,
            EngineVersion = typeof(PcTestEngine).Assembly.GetName().Version?.ToString()
        };

        var result = _runnerService.RunTestCase(request);
        IndexWriter.AppendTestCase(options.RunsRoot, result, testCase.Manifest, null, null, null);
        return result;
    }

    public SuiteRunSummary RunSuite(EngineOptions options, RunRequest runRequest)
    {
        var discovery = Discover(options);
        ValidateRunRequest(runRequest);
        if (runRequest.CaseInputs is not null)
        {
            throw new ValidationException("RunRequest.Suite.Invalid", new Dictionary<string, object>
            {
                ["reason"] = "CaseInputsNotAllowed"
            });
        }

        if (string.IsNullOrWhiteSpace(runRequest.Suite))
        {
            throw new ValidationException("RunRequest.Target.Invalid", new Dictionary<string, object>
            {
                ["target"] = "suite"
            });
        }

        var suiteIdentity = IdentityParser.Parse(runRequest.Suite);
        if (!discovery.Suites.TryGetValue(suiteIdentity, out var suite))
        {
            throw new ValidationException("RunRequest.Target.NotFound", new Dictionary<string, object>
            {
                ["entityType"] = "TestSuite",
                ["id"] = suiteIdentity.Id,
                ["version"] = suiteIdentity.Version,
                ["reason"] = "NotFound"
            });
        }

        return RunSuiteInternal(options, discovery, suite, suiteIdentity, runRequest, null, null);
    }

    public PlanRunSummary RunPlan(EngineOptions options, RunRequest runRequest)
    {
        var discovery = Discover(options);
        ValidateRunRequest(runRequest);
        if (string.IsNullOrWhiteSpace(runRequest.Plan))
        {
            throw new ValidationException("RunRequest.Target.Invalid", new Dictionary<string, object>
            {
                ["target"] = "plan"
            });
        }

        if (runRequest.NodeOverrides is not null || runRequest.CaseInputs is not null)
        {
            throw new ValidationException("RunRequest.Plan.Invalid", new Dictionary<string, object>
            {
                ["reason"] = "PlanCannotOverrideInputs"
            });
        }

        var planIdentity = IdentityParser.Parse(runRequest.Plan);
        if (!discovery.Plans.TryGetValue(planIdentity, out var plan))
        {
            throw new ValidationException("RunRequest.Target.NotFound", new Dictionary<string, object>
            {
                ["entityType"] = "TestPlan",
                ["id"] = planIdentity.Id,
                ["version"] = planIdentity.Version,
                ["reason"] = "NotFound"
            });
        }

        var planRunId = RunIdGenerator.NewRunId("G");
        var planRunFolder = Path.Combine(options.RunsRoot, planRunId);
        Directory.CreateDirectory(planRunFolder);

        JsonUtils.WriteJsonFile(Path.Combine(planRunFolder, "manifest.json"), new Dictionary<string, object?>
        {
            ["schemaVersion"] = plan.Manifest.SchemaVersion,
            ["runType"] = "TestPlan",
            ["sourceManifest"] = plan.Source,
            ["resolvedIdentity"] = new Dictionary<string, string>
            {
                ["id"] = planIdentity.Id,
                ["version"] = planIdentity.Version
            }
        });

        if (plan.Manifest.Environment?.Env is not null)
        {
            JsonUtils.WriteJsonFile(Path.Combine(planRunFolder, "environment.json"), new Dictionary<string, object?>
            {
                ["env"] = new SortedDictionary<string, string>(plan.Manifest.Environment.Env, StringComparer.OrdinalIgnoreCase)
            });
        }

        if (runRequest is not null)
        {
            JsonUtils.WriteJsonFile(Path.Combine(planRunFolder, "runRequest.json"), runRequest);
        }

        var suiteSummaries = new List<SuiteRunSummary>();
        var children = new List<object>();
        var startTime = DateTimeOffset.UtcNow;
        foreach (var suiteRef in plan.Manifest.Suites)
        {
            var suiteIdentity = IdentityParser.Parse(suiteRef.Suite);
            if (!discovery.Suites.TryGetValue(suiteIdentity, out var suite))
            {
                throw new ValidationException("Plan.Suite.NotFound", new Dictionary<string, object>
                {
                    ["entityType"] = "TestSuite",
                    ["id"] = suiteIdentity.Id,
                    ["version"] = suiteIdentity.Version,
                    ["reason"] = "NotFound"
                });
            }

            var suiteSummary = RunSuiteInternal(options, discovery, suite, suiteIdentity, new RunRequest
            {
                Suite = suiteRef.Suite,
                EnvironmentOverrides = runRequest.EnvironmentOverrides
            }, planIdentity, planRunId);
            suiteSummaries.Add(suiteSummary);
            children.Add(new Dictionary<string, object?>
            {
                ["runId"] = suiteSummary.RunId,
                ["suiteId"] = suiteIdentity.Id,
                ["suiteVersion"] = suiteIdentity.Version,
                ["status"] = suiteSummary.Status
            });
        }

        var endTime = DateTimeOffset.UtcNow;
        var status = AggregateStatus(suiteSummaries.Select(s => s.Status));
        JsonUtils.WriteJsonLines(Path.Combine(planRunFolder, "children.jsonl"), children);

        var summary = new PlanRunSummary
        {
            RunId = planRunId,
            Status = status,
            StartTime = startTime,
            EndTime = endTime,
            ChildRunIds = suiteSummaries.Select(s => s.RunId).ToArray(),
            PlanIdentity = planIdentity
        };

        JsonUtils.WriteJsonFile(Path.Combine(planRunFolder, "result.json"), new Dictionary<string, object?>
        {
            ["schemaVersion"] = plan.Manifest.SchemaVersion,
            ["runType"] = "TestPlan",
            ["planId"] = planIdentity.Id,
            ["planVersion"] = planIdentity.Version,
            ["status"] = summary.Status,
            ["startTime"] = summary.StartTime.ToString("O"),
            ["endTime"] = summary.EndTime.ToString("O"),
            ["childRunIds"] = summary.ChildRunIds
        });

        IndexWriter.AppendPlan(options.RunsRoot, summary);
        return summary;
    }

    private SuiteRunSummary RunSuiteInternal(
        EngineOptions options,
        DiscoveryResult discovery,
        DiscoveredSuite suite,
        Identity suiteIdentity,
        RunRequest runRequest,
        Identity? planIdentity,
        string? planRunId)
    {
        if (runRequest.NodeOverrides is not null)
        {
            foreach (var nodeId in runRequest.NodeOverrides.Keys)
            {
                if (!suite.Manifest.TestCases.Any(node => node.NodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ValidationException("RunRequest.NodeOverrides.Invalid", new Dictionary<string, object>
                    {
                        ["nodeId"] = nodeId
                    });
                }
            }
        }

        var groupRunId = RunIdGenerator.NewRunId("G");
        var suiteRunFolder = Path.Combine(options.RunsRoot, groupRunId);
        Directory.CreateDirectory(suiteRunFolder);

        JsonUtils.WriteJsonFile(Path.Combine(suiteRunFolder, "manifest.json"), new Dictionary<string, object?>
        {
            ["schemaVersion"] = suite.Manifest.SchemaVersion,
            ["runType"] = "TestSuite",
            ["sourceManifest"] = suite.Source,
            ["resolvedIdentity"] = new Dictionary<string, string>
            {
                ["id"] = suiteIdentity.Id,
                ["version"] = suiteIdentity.Version
            }
        });

        if (suite.Manifest.Controls.HasValue)
        {
            JsonUtils.WriteJsonFile(Path.Combine(suiteRunFolder, "controls.json"), suite.Manifest.Controls.Value);
        }

        if (suite.Manifest.Environment is not null)
        {
            JsonUtils.WriteJsonFile(Path.Combine(suiteRunFolder, "environment.json"), new Dictionary<string, object?>
            {
                ["env"] = suite.Manifest.Environment.Env is null ? null : new SortedDictionary<string, string>(suite.Manifest.Environment.Env, StringComparer.OrdinalIgnoreCase),
                ["workingDir"] = suite.Manifest.Environment.WorkingDir
            });
        }

        if (runRequest is not null)
        {
            JsonUtils.WriteJsonFile(Path.Combine(suiteRunFolder, "runRequest.json"), runRequest);
        }

        var osEnv = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(entry => entry.Key is string)
            .ToDictionary(entry => (string)entry.Key, entry => entry.Value?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        var planEnv = planIdentity is null ? null : (discovery.Plans.TryGetValue(planIdentity.Value, out var plan) ? plan.Manifest.Environment?.Env : null);
        var effectiveEnv = Resolution.ComputeEffectiveEnvironment(osEnv, suite.Manifest.Environment?.Env, planEnv, runRequest.EnvironmentOverrides?.Env);

        var childResults = new List<TestCaseExecutionResult>();
        var children = new List<object>();
        var startTime = DateTimeOffset.UtcNow;

        foreach (var node in suite.Manifest.TestCases)
        {
            var resolvedCase = ResolveTestCaseRef(options.TestCaseRoot, suite.ManifestPath, node.Ref);
            if (!discovery.TestCases.TryGetValue(new Identity(resolvedCase.Manifest.Id, resolvedCase.Manifest.Version), out var testCase))
            {
                testCase = resolvedCase;
            }

            var overrideInputs = runRequest.NodeOverrides != null && runRequest.NodeOverrides.TryGetValue(node.NodeId, out var nodeOverride)
                ? nodeOverride.Inputs
                : null;

            var inputResolution = Resolution.ResolveInputs(testCase.Manifest, node.Inputs, overrideInputs, effectiveEnv, node.NodeId);
            var runId = RunIdGenerator.NewRunId();
            var request = new TestCaseExecutionRequest
            {
                TestCasePath = testCase.ManifestPath,
                TestCase = testCase.Manifest,
                SourceManifest = testCase.Source,
                ResolvedRef = resolvedCase.ManifestPath,
                Identity = new Identity(testCase.Manifest.Id, testCase.Manifest.Version),
                RunId = runId,
                RunsRoot = options.RunsRoot,
                NodeId = node.NodeId,
                SuiteIdentity = suiteIdentity,
                PlanIdentity = planIdentity,
                EffectiveInputs = inputResolution.EffectiveInputs,
                RedactedInputs = inputResolution.RedactedInputs,
                SecretInputs = inputResolution.SecretInputs,
                EffectiveEnvironment = effectiveEnv,
                InputTemplates = JsonSerializer.SerializeToElement(inputResolution.InputTemplates, JsonUtils.SerializerOptions),
                WorkingDir = suite.Manifest.Environment?.WorkingDir,
                EngineVersion = typeof(PcTestEngine).Assembly.GetName().Version?.ToString()
            };

            var result = _runnerService.RunTestCase(request);
            childResults.Add(result);
            children.Add(new Dictionary<string, object?>
            {
                ["runId"] = result.RunId,
                ["nodeId"] = node.NodeId,
                ["testId"] = testCase.Manifest.Id,
                ["testVersion"] = testCase.Manifest.Version,
                ["status"] = result.Status
            });

            IndexWriter.AppendTestCase(options.RunsRoot, result, testCase.Manifest, node.NodeId, suiteIdentity, planIdentity, groupRunId);
        }

        var endTime = DateTimeOffset.UtcNow;
        var status = AggregateStatus(childResults.Select(r => r.Status));
        JsonUtils.WriteJsonLines(Path.Combine(suiteRunFolder, "children.jsonl"), children);

        var summary = new SuiteRunSummary
        {
            RunId = groupRunId,
            Status = status,
            StartTime = startTime,
            EndTime = endTime,
            ChildRunIds = childResults.Select(r => r.RunId).ToArray(),
            SuiteIdentity = suiteIdentity,
            PlanIdentity = planIdentity
        };

        var resultPayload = new Dictionary<string, object?>
        {
            ["schemaVersion"] = suite.Manifest.SchemaVersion,
            ["runType"] = "TestSuite",
            ["suiteId"] = suiteIdentity.Id,
            ["suiteVersion"] = suiteIdentity.Version,
            ["status"] = summary.Status,
            ["startTime"] = summary.StartTime.ToString("O"),
            ["endTime"] = summary.EndTime.ToString("O"),
            ["childRunIds"] = summary.ChildRunIds
        };

        if (planIdentity is not null)
        {
            resultPayload["planId"] = planIdentity.Value.Id;
            resultPayload["planVersion"] = planIdentity.Value.Version;
        }

        JsonUtils.WriteJsonFile(Path.Combine(suiteRunFolder, "result.json"), resultPayload);
        IndexWriter.AppendSuite(options.RunsRoot, summary, planRunId);
        return summary;
    }

    private static DiscoveredTestCase ResolveTestCaseRef(string testCaseRoot, string suitePath, string reference)
    {
        var expectedRoot = PathUtils.NormalizeAbsolute(testCaseRoot);
        var candidateDir = PathUtils.NormalizeAbsolute(Path.Combine(expectedRoot, reference));
        var resolvedDir = PathUtils.ResolveLinkTargetPath(candidateDir);

        if (!PathUtils.IsContained(expectedRoot, resolvedDir))
        {
            throw new ValidationException("Suite.TestCaseRef.Invalid", new Dictionary<string, object>
            {
                ["entityType"] = "TestSuite",
                ["suitePath"] = suitePath,
                ["ref"] = reference,
                ["resolvedPath"] = resolvedDir,
                ["expectedRoot"] = expectedRoot,
                ["reason"] = "OutOfRoot"
            });
        }

        if (!Directory.Exists(resolvedDir))
        {
            throw new ValidationException("Suite.TestCaseRef.Invalid", new Dictionary<string, object>
            {
                ["entityType"] = "TestSuite",
                ["suitePath"] = suitePath,
                ["ref"] = reference,
                ["resolvedPath"] = resolvedDir,
                ["expectedRoot"] = expectedRoot,
                ["reason"] = "NotFound"
            });
        }

        var manifestPath = Path.Combine(resolvedDir, "test.manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new ValidationException("Suite.TestCaseRef.Invalid", new Dictionary<string, object>
            {
                ["entityType"] = "TestSuite",
                ["suitePath"] = suitePath,
                ["ref"] = reference,
                ["resolvedPath"] = manifestPath,
                ["expectedRoot"] = expectedRoot,
                ["reason"] = "MissingManifest"
            });
        }

        using var doc = JsonUtils.ReadJsonDocument(manifestPath);
        var manifest = doc.RootElement.Deserialize<TestCaseManifest>(JsonUtils.SerializerOptions)
            ?? throw new InvalidDataException($"Invalid manifest: {manifestPath}");
        return new DiscoveredTestCase(manifest, manifestPath, resolvedDir, doc.RootElement.Clone());
    }

    private static string AggregateStatus(IEnumerable<string> statuses)
    {
        var statusList = statuses.ToList();
        if (statusList.Any(status => status.Equals("Error", StringComparison.OrdinalIgnoreCase)))
        {
            return "Error";
        }

        if (statusList.Any(status => status.Equals("Timeout", StringComparison.OrdinalIgnoreCase)))
        {
            return "Timeout";
        }

        if (statusList.Any(status => status.Equals("Failed", StringComparison.OrdinalIgnoreCase)))
        {
            return "Failed";
        }

        return "Passed";
    }

    private static void ValidateRunRequest(RunRequest runRequest)
    {
        var targets = new[] { runRequest.Suite, runRequest.TestCase, runRequest.Plan };
        if (targets.Count(target => !string.IsNullOrWhiteSpace(target)) != 1)
        {
            throw new ValidationException("RunRequest.Target.Invalid", new Dictionary<string, object>
            {
                ["reason"] = "MustSpecifyExactlyOneTarget"
            });
        }
    }
}

public sealed record SuiteRunSummary
{
    public string RunId { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }
    public string[] ChildRunIds { get; init; } = Array.Empty<string>();
    public Identity SuiteIdentity { get; init; }
    public Identity? PlanIdentity { get; init; }
}

public sealed record PlanRunSummary
{
    public string RunId { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }
    public string[] ChildRunIds { get; init; } = Array.Empty<string>();
    public Identity PlanIdentity { get; init; }
}
