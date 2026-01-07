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
        RunRequest runRequest,
        RebootResumeSession? resumeSession = null)
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
        var isResume = resumeSession is not null;
        var groupRunId = isResume ? resumeSession!.RunId : GroupRunFolderManager.GenerateGroupRunId("P");
        var folderManager = new GroupRunFolderManager(_runsRoot);
        var groupRunFolder = isResume
            ? folderManager.GetExistingGroupRunFolder(resumeSession!.RunFolder)
            : folderManager.CreateGroupRunFolder(groupRunId);
        var resumeNodeIndex = isResume ? resumeSession!.CurrentNodeIndex : 0;
        var resumeSuiteRunId = isResume ? resumeSession!.CurrentChildRunId : null;

        var childRunIds = new List<string>();
        var childResults = new List<GroupResult>();
        var statusCounts = new StatusCounts();

        if (isResume && string.IsNullOrEmpty(resumeSuiteRunId))
        {
            throw new InvalidOperationException("Resume session is missing the current suite run ID.");
        }

        try
        {
            // Write initial artifacts
            if (!isResume)
            {
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
                await folderManager.WriteManifestAsync(groupRunFolder, manifestSnapshot);
                await folderManager.WriteRunRequestAsync(groupRunFolder, runRequest);
            }

            // Compute effective environment
            var envResolver = new EnvironmentResolver();

            if (isResume)
            {
                await folderManager.AppendEventAsync(groupRunFolder, new EventEntry
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Code = "TestPlan.Resumed",
                    Level = "info",
                    Message = $"Test plan '{plan.Manifest.Id}' (version {plan.Manifest.Version}) execution resumed after reboot",
                    Data = new Dictionary<string, object?>
                    {
                        ["planId"] = plan.Manifest.Id,
                        ["planVersion"] = plan.Manifest.Version,
                        ["runId"] = groupRunId,
                        ["resumeFromNodeIndex"] = resumeNodeIndex
                    }
                });
            }

            // Record plan started event
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

            if (!isResume)
            {
                // Report planned nodes - plan level shows suites as nodes
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

            if (isResume)
            {
                LoadExistingPlanState(_runsRoot, groupRunFolder, childRunIds, childResults, statusCounts, resumeSuiteRunId);
            }

            // Execute each Suite in order per spec section 6.4
            for (var suiteIndex = resumeNodeIndex; suiteIndex < plan.Manifest.Suites.Count; suiteIndex++)
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
                RebootResumeSession? suiteResumeSession = null;
                var isResumeSuite = isResume && suiteIndex == resumeNodeIndex && !string.IsNullOrEmpty(resumeSuiteRunId);
                if (isResumeSuite && resumeSuiteRunId is not null)
                {
                    var suiteRunFolder = Path.Combine(_runsRoot, resumeSuiteRunId);
                    suiteResumeSession = await RebootResumeSession.LoadAsync(suiteRunFolder);
                }

                var suiteResult = await suiteOrchestrator.ExecuteAsync(
                    suite,
                    suiteRunRequest,
                    plan.Manifest.Id,
                    plan.Manifest.Version,
                    groupRunId,
                    suiteIdentity,
                    groupRunFolder,
                    plan.Manifest,
                    suiteResumeSession);  // Pass plan manifest for env resolution

                if (suiteResult.Status == RunStatus.RebootRequired)
                {
                    _reporter.OnNodeFinished(groupRunId, new NodeFinishedState
                    {
                        NodeId = suiteIdentity,
                        Status = RunStatus.RebootRequired,
                        StartTime = suiteStartTime,
                        EndTime = DateTime.UtcNow,
                        Message = suiteResult.Reboot?.Reason
                    });

                    await folderManager.AppendChildAsync(groupRunFolder, new ChildEntry
                    {
                        RunId = suiteResult.RunId ?? "",
                        SuiteId = suite.Manifest.Id,
                        SuiteVersion = suite.Manifest.Version,
                        Status = RunStatus.RebootRequired
                    });

                    await HandlePlanRebootAsync(
                        plan,
                        groupRunId,
                        groupRunFolder,
                        suiteIndex,
                        suiteIdentity,
                        suiteResult.RunId,
                        suiteResult.Reboot);

                    var rebootResult = BuildRebootResult(
                        plan,
                        groupRunId,
                        startTime,
                        childRunIds,
                        childResults,
                        statusCounts,
                        suiteResult.Reboot);
                    await folderManager.WriteResultAsync(groupRunFolder, rebootResult);
                    return rebootResult;
                }

                childResults.Add(suiteResult);
                foreach (var childRunId in suiteResult.ChildRunIds)
                {
                    TrackChildRunId(childRunIds, childRunId);
                }
                UpdateCounts(statusCounts, suiteResult.Status);

                // Append to children.jsonl
                await folderManager.AppendChildAsync(groupRunFolder, new ChildEntry
                {
                    RunId = suiteResult.RunId ?? suiteResult.ChildRunIds.FirstOrDefault() ?? "",
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

    private static void UpdateCounts(StatusCounts counts, RunStatus status)
    {
        switch (status)
        {
            case RunStatus.Passed: counts.Passed++; break;
            case RunStatus.Failed: counts.Failed++; break;
            case RunStatus.Error: counts.Error++; break;
            case RunStatus.Timeout: counts.Timeout++; break;
            case RunStatus.Aborted: counts.Aborted++; break;
            case RunStatus.Planned:
            case RunStatus.Running:
            case RunStatus.RebootRequired:
                break;
        }
    }

    private static void TrackChildRunId(List<string> childRunIds, string runId)
    {
        if (!childRunIds.Contains(runId))
        {
            childRunIds.Add(runId);
        }
    }

    private static bool IsFinalStatus(RunStatus status)
    {
        return status == RunStatus.Passed
               || status == RunStatus.Failed
               || status == RunStatus.Error
               || status == RunStatus.Timeout
               || status == RunStatus.Aborted;
    }

    private static void LoadExistingPlanState(
        string runsRoot,
        string groupRunFolder,
        List<string> childRunIds,
        List<GroupResult> childResults,
        StatusCounts statusCounts,
        string? resumeSuiteRunId)
    {
        var childrenPath = Path.Combine(groupRunFolder, "children.jsonl");
        if (!File.Exists(childrenPath))
        {
            return;
        }

        var latestStatuses = new Dictionary<string, RunStatus>();
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

                latestStatuses[entry.RunId] = entry.Status;
            }
            catch
            {
                // Ignore malformed lines.
            }
        }

        foreach (var (runId, status) in latestStatuses)
        {
            if (string.Equals(runId, resumeSuiteRunId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!IsFinalStatus(status))
            {
                continue;
            }

            var suiteResultPath = Path.Combine(runsRoot, runId, "result.json");
            if (File.Exists(suiteResultPath))
            {
                try
                {
                    var json = File.ReadAllText(suiteResultPath);
                    var suiteResult = JsonDefaults.Deserialize<GroupResult>(json);
                    if (suiteResult?.ChildRunIds is not null)
                    {
                        foreach (var childRunId in suiteResult.ChildRunIds)
                        {
                            TrackChildRunId(childRunIds, childRunId);
                        }
                    }
                }
                catch
                {
                    // Ignore suite result read failures.
                }
            }

            childResults.Add(new GroupResult
            {
                RunType = RunType.TestSuite,
                Status = status
            });
            UpdateCounts(statusCounts, status);
        }
    }

    private async Task HandlePlanRebootAsync(
        DiscoveredTestPlan plan,
        string groupRunId,
        string groupRunFolder,
        int suiteIndex,
        string suiteIdentity,
        string? suiteRunId,
        RebootInfo? rebootInfo)
    {
        if (rebootInfo is null)
        {
            throw new InvalidOperationException("Reboot details are required to resume the plan.");
        }

        if (string.IsNullOrWhiteSpace(suiteRunId))
        {
            throw new InvalidOperationException("Suite run ID is required to resume a plan after reboot.");
        }

        var resumeToken = Guid.NewGuid().ToString("N");
        var session = new RebootResumeSession
        {
            RunId = groupRunId,
            EntityType = "TestPlan",
            EntityId = plan.Manifest.Id,
            CurrentNodeIndex = suiteIndex,
            CurrentNodeId = suiteIdentity,
            CurrentChildRunId = suiteRunId,
            NextPhase = rebootInfo.NextPhase,
            Reason = rebootInfo.Reason,
            OriginTestId = rebootInfo.OriginTestId,
            DelaySec = rebootInfo.DelaySec,
            ResumeToken = resumeToken,
            ResumeCount = 0,
            State = "PendingResume",
            RunFolder = groupRunFolder,
            GroupContext = new ResumeGroupContext
            {
                RunsRoot = _runsRoot,
                AssetsRoot = _assetsRoot,
                CasesRoot = _discovery.ResolvedTestCaseRoot,
                SuitesRoot = _discovery.ResolvedTestSuiteRoot,
                PlansRoot = _discovery.ResolvedTestPlanRoot
            }
        };

        await session.SaveAsync();

        var entry = new EventEntry
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            Code = "TestPlan.RebootRequested",
            Level = "warning",
            Message = $"Test plan '{plan.Manifest.Id}' requested a reboot.",
            Data = new Dictionary<string, object?>
            {
                ["planId"] = plan.Manifest.Id,
                ["planVersion"] = plan.Manifest.Version,
                ["runId"] = groupRunId,
                ["suiteIndex"] = suiteIndex,
                ["suiteIdentity"] = suiteIdentity,
                ["nextPhase"] = rebootInfo.NextPhase,
                ["reason"] = rebootInfo.Reason,
                ["originTestId"] = rebootInfo.OriginTestId
            }
        };

        await new GroupRunFolderManager(_runsRoot).AppendEventAsync(groupRunFolder, entry);

        ResumeTaskScheduler.CreateResumeTask(groupRunId, resumeToken, ResolveRunnerExecutablePath(), _runsRoot);
        RebootExecutor.RestartMachine(rebootInfo.DelaySec);
        Environment.Exit(0);
    }

    private static GroupResult BuildRebootResult(
        DiscoveredTestPlan plan,
        string groupRunId,
        DateTime startTime,
        List<string> childRunIds,
        List<GroupResult> childResults,
        StatusCounts statusCounts,
        RebootInfo? rebootInfo)
    {
        var endTime = DateTime.UtcNow;
        statusCounts.Total = childResults.Count;

        return new GroupResult
        {
            SchemaVersion = "1.5.0",
            RunType = RunType.TestPlan,
            RunId = groupRunId,
            PlanId = plan.Manifest.Id,
            PlanVersion = plan.Manifest.Version,
            Status = RunStatus.RebootRequired,
            StartTime = startTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            EndTime = endTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Counts = statusCounts,
            ChildRunIds = new List<string>(childRunIds),
            Reboot = rebootInfo
        };
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
