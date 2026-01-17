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
        return await ExecuteInternalAsync(plan, runRequest, null);
    }

    public async Task<GroupResult> ResumeAsync(
        RebootResumeSession session,
        DiscoveredTestPlan plan)
    {
        if (session.PlanContext is null)
        {
            throw new InvalidOperationException("Plan resume context missing.");
        }

        return await ExecuteInternalAsync(plan, session.PlanContext.RunRequest, session);
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

        var startTime = DateTime.Now;
        var groupRunId = resumeSession?.RunId ?? GroupRunFolderManager.GenerateGroupRunId("P");
        
        // Preserve original startTime when resuming from reboot by reading from events.jsonl
        if (resumeSession is not null)
        {
            var eventsPath = Path.Combine(resumeSession.RunFolder, "events.jsonl");
            if (File.Exists(eventsPath))
            {
                try
                {
                    // Read first TestPlan.Started event to get original startTime
                    var lines = await File.ReadAllLinesAsync(eventsPath);
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        
                        if (root.TryGetProperty("code", out var code) && 
                            code.GetString() == "TestPlan.Started" &&
                            root.TryGetProperty("timestamp", out var timestamp))
                        {
                            var timestampStr = timestamp.GetString();
                            if (!string.IsNullOrEmpty(timestampStr) &&
                                DateTime.TryParse(timestampStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedStartTime))
                            {
                                startTime = parsedStartTime.Kind == DateTimeKind.Utc ? parsedStartTime : parsedStartTime.ToUniversalTime();
                                break; // Use first occurrence
                            }
                        }
                    }
                }
                catch
                {
                    // If we can't read events, use current time
                }
            }
        }
        var folderManager = new GroupRunFolderManager(_runsRoot);
        var groupRunFolder = resumeSession is null
            ? folderManager.CreateGroupRunFolder(groupRunId)
            : folderManager.GetExistingGroupRunFolder(resumeSession.RunFolder);

        var childRunIds = new List<string>();
        var childResults = new List<GroupResult>();
        var statusCounts = new StatusCounts();
        var isResuming = resumeSession is not null;
        var startNodeIndex = resumeSession?.CurrentNodeIndex ?? 0;

        if (isResuming && (startNodeIndex < 0 || startNodeIndex >= plan.Manifest.TestSuites.Count))
        {
            throw new InvalidOperationException($"Invalid resume node index {startNodeIndex} for plan '{plan.Manifest.Id}'.");
        }

        if (isResuming)
        {
            foreach (var entry in LoadLatestChildEntries(groupRunFolder, true))
            {
                childRunIds.Add(entry.RunId);
                childResults.Add(new GroupResult { Status = entry.Status });
                UpdateCounts(statusCounts, entry.Status);
            }
        }

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
                ResolvedAt = DateTime.Now.ToString("o"),
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

            if (isResuming)
            {
                await folderManager.AppendEventAsync(groupRunFolder, new EventEntry
                {
                    Timestamp = DateTime.Now.ToString("o"),
                    Code = "TestPlan.Resumed",
                    Level = "info",
                    Message = $"Test plan '{plan.Manifest.Id}' (version {plan.Manifest.Version}) execution resumed after reboot",
                    Data = new Dictionary<string, object?>
                    {
                        ["planId"] = plan.Manifest.Id,
                        ["planVersion"] = plan.Manifest.Version,
                        ["runId"] = groupRunId,
                        ["suiteCount"] = plan.Manifest.TestSuites.Count,
                        ["currentNodeIndex"] = resumeSession?.CurrentNodeIndex
                    }
                });
            }
            else
            {
                // Record plan started event
                await folderManager.AppendEventAsync(groupRunFolder, new EventEntry
                {
                    Timestamp = DateTime.Now.ToString("o"),
                    Code = "TestPlan.Started",
                    Level = "info",
                    Message = $"Test plan '{plan.Manifest.Id}' (version {plan.Manifest.Version}) execution started",
                    Data = new Dictionary<string, object?>
                    {
                        ["planId"] = plan.Manifest.Id,
                        ["planVersion"] = plan.Manifest.Version,
                        ["runId"] = groupRunId,
                        ["suiteCount"] = plan.Manifest.TestSuites.Count
                    }
                });
            }

            // Report planned nodes - plan level shows suites as nodes
            var plannedNodes = new List<PlannedNode>();
            foreach (var suiteNode in plan.Manifest.TestSuites)
            {
                var parseResult = IdentityParser.Parse(suiteNode.NodeId);
                plannedNodes.Add(new PlannedNode
                {
                    NodeId = suiteNode.NodeId,
                    TestId = parseResult.Success ? parseResult.Id : suiteNode.NodeId,
                    TestVersion = parseResult.Success ? parseResult.Version : "unknown",
                    NodeType = RunType.TestSuite,
                    ReferenceName = string.IsNullOrWhiteSpace(suiteNode.Ref) ? null : suiteNode.Ref
                });
            }
            _reporter.OnRunPlanned(groupRunId, RunType.TestPlan, plannedNodes);

            // Execute each Suite in order per spec section 6.4
            for (var suiteIndex = 0; suiteIndex < plan.Manifest.TestSuites.Count; suiteIndex++)
            {
                if (suiteIndex < startNodeIndex)
                {
                    continue;
                }

                var suiteNode = plan.Manifest.TestSuites[suiteIndex];
                var suiteIdentity = suiteNode.NodeId;
                if (_cancellationToken.IsCancellationRequested)
                    break;

                // Report node started
                _reporter.OnNodeStarted(groupRunId, suiteIdentity);

                var suiteStartTime = DateTime.Now;

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
                        EndTime = DateTime.Now,
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

                // Execute Suite with optional control overrides from the plan node
                var suiteOrchestrator = new SuiteOrchestrator(_discovery, _runsRoot, _assetsRoot, _reporter, _cancellationToken);
                var suiteRunId = isResuming && suiteIndex == startNodeIndex && resumeSession?.CurrentChildRunId is not null
                    ? resumeSession.CurrentChildRunId
                    : GroupRunFolderManager.GenerateGroupRunId("S");
                GroupResult suiteResult;

                if (isResuming && suiteIndex == startNodeIndex && resumeSession?.CurrentChildRunId is not null)
                {
                    var suiteSession = await RebootResumeSession.LoadAsync(
                        Path.Combine(_runsRoot, resumeSession.CurrentChildRunId));
                    suiteResult = await suiteOrchestrator.ResumeAsync(suiteSession, suite);
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
                        plan.Manifest,
                        suiteRunId,
                        suiteNode.Controls);  // Pass plan-level control overrides
                }

                // Append to children.jsonl
                await folderManager.AppendChildAsync(groupRunFolder, new ChildEntry
                {
                    RunId = suiteResult.ChildRunIds.FirstOrDefault() ?? "",
                    SuiteId = suite.Manifest.Id,
                    SuiteVersion = suite.Manifest.Version,
                    Status = suiteResult.Status
                });

                if (suiteResult.Status == RunStatus.RebootRequired)
                {
                    await HandlePlanRebootAsync(
                        groupRunFolder,
                        groupRunId,
                        plan,
                        runRequest,
                        suiteIndex,
                        suiteIdentity,
                        suiteRunId,
                        suiteResult.Reboot);

                    _reporter.OnNodeFinished(groupRunId, new NodeFinishedState
                    {
                        NodeId = suiteIdentity,
                        Status = RunStatus.RebootRequired,
                        StartTime = suiteStartTime,
                        EndTime = DateTime.Now,
                        Message = suiteResult.Reboot?.Reason
                    });

                    return new GroupResult
                    {
                        SchemaVersion = "1.5.0",
                        RunType = RunType.TestPlan,
                        PlanId = plan.Manifest.Id,
                        PlanVersion = plan.Manifest.Version,
                        Status = RunStatus.RebootRequired,
                        StartTime = startTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        EndTime = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        Counts = statusCounts,
                        ChildRunIds = childRunIds,
                        Reboot = suiteResult.Reboot
                    };
                }

                childResults.Add(suiteResult);
                childRunIds.AddRange(suiteResult.ChildRunIds);
                UpdateCounts(statusCounts, suiteResult.Status);

                // Report node finished
                _reporter.OnNodeFinished(groupRunId, new NodeFinishedState
                {
                    NodeId = suiteIdentity,
                    Status = suiteResult.Status,
                    StartTime = suiteStartTime,
                    EndTime = DateTime.Now,
                    Message = suiteResult.Message
                });
            }

            // Compute aggregate status
            var endTime = DateTime.Now;
            var aggregateStatus = ComputeAggregateStatus(childResults, _cancellationToken.IsCancellationRequested);

            statusCounts.Total = childResults.Count;

            var result = new GroupResult
            {
                SchemaVersion = "1.5.0",
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
                Timestamp = DateTime.Now.ToString("o"),
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
                    ["totalSuites"] = plan.Manifest.TestSuites.Count,
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
            var endTime = DateTime.Now;
            var result = new GroupResult
            {
                SchemaVersion = "1.5.0",
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
        var now = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ");

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
            case RunStatus.RebootRequired: break;
        }
    }

    private static RunStatus ComputeAggregateStatus(List<GroupResult> results, bool wasAborted)
    {
        if (wasAborted)
            return RunStatus.Aborted;

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

    private async Task HandlePlanRebootAsync(
        string groupRunFolder,
        string groupRunId,
        DiscoveredTestPlan plan,
        RunRequest runRequest,
        int suiteIndex,
        string suiteIdentity,
        string suiteRunId,
        RebootInfo? rebootInfo)
    {
        var resumeToken = Guid.NewGuid().ToString("N");
        var session = new RebootResumeSession
        {
            RunId = groupRunId,
            EntityType = "TestPlan",
            State = "PendingResume",
            CurrentNodeIndex = suiteIndex,
            NextPhase = rebootInfo?.NextPhase ?? 1,
            ResumeCount = 0,
            ResumeToken = resumeToken,
            CurrentNodeId = suiteIdentity,
            CurrentChildRunId = suiteRunId,
            OriginTestId = rebootInfo?.OriginTestId,
            RunFolder = groupRunFolder,
            PlanContext = new PlanResumeContext
            {
                PlanIdentity = plan.Identity,
                RunRequest = runRequest
            },
            Paths = BuildResumePaths()
        };

        await session.SaveAsync();

        var runnerExecutablePath = ResolveRunnerExecutablePath();
        if (string.IsNullOrWhiteSpace(runnerExecutablePath))
        {
            throw new InvalidOperationException("Runner executable path could not be determined.");
        }

        // Top-level plan orchestrator handles reboot/resume scheduling.
        ResumeTaskScheduler.CreateResumeTask(groupRunId, resumeToken, runnerExecutablePath, _runsRoot);
        RebootExecutor.RestartMachine(rebootInfo?.DelaySec);
        Environment.Exit(0);
    }

    private static IEnumerable<ChildEntry> LoadLatestChildEntries(string groupRunFolder, bool ignoreRebootRequired)
    {
        var path = Path.Combine(groupRunFolder, "children.jsonl");
        if (!File.Exists(path))
        {
            yield break;
        }

        var latest = new Dictionary<string, ChildEntry>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonDefaults.Deserialize<ChildEntry>(line);
                if (entry is null)
                {
                    continue;
                }

                if (ignoreRebootRequired && entry.Status == RunStatus.RebootRequired)
                {
                    continue;
                }

                latest[entry.RunId] = entry;
            }
            catch
            {
                // Ignore malformed lines.
            }
        }

        foreach (var entry in latest.Values)
        {
            yield return entry;
        }
    }

    private ResumePaths BuildResumePaths()
    {
        return new ResumePaths
        {
            TestCasesRoot = _discovery.ResolvedTestCaseRoot,
            TestSuitesRoot = _discovery.ResolvedTestSuiteRoot,
            TestPlansRoot = _discovery.ResolvedTestPlanRoot,
            AssetsRoot = _assetsRoot,
            RunsRoot = _runsRoot
        };
    }

    private static string ResolveRunnerExecutablePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var cliPath = Path.Combine(baseDir, "PcTest.Cli.exe");
        return File.Exists(cliPath) ? cliPath : (Environment.ProcessPath ?? string.Empty);
    }
}
