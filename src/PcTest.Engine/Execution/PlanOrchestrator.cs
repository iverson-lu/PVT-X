using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Contracts.Requests;
using PcTest.Contracts.Results;
using PcTest.Contracts.Validation;
using PcTest.Engine.Discovery;
using PcTest.Engine.Resolution;
using PcTest.Runner;

namespace PcTest.Engine.Execution;

/// <summary>
/// Orchestrator for Test Plan execution per spec section 10.
/// </summary>
public sealed class PlanOrchestrator
{
    private readonly DiscoveryResult _discovery;
    private readonly string _runsRoot;
    private readonly string _assetsRoot;
    private readonly IExecutionReporter _reporter;
    private readonly CancellationToken _cancellationToken;

    public PlanOrchestrator(
        DiscoveryResult discovery,
        string runsRoot,
        string assetsRoot,
        IExecutionReporter reporter,
        CancellationToken cancellationToken = default)
    {
        _discovery = discovery;
        _runsRoot = PathUtils.NormalizePath(runsRoot);
        _assetsRoot = PathUtils.NormalizePath(assetsRoot);
        _reporter = reporter ?? NullExecutionReporter.Instance;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Executes a Plan with the given RunRequest.
    /// Per spec section 8.3: Plan RunRequest must NOT include nodeOverrides or caseInputs.
    /// </summary>
    public async Task<GroupResult> ExecuteAsync(
        DiscoveredTestPlan plan,
        RunRequest runRequest)
    {
        return await ExecuteInternalAsync(plan, runRequest, resumeSession: null);
    }

    public async Task<GroupResult> ResumeAsync(
        DiscoveredTestPlan plan,
        RunRequest runRequest,
        RebootResumeSession resumeSession)
    {
        return await ExecuteInternalAsync(plan, runRequest, resumeSession);
    }

    private async Task<GroupResult> ExecuteInternalAsync(
        DiscoveredTestPlan plan,
        RunRequest runRequest,
        RebootResumeSession? resumeSession)
    {
        // Validate Plan RunRequest constraints per spec section 8.3
        if (runRequest.NodeOverrides is not null && runRequest.NodeOverrides.Count > 0)
        {
            throw new ValidationException(ErrorCodes.RunRequestPlanInputOverride,
                "Plan RunRequest must not include nodeOverrides");
        }

        if (runRequest.CaseInputs is not null && runRequest.CaseInputs.Count > 0)
        {
            throw new ValidationException(ErrorCodes.RunRequestPlanInputOverride,
                "Plan RunRequest must not include caseInputs");
        }

        var startTime = DateTime.UtcNow;
        var isResuming = resumeSession is not null;
        var groupRunId = isResuming ? resumeSession!.RunId : GroupRunFolderManager.GenerateGroupRunId("P");
        var folderManager = new GroupRunFolderManager(_runsRoot);
        var groupRunFolder = isResuming
            ? resumeSession!.RunFolder
            : folderManager.CreateGroupRunFolder(groupRunId);

        var childRunIds = new List<string>();
        var childResults = new List<GroupResult>();
        var statusCounts = new StatusCounts();

        try
        {
            if (isResuming && resumeSession?.Context is null)
            {
                throw new InvalidOperationException("Resume context missing for plan.");
            }

            if (isResuming)
            {
                var existingState = LoadExistingPlanState(groupRunFolder);
                childResults.AddRange(existingState.Results);
                childRunIds.AddRange(existingState.RunIds);
                statusCounts.Passed = existingState.Counts.Passed;
                statusCounts.Failed = existingState.Counts.Failed;
                statusCounts.Error = existingState.Counts.Error;
                statusCounts.Timeout = existingState.Counts.Timeout;
                statusCounts.Aborted = existingState.Counts.Aborted;
            }

            // Write initial artifacts
            var manifestSnapshot = new GroupManifestSnapshot
            {
                SchemaVersion = "1.5.0",
                RunType = RunType.TestPlan,
                PlanId = plan.Manifest.Id,
                PlanVersion = plan.Manifest.Version,
                OriginalManifest = JsonSerializer.SerializeToElement(plan.Manifest, JsonDefaults.WriteOptions),
                ResolvedAt = DateTime.UtcNow.ToString("o"),
                EngineVersion = "1.0.0"
            };
            if (!isResuming || !File.Exists(Path.Combine(groupRunFolder, "manifest.json")))
            {
                await folderManager.WriteManifestAsync(groupRunFolder, manifestSnapshot);
            }

            // Compute effective environment
            var envResolver = new EnvironmentResolver();

            if (!isResuming || !File.Exists(Path.Combine(groupRunFolder, "runRequest.json")))
            {
                await folderManager.WriteRunRequestAsync(groupRunFolder, runRequest);
            }

            // Record plan started event
            if (!isResuming)
            {
                await folderManager.AppendEventAsync(groupRunFolder, new EventEntry
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Code = "TestPlan.Started",
                    Level = "info",
                    Message = $"Test plan '{plan.Manifest.Id}' (version {plan.Manifest.Version}) execution started",
                    Data = new Dictionary<string, object?>
                    {
                        ["planId"] = plan.Manifest.Id,
                        ["planVersion"] = plan.Manifest.Version,
                        ["runId"] = groupRunId,
                        ["suiteCount"] = plan.Manifest.Suites.Count
                    }
                });
            }

            // Report planned nodes - plan level shows suites as nodes
            if (!isResuming)
            {
                var plannedNodes = new List<PlannedNode>();
                foreach (var suiteIdentity in plan.Manifest.Suites)
                {
                    var parseResult = IdentityParser.Parse(suiteIdentity);
                    plannedNodes.Add(new PlannedNode
                    {
                        NodeId = suiteIdentity,
                        TestId = parseResult.Success ? parseResult.Id : suiteIdentity,
                        TestVersion = parseResult.Success ? parseResult.Version : "unknown",
                        NodeType = RunType.TestSuite
                    });
                }
                _reporter.OnRunPlanned(groupRunId, RunType.TestPlan, plannedNodes);
            }

            // Execute each Suite in order per spec section 6.4
            var startIndex = resumeSession?.CurrentNodeIndex ?? 0;
            for (var suiteIndex = startIndex; suiteIndex < plan.Manifest.Suites.Count; suiteIndex++)
            {
                var suiteIdentity = plan.Manifest.Suites[suiteIndex];
                if (_cancellationToken.IsCancellationRequested)
                    break;

                // Report node started
                _reporter.OnNodeStarted(groupRunId, suiteIdentity);

                var suiteStartTime = DateTime.UtcNow;

                // Resolve Suite
                if (!_discovery.TestSuites.TryGetValue(suiteIdentity, out var suite))
                {
                    // Suite not found
                    var errorResult = CreateSuiteErrorResult(
                        suiteIdentity, plan.Manifest, $"Suite '{suiteIdentity}' not found");
                    childResults.Add(errorResult);
                    UpdateCounts(statusCounts, errorResult.Status);

                    // Report node finished with error
                    _reporter.OnNodeFinished(groupRunId, new NodeFinishedState
                    {
                        NodeId = suiteIdentity,
                        Status = RunStatus.Error,
                        StartTime = suiteStartTime,
                        EndTime = DateTime.UtcNow,
                        Message = $"Suite '{suiteIdentity}' not found"
                    });
                    continue;
                }

                // Compute effective environment for this Suite under the Plan
                var effectiveEnv = envResolver.ComputePlanEnvironment(
                    plan.Manifest, suite.Manifest, runRequest.EnvironmentOverrides);

                // Create Suite RunRequest (env-only override from Plan)
                var suiteRunRequest = new RunRequest
                {
                    Suite = suiteIdentity,
                    EnvironmentOverrides = runRequest.EnvironmentOverrides
                };

                // Execute Suite
                var suiteOrchestrator = new SuiteOrchestrator(_discovery, _runsRoot, _assetsRoot, _reporter, _cancellationToken);
                GroupResult suiteResult;
                if (isResuming && suiteIndex == startIndex && resumeSession?.Context is not null)
                {
                    if (string.IsNullOrWhiteSpace(resumeSession.ChildRunId))
                    {
                        throw new InvalidOperationException("Resume session missing suite run ID.");
                    }

                    var suiteResumeSession = new RebootResumeSession
                    {
                        RunId = resumeSession.ChildRunId,
                        EntityType = "TestSuite",
                        CurrentNodeIndex = resumeSession.ChildNodeIndex ?? 0,
                        CurrentIteration = resumeSession.ChildIteration ?? 0,
                        NextPhase = resumeSession.NextPhase,
                        RunFolder = Path.Combine(_runsRoot, resumeSession.ChildRunId),
                        Context = resumeSession.Context
                    };

                    suiteResult = await suiteOrchestrator.ResumeAsync(
                        suite,
                        suiteRunRequest,
                        suiteResumeSession,
                        plan.Manifest.Id,
                        plan.Manifest.Version,
                        groupRunId,
                        suiteIdentity,
                        groupRunFolder,
                        plan.Manifest);
                }
                else
                {
                    suiteResult = await suiteOrchestrator.ExecuteAsync(
                        suite,
                        suiteRunRequest,
                        plan.Manifest.Id,
                        plan.Manifest.Version,
                        groupRunId,
                        suiteIdentity,
                        groupRunFolder,
                        plan.Manifest);  // Pass plan manifest for env resolution
                }

                if (suiteResult.Status == RunStatus.RebootRequired && suiteOrchestrator.LastRebootContext is not null)
                {
                    await HandlePlanRebootAsync(groupRunId, groupRunFolder, suiteIndex, suiteOrchestrator.LastRebootContext);
                    return new GroupResult
                    {
                        SchemaVersion = "1.5.0",
                        RunType = RunType.TestPlan,
                        RunId = groupRunId,
                        PlanId = plan.Manifest.Id,
                        PlanVersion = plan.Manifest.Version,
                        Status = RunStatus.RebootRequired,
                        StartTime = startTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        EndTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        Message = "Reboot requested by test case in suite.",
                        Reboot = suiteResult.Reboot
                    };
                }

                childResults.Add(suiteResult);
                if (!string.IsNullOrWhiteSpace(suiteResult.RunId))
                {
                    if (!childRunIds.Contains(suiteResult.RunId))
                    {
                        childRunIds.Add(suiteResult.RunId);
                    }
                }
                UpdateCounts(statusCounts, suiteResult.Status);

                // Append to children.jsonl
                await folderManager.AppendChildAsync(groupRunFolder, new ChildEntry
                {
                    RunId = suiteResult.RunId ?? "",
                    SuiteId = suite.Manifest.Id,
                    SuiteVersion = suite.Manifest.Version,
                    Status = suiteResult.Status
                });

                // Report node finished
                _reporter.OnNodeFinished(groupRunId, new NodeFinishedState
                {
                    NodeId = suiteIdentity,
                    Status = suiteResult.Status,
                    StartTime = suiteStartTime,
                    EndTime = DateTime.UtcNow,
                    Message = suiteResult.Message
                });
            }

            // Compute aggregate status
            var endTime = DateTime.UtcNow;
            var aggregateStatus = ComputeAggregateStatus(childResults, _cancellationToken.IsCancellationRequested);

            statusCounts.Total = childResults.Count;

            var result = new GroupResult
            {
                SchemaVersion = "1.5.0",
                RunType = RunType.TestPlan,
                RunId = groupRunId,
                PlanId = plan.Manifest.Id,
                PlanVersion = plan.Manifest.Version,
                Status = aggregateStatus,
                StartTime = startTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                EndTime = endTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Counts = statusCounts,
                ChildRunIds = childRunIds
            };

            await folderManager.WriteResultAsync(groupRunFolder, result);

            // Record plan completed event
            await folderManager.AppendEventAsync(groupRunFolder, new EventEntry
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
                Code = "TestPlan.Completed",
                Level = aggregateStatus == RunStatus.Passed ? "info" : "warning",
                Message = $"Test plan '{plan.Manifest.Id}' execution completed with status: {aggregateStatus}",
                Data = new Dictionary<string, object?>
                {
                    ["planId"] = plan.Manifest.Id,
                    ["planVersion"] = plan.Manifest.Version,
                    ["runId"] = groupRunId,
                    ["status"] = aggregateStatus.ToString(),
                    ["duration"] = (endTime - startTime).TotalSeconds,
                    ["totalSuites"] = plan.Manifest.Suites.Count,
                    ["passedSuites"] = statusCounts.Passed,
                    ["failedSuites"] = statusCounts.Failed
                }
            });

            // Append Plan run to index
            folderManager.AppendIndexEntry(new IndexEntry
            {
                RunId = groupRunId,
                RunType = RunType.TestPlan,
                PlanId = plan.Manifest.Id,
                PlanVersion = plan.Manifest.Version,
                StartTime = result.StartTime,
                EndTime = result.EndTime,
                Status = result.Status
            });

            // Report run finished
            _reporter.OnRunFinished(groupRunId, aggregateStatus);

            return result;
        }
        catch (Exception ex)
        {
            var endTime = DateTime.UtcNow;
            var result = new GroupResult
            {
                SchemaVersion = "1.5.0",
                RunType = RunType.TestPlan,
                RunId = groupRunId,
                PlanId = plan.Manifest.Id,
                PlanVersion = plan.Manifest.Version,
                Status = RunStatus.Error,
                StartTime = startTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                EndTime = endTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ChildRunIds = childRunIds,
                Message = ex.Message
            };

            await folderManager.WriteResultAsync(groupRunFolder, result);

            // Append Plan run to index
            folderManager.AppendIndexEntry(new IndexEntry
            {
                RunId = groupRunId,
                RunType = RunType.TestPlan,
                PlanId = plan.Manifest.Id,
                PlanVersion = plan.Manifest.Version,
                StartTime = result.StartTime,
                EndTime = result.EndTime,
                Status = result.Status
            });

            // Report run finished with error
            _reporter.OnRunFinished(groupRunId, RunStatus.Error);

            return result;
        }
    }

    private static GroupResult CreateSuiteErrorResult(
        string identity,
        TestPlanManifest plan,
        string message)
    {
        var parseResult = IdentityParser.Parse(identity);
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        return new GroupResult
        {
            SchemaVersion = "1.5.0",
            RunType = RunType.TestSuite,
            RunId = null,
            SuiteId = parseResult.Success ? parseResult.Id : identity,
            SuiteVersion = parseResult.Success ? parseResult.Version : "unknown",
            PlanId = plan.Id,
            PlanVersion = plan.Version,
            Status = RunStatus.Error,
            StartTime = now,
            EndTime = now,
            Message = message
        };
    }

    private async Task HandlePlanRebootAsync(
        string groupRunId,
        string groupRunFolder,
        int suiteIndex,
        SuiteOrchestrator.SuiteRebootContext rebootContext)
    {
        var resumeToken = Guid.NewGuid().ToString("N");
        var session = new RebootResumeSession
        {
            RunId = groupRunId,
            EntityType = "TestPlan",
            CurrentNodeIndex = suiteIndex,
            CurrentIteration = 0,
            NextPhase = rebootContext.NextPhase,
            ResumeToken = resumeToken,
            ResumeCount = 0,
            State = "PendingResume",
            RunFolder = groupRunFolder,
            ChildRunId = rebootContext.SuiteRunId,
            ChildNodeIndex = rebootContext.NodeIndex,
            ChildIteration = rebootContext.Iteration,
            Context = rebootContext.ResumeContext,
            CasesRoot = _discovery.ResolvedTestCaseRoot,
            SuitesRoot = _discovery.ResolvedTestSuiteRoot,
            PlansRoot = _discovery.ResolvedTestPlanRoot,
            AssetsRoot = _assetsRoot
        };

        await session.SaveAsync();
        var runnerExecutablePath = ResolveRunnerExecutablePath();
        ResumeTaskScheduler.CreateResumeTask(groupRunId, resumeToken, runnerExecutablePath, _runsRoot);
        RebootExecutor.RestartMachine(rebootContext.DelaySec);
        Environment.Exit(0);
    }

    private static void UpdateCounts(StatusCounts counts, RunStatus status)
    {
        switch (status)
        {
            case RunStatus.Planned:
            case RunStatus.Running:
            case RunStatus.RebootRequired:
                break;
            case RunStatus.Passed: counts.Passed++; break;
            case RunStatus.Failed: counts.Failed++; break;
            case RunStatus.Error: counts.Error++; break;
            case RunStatus.Timeout: counts.Timeout++; break;
            case RunStatus.Aborted: counts.Aborted++; break;
        }
    }

    private static (List<GroupResult> Results, List<string> RunIds, StatusCounts Counts) LoadExistingPlanState(string groupRunFolder)
    {
        var results = new List<GroupResult>();
        var runIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var counts = new StatusCounts();

        var childrenPath = Path.Combine(groupRunFolder, "children.jsonl");
        if (!File.Exists(childrenPath))
        {
            return (results, runIds.ToList(), counts);
        }

        var latestStatus = new Dictionary<string, RunStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(childrenPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonDefaults.Deserialize<ChildEntry>(line);
                if (entry is null || string.IsNullOrWhiteSpace(entry.RunId))
                {
                    continue;
                }

                runIds.Add(entry.RunId);
                latestStatus[entry.RunId] = entry.Status;
            }
            catch
            {
                // Ignore malformed lines.
            }
        }

        foreach (var status in latestStatus.Values)
        {
            if (status is RunStatus.Planned or RunStatus.Running or RunStatus.RebootRequired)
            {
                continue;
            }

            results.Add(new GroupResult { Status = status });
            UpdateCounts(counts, status);
        }

        return (results, runIds.ToList(), counts);
    }

    private static RunStatus ComputeAggregateStatus(List<GroupResult> results, bool wasAborted)
    {
        if (wasAborted)
            return RunStatus.Aborted;

        if (results.Any(r => r.Status == RunStatus.RebootRequired))
            return RunStatus.RebootRequired;
        if (results.Any(r => r.Status == RunStatus.Error))
            return RunStatus.Error;
        if (results.Any(r => r.Status == RunStatus.Timeout))
            return RunStatus.Timeout;
        if (results.Any(r => r.Status == RunStatus.Failed))
            return RunStatus.Failed;
        if (results.Any(r => r.Status == RunStatus.Aborted))
            return RunStatus.Aborted;

        return RunStatus.Passed;
    }

    private static string ResolveRunnerExecutablePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var cliPath = Path.Combine(baseDir, "PcTest.Cli.exe");
        return File.Exists(cliPath) ? cliPath : (Environment.ProcessPath ?? string.Empty);
    }
}
