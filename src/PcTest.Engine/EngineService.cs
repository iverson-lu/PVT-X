using System.Text.Json;
using PcTest.Contracts;
using PcTest.Runner;

namespace PcTest.Engine;

public sealed class EngineService
{
    private readonly DiscoveryService _discoveryService = new();
    private readonly InputResolver _inputResolver = new();
    private readonly RunnerService _runnerService = new();

    public DiscoveryResult Discover(ResolvedRoots roots) => _discoveryService.Discover(roots);

    public RunnerResult RunCase(ResolvedRoots roots, string caseIdVersion, RunRequest? runRequest)
    {
        var discovery = Discover(roots);
        var entry = discovery.Cases.SingleOrDefault(item => string.Equals(item.IdVersion, caseIdVersion, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            throw new InvalidOperationException($"Case not found: {caseIdVersion}");
        }

        var runId = Guid.NewGuid().ToString("N");
        var caseRunFolder = Path.Combine(roots.RunsRoot, runId);
        var runIndex = new RunIndexWriter(roots.RunsRoot);
        var effectiveEnv = _inputResolver.ResolveEnvironment(null, entry.Manifest.Environment, runRequest?.EnvOverride);
        var resolved = _inputResolver.ResolveInputs(entry.Manifest, null, runRequest?.CaseInputs, effectiveEnv, null);
        var envRedaction = _inputResolver.ResolveEnvRedaction(effectiveEnv, runRequest?.EnvOverride);
        resolved.Redaction.SecretEnv.AddRange(envRedaction.SecretEnv);

        var request = new RunnerRequest
        {
            CaseRunId = runId,
            CaseRunFolder = caseRunFolder,
            ManifestPath = entry.Path,
            ScriptPath = Path.Combine(Path.GetDirectoryName(entry.Path) ?? string.Empty, entry.Manifest.Script),
            WorkingDir = caseRunFolder,
            Inputs = resolved.Values,
            Environment = effectiveEnv,
            Redaction = resolved.Redaction
        };

        var result = _runnerService.Run(request, CancellationToken.None);
        runIndex.Append(new RunIndexEntry
        {
            RunId = runId,
            Entity = "case",
            Id = entry.Manifest.Id,
            Version = entry.Manifest.Version,
            Path = caseRunFolder
        });

        return result;
    }

    public SuiteResult RunSuite(ResolvedRoots roots, string suiteIdVersion, RunRequest? runRequest)
    {
        var discovery = Discover(roots);
        var suiteEntry = discovery.Suites.SingleOrDefault(item => string.Equals(item.IdVersion, suiteIdVersion, StringComparison.OrdinalIgnoreCase));
        if (suiteEntry is null)
        {
            throw new InvalidOperationException($"Suite not found: {suiteIdVersion}");
        }

        if (suiteEntry.Manifest.Controls?.MaxParallel is not null)
        {
            Console.Error.WriteLine(WarningCodes.ControlsMaxParallelIgnored);
        }

        var runId = Guid.NewGuid().ToString("N");
        var groupFolder = Path.Combine(roots.RunsRoot, runId);
        Directory.CreateDirectory(groupFolder);
        var childrenPath = Path.Combine(groupFolder, "children.jsonl");
        var resultPath = Path.Combine(groupFolder, "result.json");

        var runIndex = new RunIndexWriter(roots.RunsRoot);
        runIndex.Append(new RunIndexEntry
        {
            RunId = runId,
            Entity = "suite",
            Id = suiteEntry.Manifest.Id,
            Version = suiteEntry.Manifest.Version,
            Path = groupFolder
        });

        var suiteResult = new SuiteResult();
        var effectiveEnv = _inputResolver.ResolveEnvironment(null, suiteEntry.Manifest.Environment, runRequest?.EnvOverride);

        foreach (var node in suiteEntry.Manifest.Nodes)
        {
            var caseManifestPath = ResolveCaseManifestPath(roots.TestCaseRoot, node.Ref, suiteEntry.Path);
            var caseEntry = discovery.Cases.SingleOrDefault(item => string.Equals(item.Path, caseManifestPath, StringComparison.OrdinalIgnoreCase));
            if (caseEntry is null)
            {
                throw new InvalidOperationException(JsonSerializer.Serialize(new
                {
                    code = ErrorCodes.SuiteTestCaseRefInvalid,
                    payload = new
                    {
                        entityType = "TestCase",
                        suitePath = suiteEntry.Path,
                        @ref = node.Ref,
                        resolvedPath = caseManifestPath,
                        expectedRoot = roots.TestCaseRoot,
                        reason = "NotFound"
                    }
                }, JsonDefaults.Options));
            }

            var overrideInputs = runRequest?.NodeOverrides is not null && runRequest.NodeOverrides.TryGetValue(node.NodeId, out var nodeOverride)
                ? nodeOverride.Inputs
                : null;

            var resolved = _inputResolver.ResolveInputs(caseEntry.Manifest, node.Inputs, overrideInputs, effectiveEnv, node.NodeId);
            var runEntry = ExecuteCaseNode(roots, caseEntry, node.NodeId, resolved, effectiveEnv);
            suiteResult.Children.Add(runEntry);

            var childJson = JsonSerializer.Serialize(runEntry, JsonDefaults.Options);
            File.AppendAllText(childrenPath, childJson + Environment.NewLine);

            if (runEntry.Status is "Failed" && !node.ContinueOnFailure)
            {
                suiteResult.Status = "Failed";
                File.WriteAllText(resultPath, JsonSerializer.Serialize(suiteResult, JsonDefaults.Options));
                return suiteResult;
            }

            if (runEntry.Status is "Error" && node.RetryOnError > 0)
            {
                for (var retry = 0; retry < node.RetryOnError; retry++)
                {
                    runEntry = ExecuteCaseNode(roots, caseEntry, node.NodeId, resolved, effectiveEnv);
                    suiteResult.Children.Add(runEntry);
                    childJson = JsonSerializer.Serialize(runEntry, JsonDefaults.Options);
                    File.AppendAllText(childrenPath, childJson + Environment.NewLine);
                    if (runEntry.Status is not "Error")
                    {
                        break;
                    }
                }
            }

            if (node.Repeat > 1)
            {
                for (var repeat = 1; repeat < node.Repeat; repeat++)
                {
                    runEntry = ExecuteCaseNode(roots, caseEntry, node.NodeId, resolved, effectiveEnv);
                    suiteResult.Children.Add(runEntry);
                    childJson = JsonSerializer.Serialize(runEntry, JsonDefaults.Options);
                    File.AppendAllText(childrenPath, childJson + Environment.NewLine);
                }
            }
        }

        suiteResult.Status = suiteResult.Children.Any(child => child.Status == "Error") ? "Error"
            : suiteResult.Children.Any(child => child.Status == "Failed") ? "Failed"
            : "Passed";
        File.WriteAllText(resultPath, JsonSerializer.Serialize(suiteResult, JsonDefaults.Options));
        return suiteResult;
    }

    public SuiteResult RunPlan(ResolvedRoots roots, string planIdVersion, RunRequest? runRequest)
    {
        if (runRequest?.CaseInputs is not null || runRequest?.NodeOverrides is not null)
        {
            throw new InvalidOperationException("Plan run request cannot include caseInputs or nodeOverrides.");
        }

        var discovery = Discover(roots);
        var planEntry = discovery.Plans.SingleOrDefault(item => string.Equals(item.IdVersion, planIdVersion, StringComparison.OrdinalIgnoreCase));
        if (planEntry is null)
        {
            throw new InvalidOperationException($"Plan not found: {planIdVersion}");
        }

        var runId = Guid.NewGuid().ToString("N");
        var groupFolder = Path.Combine(roots.RunsRoot, runId);
        Directory.CreateDirectory(groupFolder);
        var childrenPath = Path.Combine(groupFolder, "children.jsonl");
        var resultPath = Path.Combine(groupFolder, "result.json");

        var runIndex = new RunIndexWriter(roots.RunsRoot);
        runIndex.Append(new RunIndexEntry
        {
            RunId = runId,
            Entity = "plan",
            Id = planEntry.Manifest.Id,
            Version = planEntry.Manifest.Version,
            Path = groupFolder
        });

        var suiteResult = new SuiteResult();
        foreach (var suiteRef in planEntry.Manifest.Suites)
        {
            var suiteEntry = discovery.Suites.SingleOrDefault(item => string.Equals(item.IdVersion, suiteRef, StringComparison.OrdinalIgnoreCase));
            if (suiteEntry is null)
            {
                throw new InvalidOperationException($"Suite not found in plan: {suiteRef}");
            }

            var planEnv = planEntry.Manifest.Environment;
            var effectiveEnv = _inputResolver.ResolveEnvironment(planEnv, suiteEntry.Manifest.Environment, runRequest?.EnvOverride);

            foreach (var node in suiteEntry.Manifest.Nodes)
            {
                var caseManifestPath = ResolveCaseManifestPath(roots.TestCaseRoot, node.Ref, suiteEntry.Path);
                var caseEntry = discovery.Cases.SingleOrDefault(item => string.Equals(item.Path, caseManifestPath, StringComparison.OrdinalIgnoreCase));
                if (caseEntry is null)
                {
                    throw new InvalidOperationException(JsonSerializer.Serialize(new
                    {
                        code = ErrorCodes.SuiteTestCaseRefInvalid,
                        payload = new
                        {
                            entityType = "TestCase",
                            suitePath = suiteEntry.Path,
                            @ref = node.Ref,
                            resolvedPath = caseManifestPath,
                            expectedRoot = roots.TestCaseRoot,
                            reason = "NotFound"
                        }
                    }, JsonDefaults.Options));
                }

                var resolved = _inputResolver.ResolveInputs(caseEntry.Manifest, node.Inputs, null, effectiveEnv, node.NodeId);
                var runEntry = ExecuteCaseNode(roots, caseEntry, node.NodeId, resolved, effectiveEnv);
                suiteResult.Children.Add(runEntry);
                var childJson = JsonSerializer.Serialize(runEntry, JsonDefaults.Options);
                File.AppendAllText(childrenPath, childJson + Environment.NewLine);
            }
        }

        suiteResult.Status = suiteResult.Children.Any(child => child.Status == "Error") ? "Error"
            : suiteResult.Children.Any(child => child.Status == "Failed") ? "Failed"
            : "Passed";
        File.WriteAllText(resultPath, JsonSerializer.Serialize(suiteResult, JsonDefaults.Options));
        return suiteResult;
    }

    private ChildResult ExecuteCaseNode(
        ResolvedRoots roots,
        ManifestEntry<TestCaseManifest> caseEntry,
        string nodeId,
        ResolvedInputs resolved,
        Dictionary<string, string> effectiveEnv)
    {
        var runId = Guid.NewGuid().ToString("N");
        var caseRunFolder = Path.Combine(roots.RunsRoot, runId);
        var runIndex = new RunIndexWriter(roots.RunsRoot);
        var request = new RunnerRequest
        {
            CaseRunId = runId,
            CaseRunFolder = caseRunFolder,
            ManifestPath = caseEntry.Path,
            ScriptPath = Path.Combine(Path.GetDirectoryName(caseEntry.Path) ?? string.Empty, caseEntry.Manifest.Script),
            WorkingDir = caseRunFolder,
            Inputs = resolved.Values,
            Environment = effectiveEnv,
            Redaction = resolved.Redaction
        };

        var result = _runnerService.Run(request, CancellationToken.None);
        runIndex.Append(new RunIndexEntry
        {
            RunId = runId,
            Entity = "case",
            Id = caseEntry.Manifest.Id,
            Version = caseEntry.Manifest.Version,
            Path = caseRunFolder
        });

        return new ChildResult
        {
            NodeId = nodeId,
            CaseRunId = runId,
            Status = result.Status
        };
    }

    private static string ResolveCaseManifestPath(string caseRoot, string reference, string suitePath)
    {
        var rootFull = Path.GetFullPath(caseRoot);
        var combined = Path.GetFullPath(Path.Combine(rootFull, reference, "test.manifest.json"));
        if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(JsonSerializer.Serialize(new
            {
                code = ErrorCodes.SuiteTestCaseRefInvalid,
                payload = new
                {
                    entityType = "TestCase",
                    suitePath,
                    @ref = reference,
                    resolvedPath = combined,
                    expectedRoot = rootFull,
                    reason = "OutOfRoot"
                }
            }, JsonDefaults.Options));
        }

        if (HasReparsePoint(combined))
        {
            throw new InvalidOperationException(JsonSerializer.Serialize(new
            {
                code = ErrorCodes.SuiteTestCaseRefInvalid,
                payload = new
                {
                    entityType = "TestCase",
                    suitePath,
                    @ref = reference,
                    resolvedPath = combined,
                    expectedRoot = rootFull,
                    reason = "ReparsePoint"
                }
            }, JsonDefaults.Options));
        }

        if (!File.Exists(combined))
        {
            throw new InvalidOperationException(JsonSerializer.Serialize(new
            {
                code = ErrorCodes.SuiteTestCaseRefInvalid,
                payload = new
                {
                    entityType = "TestCase",
                    suitePath,
                    @ref = reference,
                    resolvedPath = combined,
                    expectedRoot = rootFull,
                    reason = "MissingManifest"
                }
            }, JsonDefaults.Options));
        }

        return combined;
    }

    private static bool HasReparsePoint(string path)
    {
        var current = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(current))
            {
                var attrs = new DirectoryInfo(current).Attributes;
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
            }

            current = Path.GetDirectoryName(current);
        }

        return false;
    }
}
