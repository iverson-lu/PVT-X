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
            ? resumeSession!.RunFolder
            : folderManager.CreateGroupRunFolder(groupRunId);

        var childRunIds = new List<string>();
        var childResults = new List<GroupResult>();
        var statusCounts = new StatusCounts();
        var resumeNodeIndex = isResume ? resumeSession!.CurrentNodeIndex : 0;

        try
        {
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
            if (!isResume || !File.Exists(Path.Combine(groupRunFolder, "manifest.json")))
            {
                await folderManager.WriteManifestAsync(groupRunFolder, manifestSnapshot);
            }

            // Compute effective environment
            var envResolver = new EnvironmentResolver();

            if (!isResume || !File.Exists(Path.Combine(groupRunFolder, "runRequest.json")))
            {
                await folderManager.WriteRunRequestAsync(groupRunFolder, runRequest);
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

            if (isResume)
            {
                LoadExistingPlanProgress(groupRunFolder, childResults, statusCounts);
            }

            // Execute each Suite in order per spec section 6.4
            for (var suiteIndex = 0; suiteIndex < plan.Manifest.Suites.Count; suiteIndex++)
            {
                var suiteIdentity = plan.Manifest.Suites[suiteIndex];
                if (_cancellationToken.IsCancellationRequested)
                    break;

                if (isResume && suiteIndex < resumeNodeIndex)
                {
                    continue;
                }

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
                var suiteResumeSession = isResume && suiteIndex == resumeNodeIndex
                    ? BuildSuiteResumeSession(resumeSession, suiteIdentity)
                    : null;

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
                    var resumeToken = Guid.NewGuid().ToString("N");
                    var rebootInfo = suiteResult.Reboot ?? new RebootInfo
                    {
                        NextPhase = resumeSession?.NextPhase ?? 1,
                        Reason = suiteResult.Message ?? "Reboot requested"
                    };

                    var session = new RebootResumeSession
                    {
                        RunId = groupRunId,
                        EntityType = "TestPlan",
                        State = "PendingResume",
                        CurrentNodeIndex = suiteIndex,
                        NextPhase = rebootInfo.NextPhase,
                        ResumeToken = resumeToken,
                        ResumeCount = 0,
                        RunFolder = groupRunFolder,
                        PlanContext = new PlanResumeContext
                        {
                            PlanIdentity = plan.Identity,
                            RunRequest = runRequest,
                            RunsRoot = _runsRoot,
                            AssetsRoot = _assetsRoot,
                            CasesRoot = _discovery.ResolvedTestCaseRoot,
                            SuitesRoot = _discovery.ResolvedTestSuiteRoot,
                            PlansRoot = _discovery.ResolvedTestPlanRoot,
                            CurrentSuiteIdentity = suiteIdentity,
                            CurrentSuiteRunId = suiteResult.RunId,
                            CurrentSuiteCaseIndex = suiteResult.Reboot?.OriginNodeIndex,
                            CurrentSuiteNodeId = suiteResult.Reboot?.OriginNodeId
                        }
                    };
                    await session.SaveAsync();

                    ResumeTaskScheduler.CreateResumeTask(groupRunId, resumeToken, ResolveRunnerExecutablePath(), _runsRoot);
                    RebootExecutor.RestartMachine(rebootInfo.DelaySec);
                    Environment.Exit(0);
                }

                childResults.Add(suiteResult);
                childRunIds.AddRange(suiteResult.ChildRunIds);
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
                RunId = groupRunId,
                RunType = RunType.TestPlan,
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
                RunId = groupRunId,
                RunType = RunType.TestPlan,
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
        }
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

    private static void LoadExistingPlanProgress(
        string groupRunFolder,
        List<GroupResult> childResults,
        StatusCounts statusCounts)
    {
        var path = Path.Combine(groupRunFolder, "children.jsonl");
        if (!File.Exists(path))
        {
            return;
        }

        var latestByRunId = new Dictionary<string, ChildEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
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

                latestByRunId[entry.RunId] = entry;
            }
            catch
            {
                // Ignore malformed lines.
            }
        }

        foreach (var entry in latestByRunId.Values)
        {
            if (entry.Status is RunStatus.Passed or RunStatus.Failed or RunStatus.Error or RunStatus.Timeout or RunStatus.Aborted)
            {
                childResults.Add(new GroupResult { Status = entry.Status });
                UpdateCounts(statusCounts, entry.Status);
            }
        }
    }

    private static RebootResumeSession? BuildSuiteResumeSession(
        RebootResumeSession? resumeSession,
        string suiteIdentity)
    {
        if (resumeSession?.PlanContext is null ||
            !string.Equals(resumeSession.PlanContext.CurrentSuiteIdentity, suiteIdentity, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(resumeSession.PlanContext.CurrentSuiteRunId) ||
            resumeSession.PlanContext.CurrentSuiteCaseIndex is null)
        {
            throw new InvalidOperationException("Plan resume session is missing suite reboot context.");
        }

        var suiteRunId = resumeSession.PlanContext.CurrentSuiteRunId;
        var suiteRunFolder = Path.Combine(resumeSession.PlanContext.RunsRoot, suiteRunId);

        return new RebootResumeSession
        {
            RunId = suiteRunId,
            EntityType = "TestSuite",
            State = "PendingResume",
            CurrentNodeIndex = resumeSession.PlanContext.CurrentSuiteCaseIndex.Value,
            NextPhase = resumeSession.NextPhase,
            ResumeToken = resumeSession.ResumeToken,
            ResumeCount = resumeSession.ResumeCount,
            RunFolder = suiteRunFolder
        };
    }

    private static string ResolveRunnerExecutablePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var cliPath = Path.Combine(baseDir, "PcTest.Cli.exe");
        return File.Exists(cliPath) ? cliPath : (Environment.ProcessPath ?? string.Empty);
    }
}
