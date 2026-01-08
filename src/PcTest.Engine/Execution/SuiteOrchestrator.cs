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
/// Orchestrator for Test Suite execution per spec section 10.
/// </summary>
public sealed class SuiteOrchestrator
{
    private readonly DiscoveryResult _discovery;
    private readonly string _runsRoot;
    private readonly string _assetsRoot;
    private readonly IExecutionReporter _reporter;
    private readonly CancellationToken _cancellationToken;

    public SuiteOrchestrator(
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
    /// Executes a Suite with the given RunRequest.
    /// </summary>
    public async Task<GroupResult> ExecuteAsync(
        DiscoveredTestSuite suite,
        RunRequest runRequest,
        string? planId = null,
        string? planVersion = null,
        string? parentPlanRunId = null,
        string? parentNodeId = null,
        string? parentPlanRunFolder = null,
        TestPlanManifest? planManifest = null,
        string? suiteRunId = null)
    {
        return await ExecuteInternalAsync(
            suite,
            runRequest,
            planId,
            planVersion,
            parentPlanRunId,
            parentNodeId,
            parentPlanRunFolder,
            planManifest,
            suiteRunId,
            null);
    }

    public async Task<GroupResult> ResumeAsync(
        RebootResumeSession session,
        DiscoveredTestSuite suite)
    {
        if (session.SuiteContext is null)
        {
            throw new InvalidOperationException("Suite resume context missing.");
        }

        return await ExecuteInternalAsync(
            suite,
            session.SuiteContext.RunRequest,
            session.SuiteContext.PlanId,
            session.SuiteContext.PlanVersion,
            session.SuiteContext.ParentPlanRunId,
            session.SuiteContext.ParentNodeId,
            session.SuiteContext.ParentPlanRunFolder,
            session.SuiteContext.PlanManifest,
            session.RunId,
            session);
    }

    private async Task<GroupResult> ExecuteInternalAsync(
        DiscoveredTestSuite suite,
        RunRequest runRequest,
        string? planId,
        string? planVersion,
        string? parentPlanRunId,
        string? parentNodeId,
        string? parentPlanRunFolder,
        TestPlanManifest? planManifest,
        string? suiteRunId,
        RebootResumeSession? resumeSession)
    {
        var startTime = DateTime.UtcNow;
        var groupRunId = suiteRunId ?? GroupRunFolderManager.GenerateGroupRunId("S");
        var folderManager = new GroupRunFolderManager(_runsRoot);
        var groupRunFolder = resumeSession is null
            ? folderManager.CreateGroupRunFolder(groupRunId)
            : folderManager.GetExistingGroupRunFolder(resumeSession.RunFolder);

        var isResuming = resumeSession is not null;
        var childRunIds = new List<string>();
        var childResults = new List<TestCaseResult>();
        var statusCounts = new StatusCounts();

        if (isResuming)
        {
            foreach (var entry in LoadLatestChildEntries(groupRunFolder, true))
            {
                childRunIds.Add(entry.RunId);
                childResults.Add(new TestCaseResult { Status = entry.Status });
                UpdateCounts(statusCounts, entry.Status);
            }
        }

        try
        {
            // Write initial artifacts
            var manifestSnapshot = new GroupManifestSnapshot
            {
                SchemaVersion = "1.5.0",
                RunType = RunType.TestSuite,
                SuiteId = suite.Manifest.Id,
                SuiteVersion = suite.Manifest.Version,
                PlanId = planId,
                PlanVersion = planVersion,
                OriginalManifest = JsonSerializer.SerializeToElement(suite.Manifest, JsonDefaults.WriteOptions),
                ResolvedAt = DateTime.UtcNow.ToString("o"),
                EngineVersion = "1.0.0"
            };

            if (!isResuming || !File.Exists(Path.Combine(groupRunFolder, "manifest.json")))
            {
                await folderManager.WriteManifestAsync(groupRunFolder, manifestSnapshot);
            }

            var controls = suite.Manifest.Controls ?? new SuiteControls();
            if (!isResuming || !File.Exists(Path.Combine(groupRunFolder, "controls.json")))
            {
                await folderManager.WriteControlsAsync(groupRunFolder, controls);
            }

            // Compute effective environment
            var envResolver = new EnvironmentResolver();
            var effectiveEnv = planManifest != null
                ? envResolver.ComputeSuiteEnvironment(planManifest, suite.Manifest, runRequest.EnvironmentOverrides)
                : envResolver.ComputeSuiteEnvironment(suite.Manifest, runRequest.EnvironmentOverrides);
            if (!isResuming || !File.Exists(Path.Combine(groupRunFolder, "environment.json")))
            {
                await folderManager.WriteEnvironmentAsync(groupRunFolder, effectiveEnv);
            }

            if (!isResuming || !File.Exists(Path.Combine(groupRunFolder, "runRequest.json")))
            {
                await folderManager.WriteRunRequestAsync(groupRunFolder, runRequest);
            }

            if (isResuming)
            {
                await folderManager.AppendEventAsync(groupRunFolder, new EventEntry
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Code = "TestSuite.Resumed",
                    Level = "info",
                    Message = $"Test suite '{suite.Manifest.Id}' (version {suite.Manifest.Version}) execution resumed after reboot",
                    Data = new Dictionary<string, object?>
                    {
                        ["suiteId"] = suite.Manifest.Id,
                        ["suiteVersion"] = suite.Manifest.Version,
                        ["runId"] = groupRunId,
                        ["planId"] = planId,
                        ["planVersion"] = planVersion,
                        ["parentRunId"] = parentPlanRunId,
                        ["nextPhase"] = resumeSession?.NextPhase,
                        ["currentNodeIndex"] = resumeSession?.CurrentNodeIndex
                    }
                });
            }
            else
            {
                // Record suite started event
                await folderManager.AppendEventAsync(groupRunFolder, new EventEntry
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Code = "TestSuite.Started",
                    Level = "info",
                    Message = $"Test suite '{suite.Manifest.Id}' (version {suite.Manifest.Version}) execution started",
                    Data = new Dictionary<string, object?>
                    {
                        ["suiteId"] = suite.Manifest.Id,
                        ["suiteVersion"] = suite.Manifest.Version,
                        ["runId"] = groupRunId,
                        ["planId"] = planId,
                        ["planVersion"] = planVersion,
                        ["parentRunId"] = parentPlanRunId
                    }
                });
            }

            // Also write to parent plan folder if executing within a plan
            if (!string.IsNullOrEmpty(parentPlanRunFolder))
            {
                await folderManager.AppendEventAsync(parentPlanRunFolder, new EventEntry
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Code = isResuming ? "TestSuite.Resumed" : "TestSuite.Started",
                    Level = "info",
                    Message = isResuming
                        ? $"Test suite '{suite.Manifest.Id}' (version {suite.Manifest.Version}) execution resumed after reboot"
                        : $"Test suite '{suite.Manifest.Id}' (version {suite.Manifest.Version}) execution started",
                    Data = new Dictionary<string, object?>
                    {
                        ["suiteId"] = suite.Manifest.Id,
                        ["suiteVersion"] = suite.Manifest.Version,
                        ["runId"] = groupRunId,
                        ["planId"] = planId,
                        ["planVersion"] = planVersion,
                        ["parentRunId"] = parentPlanRunId,
                        ["nextPhase"] = resumeSession?.NextPhase,
                        ["currentNodeIndex"] = resumeSession?.CurrentNodeIndex
                    }
                });
            }

            // Log maxParallel warning if needed per spec section 6.5
            if (controls.MaxParallel > 1)
            {
                await folderManager.AppendEventAsync(groupRunFolder, new EventEntry
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Code = ErrorCodes.ControlsMaxParallelIgnored,
                    Level = "warning",
                    Message = $"maxParallel={controls.MaxParallel} is not supported; executing sequentially",
                    Location = "suite.manifest.json"
                });
            }

            // Resolve refs and execute nodes
            var refResolver = new SuiteRefResolver(_discovery.ResolvedTestCaseRoot);
            var inputResolver = new InputResolver();
            var runner = new TestCaseRunner(_cancellationToken);
            var runnerExecutablePath = ResolveRunnerExecutablePath();

            var repeat = Math.Max(1, controls.Repeat);
            var continueOnFailure = controls.ContinueOnFailure;
            var retryOnError = Math.Max(0, controls.RetryOnError);
            var startIteration = resumeSession?.SuiteContext?.CurrentIteration ?? 0;
            var startNodeIndex = resumeSession?.CurrentNodeIndex ?? 0;
            var resumeCaseContext = resumeSession?.CaseContext;
            var resumeCaseRunId = resumeSession?.CurrentChildRunId;

            if (isResuming && (startNodeIndex < 0 || startNodeIndex >= suite.Manifest.TestCases.Count))
            {
                throw new InvalidOperationException($"Invalid resume node index {startNodeIndex} for suite '{suite.Manifest.Id}'.");
            }

            // Flag to stop the entire pipeline (both repeat iterations and test case nodes)
            // when continueOnFailure=false and a non-Passed status occurs
            var shouldStopPipeline = false;

            // Report planned nodes (only report once for first iteration)
            // When under a plan, report with parent suite information for nested display
            var plannedNodes = new List<PlannedNode>();
            foreach (var node in suite.Manifest.TestCases)
            {
                var (testCaseManifest, _, _) = ResolveTestCase(node, suite.ManifestPath, refResolver);
                plannedNodes.Add(new PlannedNode
                {
                    NodeId = node.NodeId,
                    TestId = testCaseManifest?.Id ?? "unknown",
                    TestVersion = testCaseManifest?.Version ?? "unknown",
                    NodeType = RunType.TestCase,
                    ParentNodeId = parentNodeId
                });
            }
            _reporter.OnRunPlanned(
                string.IsNullOrEmpty(parentPlanRunId) ? groupRunId : parentPlanRunId, 
                RunType.TestSuite, 
                plannedNodes);

            for (var iteration = 0; iteration < repeat; iteration++)
            {
                // Check if pipeline should stop (continueOnFailure=false and a test failed)
                if (shouldStopPipeline)
                    break;

                if (iteration < startIteration)
                {
                    continue;
                }

                var nodeStartIndex = iteration == startIteration ? startNodeIndex : 0;

                for (var nodeIndex = nodeStartIndex; nodeIndex < suite.Manifest.TestCases.Count; nodeIndex++)
                {
                    if (_cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var node = suite.Manifest.TestCases[nodeIndex];
                    var isResumeNode = isResuming && iteration == startIteration && nodeIndex == startNodeIndex;

                    // Resolve test case: try NodeId first (as test case ID), then fall back to ref
                    var (testCaseManifest, testCasePath, refError) = ResolveTestCase(node, suite.ManifestPath, refResolver);

                    if (refError is not null || testCaseManifest is null || testCasePath is null)
                    {
                        // Create error result
                        var errorResult = CreateNodeErrorResult(
                            node.NodeId,
                            suite.Manifest,
                            planId, planVersion,
                            refError?.Message ?? "Ref resolution failed");
                        childResults.Add(errorResult);
                        UpdateCounts(statusCounts, errorResult.Status);

                        // Report node finished with error
                        _reporter.OnNodeFinished(
                            string.IsNullOrEmpty(parentPlanRunId) ? groupRunId : parentPlanRunId,
                            new NodeFinishedState
                            {
                                NodeId = node.NodeId,
                                Status = RunStatus.Error,
                                StartTime = DateTime.UtcNow,
                                EndTime = DateTime.UtcNow,
                                Message = refError?.Message ?? "Ref resolution failed",
                                ParentNodeId = parentNodeId
                            });

                        if (!continueOnFailure)
                        {
                            shouldStopPipeline = true;
                            break;
                        }
                        continue;
                    }

                    Dictionary<string, object?> effectiveInputs;
                    Dictionary<string, bool> secretInputs;
                    Dictionary<string, JsonElement>? inputTemplates;
                    Dictionary<string, string> caseEnvironment;

                    if (isResumeNode)
                    {
                        if (resumeCaseContext is null)
                        {
                            throw new InvalidOperationException("Resume context missing for rebooted case.");
                        }

                        testCaseManifest = resumeCaseContext.Manifest;
                        testCasePath = resumeCaseContext.TestCasePath;
                        effectiveInputs = resumeCaseContext.EffectiveInputs;
                        secretInputs = resumeCaseContext.SecretInputs;
                        inputTemplates = resumeCaseContext.InputTemplates;
                        caseEnvironment = resumeCaseContext.EffectiveEnvironment;
                    }
                    else
                    {
                        // Resolve inputs
                        var inputResult = inputResolver.ResolveSuiteTriggeredInputs(
                            testCaseManifest, node, runRequest.NodeOverrides, effectiveEnv);

                        if (!inputResult.Success)
                        {
                            var errorResult = CreateNodeErrorResult(
                                node.NodeId,
                                suite.Manifest,
                                planId, planVersion,
                                string.Join("; ", inputResult.Errors.Select(e => e.Message)));
                            childResults.Add(errorResult);
                            UpdateCounts(statusCounts, errorResult.Status);

                            // Report node finished with error
                            _reporter.OnNodeFinished(
                                string.IsNullOrEmpty(parentPlanRunId) ? groupRunId : parentPlanRunId,
                                new NodeFinishedState
                                {
                                    NodeId = node.NodeId,
                                    Status = RunStatus.Error,
                                    StartTime = DateTime.UtcNow,
                                    EndTime = DateTime.UtcNow,
                                    Message = string.Join("; ", inputResult.Errors.Select(e => e.Message)),
                                    ParentNodeId = parentNodeId
                                });

                            if (!continueOnFailure)
                            {
                                shouldStopPipeline = true;
                                break;
                            }
                            continue;
                        }

                        effectiveInputs = inputResult.EffectiveInputs;
                        secretInputs = inputResult.SecretInputs;
                        inputTemplates = inputResult.InputTemplates;
                        caseEnvironment = effectiveEnv;
                    }

                    // Execute with retry
                    TestCaseResult? nodeResult = null;
                    var attempts = 1 + retryOnError;

                    // Report node started
                    _reporter.OnNodeStarted(
                        string.IsNullOrEmpty(parentPlanRunId) ? groupRunId : parentPlanRunId,
                        node.NodeId);

                    var nodeStartTime = DateTime.UtcNow;

                    try
                    {
                        for (var attempt = 0; attempt < attempts; attempt++)
                        {
                            var isResumeAttempt = isResumeNode && attempt == 0;
                            var runId = isResumeAttempt
                                ? resumeCaseRunId ?? throw new InvalidOperationException("Resume run ID missing.")
                                : TestCaseRunner.GenerateRunId();

                            // Forward test case started event to suite events.jsonl
                            await folderManager.AppendEventAsync(groupRunFolder, new EventEntry
                            {
                                Timestamp = DateTime.UtcNow.ToString("o"),
                                Code = "TestCase.Started",
                                Level = "info",
                                Message = $"Test case '{testCaseManifest.Id}' (node '{node.NodeId}') execution started",
                                Data = new Dictionary<string, object?>
                                {
                                    ["nodeId"] = node.NodeId,
                                    ["testId"] = testCaseManifest.Id,
                                    ["testVersion"] = testCaseManifest.Version,
                                    ["runId"] = runId
                                }
                            });

                            // Also write to parent plan folder if executing within a plan
                            if (!string.IsNullOrEmpty(parentPlanRunFolder))
                            {
                                await folderManager.AppendEventAsync(parentPlanRunFolder, new EventEntry
                                {
                                    Timestamp = DateTime.UtcNow.ToString("o"),
                                    Code = "TestCase.Started",
                                    Level = "info",
                                    Message = $"Test case '{testCaseManifest.Id}' (node '{node.NodeId}') execution started",
                                    Data = new Dictionary<string, object?>
                                    {
                                        ["nodeId"] = node.NodeId,
                                        ["testId"] = testCaseManifest.Id,
                                        ["testVersion"] = testCaseManifest.Version,
                                        ["runId"] = runId
                                    }
                                });
                            }

                            RunContext context;
                            if (isResumeAttempt)
                            {
                                if (resumeCaseContext is null)
                                {
                                    throw new InvalidOperationException("Resume context missing for rebooted case.");
                                }

                                context = ResumeContextConverter.ToRunContext(
                                    resumeCaseContext,
                                    runId,
                                    resumeSession?.NextPhase ?? 1,
                                    true);
                            }
                            else
                            {
                                context = new RunContext
                                {
                                    RunId = runId,
                                    Phase = 0,
                                    Manifest = testCaseManifest,
                                    TestCasePath = testCasePath,
                                    EffectiveInputs = effectiveInputs,
                                    EffectiveEnvironment = caseEnvironment,
                                    SecretInputs = secretInputs,
                                    WorkingDir = suite.Manifest.Environment?.WorkingDir,
                                    TimeoutSec = testCaseManifest.TimeoutSec,
                                    RunsRoot = _runsRoot,
                                    AssetsRoot = _assetsRoot,
                                    NodeId = node.NodeId,
                                    SuiteId = suite.Manifest.Id,
                                    SuiteVersion = suite.Manifest.Version,
                                    PlanId = planId,
                                    PlanVersion = planVersion,
                                    ParentRunId = groupRunId,
                                    InputTemplates = inputTemplates,
                                    RunnerExecutablePath = runnerExecutablePath,
                                    IsTopLevel = false
                                };
                            }

                            nodeResult = await runner.ExecuteAsync(context);
                            childRunIds.Add(runId);

                            if (nodeResult.Status == RunStatus.RebootRequired)
                            {
                                await folderManager.AppendEventAsync(groupRunFolder, new EventEntry
                                {
                                    Timestamp = DateTime.UtcNow.ToString("o"),
                                    Code = "TestCase.RebootRequested",
                                    Level = "warning",
                                    Message = $"Test case '{testCaseManifest.Id}' (node '{node.NodeId}') requested a reboot.",
                                    Data = new Dictionary<string, object?>
                                    {
                                        ["nodeId"] = node.NodeId,
                                        ["testId"] = testCaseManifest.Id,
                                        ["testVersion"] = testCaseManifest.Version,
                                        ["runId"] = runId,
                                        ["nextPhase"] = nodeResult.Reboot?.NextPhase,
                                        ["reason"] = nodeResult.Reboot?.Reason
                                    }
                                });

                                if (!string.IsNullOrEmpty(parentPlanRunFolder))
                                {
                                    await folderManager.AppendEventAsync(parentPlanRunFolder, new EventEntry
                                    {
                                        Timestamp = DateTime.UtcNow.ToString("o"),
                                        Code = "TestCase.RebootRequested",
                                        Level = "warning",
                                        Message = $"Test case '{testCaseManifest.Id}' (node '{node.NodeId}') requested a reboot.",
                                        Data = new Dictionary<string, object?>
                                        {
                                            ["nodeId"] = node.NodeId,
                                            ["testId"] = testCaseManifest.Id,
                                            ["testVersion"] = testCaseManifest.Version,
                                            ["runId"] = runId,
                                            ["nextPhase"] = nodeResult.Reboot?.NextPhase,
                                            ["reason"] = nodeResult.Reboot?.Reason
                                        }
                                    });
                                }
                            }

                            // Forward test case events to suite events.jsonl
                            await folderManager.AppendEventAsync(groupRunFolder, new EventEntry
                            {
                                Timestamp = DateTime.UtcNow.ToString("o"),
                                Code = "TestCase.Completed",
                                Level = nodeResult.Status == RunStatus.Passed ? "info" : "warning",
                                Message = $"Test case '{testCaseManifest.Id}' (node '{node.NodeId}') completed with status: {nodeResult.Status}",
                                Data = new Dictionary<string, object?>
                                {
                                    ["nodeId"] = node.NodeId,
                                    ["testId"] = testCaseManifest.Id,
                                    ["testVersion"] = testCaseManifest.Version,
                                    ["runId"] = runId,
                                    ["status"] = nodeResult.Status.ToString(),
                                    ["exitCode"] = nodeResult.ExitCode
                                }
                            });

                            // Also write to parent plan folder if executing within a plan
                            if (!string.IsNullOrEmpty(parentPlanRunFolder))
                            {
                                await folderManager.AppendEventAsync(parentPlanRunFolder, new EventEntry
                                {
                                    Timestamp = DateTime.UtcNow.ToString("o"),
                                    Code = "TestCase.Completed",
                                    Level = nodeResult.Status == RunStatus.Passed ? "info" : "warning",
                                    Message = $"Test case '{testCaseManifest.Id}' (node '{node.NodeId}') completed with status: {nodeResult.Status}",
                                    Data = new Dictionary<string, object?>
                                    {
                                        ["nodeId"] = node.NodeId,
                                        ["testId"] = testCaseManifest.Id,
                                        ["testVersion"] = testCaseManifest.Version,
                                        ["runId"] = runId,
                                        ["status"] = nodeResult.Status.ToString(),
                                        ["exitCode"] = nodeResult.ExitCode
                                    }
                                });
                            }

                            // Append final status to children.jsonl after execution
                            await folderManager.AppendChildAsync(groupRunFolder, new ChildEntry
                            {
                                RunId = runId,
                                NodeId = node.NodeId,
                                TestId = testCaseManifest.Id,
                                TestVersion = testCaseManifest.Version,
                                Status = nodeResult.Status
                            });

                            // Append to index.jsonl
                            folderManager.AppendIndexEntry(new IndexEntry
                            {
                                RunId = runId,
                                RunType = RunType.TestCase,
                                NodeId = node.NodeId,
                                TestId = testCaseManifest.Id,
                                TestVersion = testCaseManifest.Version,
                                SuiteId = suite.Manifest.Id,
                                SuiteVersion = suite.Manifest.Version,
                                PlanId = planId,
                                PlanVersion = planVersion,
                                ParentRunId = groupRunId,
                                StartTime = nodeResult.StartTime,
                                EndTime = nodeResult.EndTime,
                                Status = nodeResult.Status
                            });

                            if (nodeResult.Status == RunStatus.RebootRequired)
                            {
                                await HandleSuiteRebootAsync(
                                    groupRunFolder,
                                    groupRunId,
                                    suite,
                                    runRequest,
                                    planId,
                                    planVersion,
                                    parentPlanRunId,
                                    parentNodeId,
                                    parentPlanRunFolder,
                                    planManifest,
                                    iteration,
                                    nodeIndex,
                                    node,
                                    runId,
                                    context,
                                    runnerExecutablePath,
                                    nodeResult.Reboot);

                                _reporter.OnNodeFinished(
                                    string.IsNullOrEmpty(parentPlanRunId) ? groupRunId : parentPlanRunId,
                                    new NodeFinishedState
                                    {
                                        NodeId = node.NodeId,
                                        Status = RunStatus.RebootRequired,
                                        StartTime = nodeStartTime,
                                        EndTime = DateTime.UtcNow,
                                        Message = nodeResult.Reboot?.Reason,
                                        RetryCount = attempt,
                                        ParentNodeId = parentNodeId
                                    });

                                return new GroupResult
                                {
                                    SchemaVersion = "1.5.0",
                                    RunType = RunType.TestSuite,
                                    SuiteId = suite.Manifest.Id,
                                    SuiteVersion = suite.Manifest.Version,
                                    PlanId = planId,
                                    PlanVersion = planVersion,
                                    Status = RunStatus.RebootRequired,
                                    StartTime = startTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                                    EndTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                                    Counts = statusCounts,
                                    ChildRunIds = childRunIds,
                                    Reboot = nodeResult.Reboot
                                };
                            }

                            // Per spec section 10: retry only on Error/Timeout
                            if (nodeResult.Status != RunStatus.Error && nodeResult.Status != RunStatus.Timeout)
                                break;

                            if (attempt < attempts - 1)
                            {
                                await folderManager.AppendEventAsync(groupRunFolder, new EventEntry
                                {
                                    Timestamp = DateTime.UtcNow.ToString("o"),
                                    Code = "Node.Retry",
                                    Level = "info",
                                    Message = $"Retrying node '{node.NodeId}' (attempt {attempt + 2}/{attempts})"
                                });
                            }
                        }

                        var nodeEndTime = DateTime.UtcNow;

                        if (nodeResult is not null)
                        {
                            childResults.Add(nodeResult);
                            UpdateCounts(statusCounts, nodeResult.Status);

                            // Report node finished
                            _reporter.OnNodeFinished(
                                string.IsNullOrEmpty(parentPlanRunId) ? groupRunId : parentPlanRunId,
                                new NodeFinishedState
                                {
                                    NodeId = node.NodeId,
                                    Status = nodeResult.Status,
                                    StartTime = nodeStartTime,
                                    EndTime = nodeEndTime,
                                    Message = nodeResult.Error?.Message,
                                    RetryCount = Math.Max(0, childRunIds.Count - 1),
                                    ParentNodeId = parentNodeId
                                });

                            // Check continue on failure
                            if (!continueOnFailure && nodeResult.Status != RunStatus.Passed)
                            {
                                shouldStopPipeline = true;
                                break;
                            }
                        }
                    }
                    catch (Exception nodeEx)
                    {
                        // CRITICAL: If exception occurs during node execution, we must call OnNodeFinished
                        // to prevent the node from being stuck in "Running" state forever
                        var nodeEndTime = DateTime.UtcNow;
                        
                        // Create error result for the failed node
                        var errorResult = CreateNodeErrorResult(
                            node.NodeId,
                            suite.Manifest,
                            planId, planVersion,
                            $"Node execution failed: {nodeEx.Message}");
                        
                        childResults.Add(errorResult);
                        UpdateCounts(statusCounts, errorResult.Status);

                        // Report node finished with error status (CRITICAL - must succeed)
                        _reporter.OnNodeFinished(
                            string.IsNullOrEmpty(parentPlanRunId) ? groupRunId : parentPlanRunId,
                            new NodeFinishedState
                            {
                                NodeId = node.NodeId,
                                Status = RunStatus.Error,
                                StartTime = nodeStartTime,
                                EndTime = nodeEndTime,
                                Message = nodeEx.Message,
                                RetryCount = 0,
                                ParentNodeId = parentNodeId
                            });

                        // Try to log the error to suite events (non-fatal - ignore if it fails)
                        try
                        {
                            await folderManager.AppendEventAsync(groupRunFolder, new EventEntry
                            {
                                Timestamp = DateTime.UtcNow.ToString("o"),
                                Code = "Node.ExecutionError",
                                Level = "error",
                                Message = $"Node '{node.NodeId}' execution failed with exception: {nodeEx.Message}",
                                Data = new Dictionary<string, object?>
                                {
                                    ["nodeId"] = node.NodeId,
                                    ["error"] = nodeEx.Message
                                }
                            });
                        }
                        catch
                        {
                            // Silently ignore event logging failures - the critical part is OnNodeFinished
                        }

                        // Check continue on failure
                        if (!continueOnFailure)
                        {
                            shouldStopPipeline = true;
                            break;
                        }
                    }
                }

                if (_cancellationToken.IsCancellationRequested)
                    break;
            }

            // Compute aggregate status per spec section 13.4
            var endTime = DateTime.UtcNow;
            var aggregateStatus = ComputeAggregateStatus(childResults, _cancellationToken.IsCancellationRequested);

            statusCounts.Total = childResults.Count;

            var result = new GroupResult
            {
                SchemaVersion = "1.5.0",
                RunType = RunType.TestSuite,
                SuiteId = suite.Manifest.Id,
                SuiteVersion = suite.Manifest.Version,
                PlanId = planId,
                PlanVersion = planVersion,
                Status = aggregateStatus,
                StartTime = startTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                EndTime = endTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Counts = statusCounts,
                ChildRunIds = childRunIds
            };

            await folderManager.WriteResultAsync(groupRunFolder, result);

            // Record suite completed event
            await folderManager.AppendEventAsync(groupRunFolder, new EventEntry
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
                Code = "TestSuite.Completed",
                Level = aggregateStatus == RunStatus.Passed ? "info" : "warning",
                Message = $"Test suite '{suite.Manifest.Id}' execution completed with status: {aggregateStatus}",
                Data = new Dictionary<string, object?>
                {
                    ["suiteId"] = suite.Manifest.Id,
                    ["suiteVersion"] = suite.Manifest.Version,
                    ["runId"] = groupRunId,
                    ["status"] = aggregateStatus.ToString(),
                    ["duration"] = (endTime - startTime).TotalSeconds,
                    ["totalCases"] = statusCounts.Passed + statusCounts.Failed + statusCounts.Error + statusCounts.Timeout + statusCounts.Aborted,
                    ["passedCases"] = statusCounts.Passed,
                    ["failedCases"] = statusCounts.Failed
                }
            });

            // Also write to parent plan folder if executing within a plan
            if (!string.IsNullOrEmpty(parentPlanRunFolder))
            {
                await folderManager.AppendEventAsync(parentPlanRunFolder, new EventEntry
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Code = "TestSuite.Completed",
                    Level = aggregateStatus == RunStatus.Passed ? "info" : "warning",
                    Message = $"Test suite '{suite.Manifest.Id}' execution completed with status: {aggregateStatus}",
                    Data = new Dictionary<string, object?>
                    {
                        ["suiteId"] = suite.Manifest.Id,
                        ["suiteVersion"] = suite.Manifest.Version,
                        ["runId"] = groupRunId,
                        ["status"] = aggregateStatus.ToString(),
                        ["duration"] = (endTime - startTime).TotalSeconds,
                        ["totalCases"] = statusCounts.Passed + statusCounts.Failed + statusCounts.Error + statusCounts.Timeout + statusCounts.Aborted,
                        ["passedCases"] = statusCounts.Passed,
                        ["failedCases"] = statusCounts.Failed
                    }
                });
            }

            // Append Suite run to index
            folderManager.AppendIndexEntry(new IndexEntry
            {
                RunId = groupRunId,
                RunType = RunType.TestSuite,
                SuiteId = suite.Manifest.Id,
                SuiteVersion = suite.Manifest.Version,
                PlanId = planId,
                PlanVersion = planVersion,
                ParentRunId = parentPlanRunId,
                StartTime = result.StartTime,
                EndTime = result.EndTime,
                Status = result.Status
            });

            // Report run finished (if top-level suite)
            if (string.IsNullOrEmpty(parentPlanRunId))
            {
                _reporter.OnRunFinished(groupRunId, aggregateStatus);
            }

            return result;
        }
        catch (Exception ex)
        {
            var endTime = DateTime.UtcNow;
            var result = new GroupResult
            {
                SchemaVersion = "1.5.0",
                RunType = RunType.TestSuite,
                SuiteId = suite.Manifest.Id,
                SuiteVersion = suite.Manifest.Version,
                PlanId = planId,
                PlanVersion = planVersion,
                Status = RunStatus.Error,
                StartTime = startTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                EndTime = endTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ChildRunIds = childRunIds,
                Message = ex.Message
            };

            await folderManager.WriteResultAsync(groupRunFolder, result);

            // Append Suite run to index
            folderManager.AppendIndexEntry(new IndexEntry
            {
                RunId = groupRunId,
                RunType = RunType.TestSuite,
                SuiteId = suite.Manifest.Id,
                SuiteVersion = suite.Manifest.Version,
                PlanId = planId,
                PlanVersion = planVersion,
                ParentRunId = parentPlanRunId,
                StartTime = result.StartTime,
                EndTime = result.EndTime,
                Status = result.Status
            });

            // Report run finished (if top-level suite)
            if (string.IsNullOrEmpty(parentPlanRunId))
            {
                _reporter.OnRunFinished(groupRunId, RunStatus.Error);
            }

            return result;
        }
    }

    private async Task HandleSuiteRebootAsync(
        string groupRunFolder,
        string groupRunId,
        DiscoveredTestSuite suite,
        RunRequest runRequest,
        string? planId,
        string? planVersion,
        string? parentPlanRunId,
        string? parentNodeId,
        string? parentPlanRunFolder,
        TestPlanManifest? planManifest,
        int iteration,
        int nodeIndex,
        TestCaseNode node,
        string runId,
        RunContext context,
        string runnerExecutablePath,
        RebootInfo? rebootInfo)
    {
        var resumeToken = Guid.NewGuid().ToString("N");
        var session = new RebootResumeSession
        {
            RunId = groupRunId,
            EntityType = "TestSuite",
            State = "PendingResume",
            CurrentNodeIndex = nodeIndex,
            NextPhase = rebootInfo?.NextPhase ?? 1,
            ResumeCount = 0,
            ResumeToken = resumeToken,
            CurrentNodeId = node.NodeId,
            CurrentChildRunId = runId,
            OriginTestId = rebootInfo?.OriginTestId,
            RunFolder = groupRunFolder,
            CaseContext = ResumeContextConverter.FromRunContext(context, Path.Combine(_runsRoot, runId)),
            SuiteContext = new SuiteResumeContext
            {
                SuiteIdentity = suite.Identity,
                RunRequest = runRequest,
                PlanId = planId,
                PlanVersion = planVersion,
                ParentPlanRunId = parentPlanRunId,
                ParentNodeId = parentNodeId,
                ParentPlanRunFolder = parentPlanRunFolder,
                PlanManifest = planManifest,
                CurrentIteration = iteration
            },
            Paths = BuildResumePaths()
        };

        await session.SaveAsync();

        if (!string.IsNullOrEmpty(parentPlanRunId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(runnerExecutablePath))
        {
            throw new InvalidOperationException("Runner executable path could not be determined.");
        }

        // Top-level suite orchestrator handles reboot/resume scheduling.
        ResumeTaskScheduler.CreateResumeTask(groupRunId, resumeToken, runnerExecutablePath, _runsRoot);
        RebootExecutor.RestartMachine(rebootInfo?.DelaySec);
        Environment.Exit(0);
    }

    private static TestCaseResult CreateNodeErrorResult(
        string nodeId,
        TestSuiteManifest suite,
        string? planId,
        string? planVersion,
        string message)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        return new TestCaseResult
        {
            SchemaVersion = "1.5.0",
            RunType = RunType.TestCase,
            NodeId = nodeId,
            TestId = "unknown",
            TestVersion = "unknown",
            SuiteId = suite.Id,
            SuiteVersion = suite.Version,
            PlanId = planId,
            PlanVersion = planVersion,
            Status = RunStatus.Error,
            StartTime = now,
            EndTime = now,
            Error = new ErrorInfo
            {
                Type = ErrorType.RunnerError,
                Source = "Engine",
                Message = message
            }
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

    /// <summary>
    /// Resolves test case by NodeId (as test case identity id@version) first, then falls back to ref path.
    /// </summary>
    private (TestCaseManifest?, string?, ValidationError?) ResolveTestCase(
        TestCaseNode node,
        string suiteManifestPath,
        SuiteRefResolver refResolver)
    {
        // Strip _1, _2, etc. suffix from NodeId to get the actual test case identity
        // e.g., "hw.bios.version_check@1.0.0_1" -> "hw.bios.version_check@1.0.0"
        var testCaseIdentity = StripNodeIdSuffix(node.NodeId);
        
        // Try to find test case by the stripped identity (id@version)
        var testCaseByNodeId = _discovery.TestCases.Values.FirstOrDefault(tc =>
            tc.Identity.Equals(testCaseIdentity, StringComparison.OrdinalIgnoreCase));

        if (testCaseByNodeId != null)
        {
            // Found by NodeId, return the manifest and path
            return (testCaseByNodeId.Manifest, testCaseByNodeId.FolderPath, null);
        }

        // Fall back to ref-based resolution
        return refResolver.ResolveRef(suiteManifestPath, node.Ref);
    }

    /// <summary>
    /// Strips the _1, _2, etc. suffix from a nodeId to get the base test case identity.
    /// e.g., "hw.bios.version_check@1.0.0_1" -> "hw.bios.version_check@1.0.0"
    /// </summary>
    private static string StripNodeIdSuffix(string nodeId)
    {
        var match = System.Text.RegularExpressions.Regex.Match(nodeId, @"^(.+)_(\d+)$");
        return match.Success ? match.Groups[1].Value : nodeId;
    }

    private static RunStatus ComputeAggregateStatus(List<TestCaseResult> results, bool wasAborted)
    {
        if (wasAborted)
            return RunStatus.Aborted;

        // Per spec section 13.4: Error > Timeout > Failed > Passed
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
