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

    public SuiteRebootContext? LastRebootContext { get; private set; }

    public sealed class SuiteRebootContext
    {
        public string SuiteRunId { get; init; } = string.Empty;
        public int NodeIndex { get; init; }
        public int Iteration { get; init; }
        public int NextPhase { get; init; }
        public string Reason { get; init; } = string.Empty;
        public int? DelaySec { get; init; }
        public string? OriginTestId { get; init; }
        public ResumeRunContext ResumeContext { get; init; } = new();
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
        TestPlanManifest? planManifest = null)
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
            resumeSession: null);
    }

    public async Task<GroupResult> ResumeAsync(
        DiscoveredTestSuite suite,
        RunRequest runRequest,
        RebootResumeSession resumeSession,
        string? planId = null,
        string? planVersion = null,
        string? parentPlanRunId = null,
        string? parentNodeId = null,
        string? parentPlanRunFolder = null,
        TestPlanManifest? planManifest = null)
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
            resumeSession);
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
        RebootResumeSession? resumeSession)
    {
        var startTime = DateTime.UtcNow;
        var isResuming = resumeSession is not null;
        var groupRunId = isResuming ? resumeSession!.RunId : GroupRunFolderManager.GenerateGroupRunId("S");
        var folderManager = new GroupRunFolderManager(_runsRoot);
        var groupRunFolder = isResuming
            ? resumeSession!.RunFolder
            : folderManager.CreateGroupRunFolder(groupRunId);

        var childRunIds = new List<string>();
        var childResults = new List<TestCaseResult>();
        var statusCounts = new StatusCounts();

        try
        {
            LastRebootContext = null;
            if (isResuming && resumeSession?.Context is null)
            {
                throw new InvalidOperationException("Resume context missing for suite.");
            }
            if (isResuming)
            {
                var existingState = LoadExistingSuiteState(groupRunFolder);
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

            // Record suite started event
            if (!isResuming)
            {
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
            if (!string.IsNullOrEmpty(parentPlanRunFolder) && !isResuming)
            {
                await folderManager.AppendEventAsync(parentPlanRunFolder, new EventEntry
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

            // Flag to stop the entire pipeline (both repeat iterations and test case nodes)
            // when continueOnFailure=false and a non-Passed status occurs
            var shouldStopPipeline = false;
            var startIteration = resumeSession?.CurrentIteration ?? 0;
            var startNodeIndex = resumeSession?.CurrentNodeIndex ?? 0;
            var resumeContext = resumeSession?.Context;

            // Report planned nodes (only report once for first iteration)
            // When under a plan, report with parent suite information for nested display
            if (!isResuming)
            {
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
            }

            for (var iteration = startIteration; iteration < repeat; iteration++)
            {
                // Check if pipeline should stop (continueOnFailure=false and a test failed)
                if (shouldStopPipeline)
                    break;

                for (var nodeIndex = iteration == startIteration ? startNodeIndex : 0;
                     nodeIndex < suite.Manifest.TestCases.Count;
                     nodeIndex++)
                {
                    var node = suite.Manifest.TestCases[nodeIndex];
                    if (_cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

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

                    // Execute with retry
                    TestCaseResult? nodeResult = null;
                    var attempts = 1 + retryOnError;
                    var isResumingNode = isResuming
                        && iteration == startIteration
                        && nodeIndex == startNodeIndex
                        && resumeContext is not null;

                    // Report node started
                    _reporter.OnNodeStarted(
                        string.IsNullOrEmpty(parentPlanRunId) ? groupRunId : parentPlanRunId,
                        node.NodeId);

                    var nodeStartTime = DateTime.UtcNow;

                    try
                    {
                        for (var attempt = 0; attempt < attempts; attempt++)
                        {
                            var runId = isResumingNode
                                ? resumeContext!.RunId
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

                        if (isResumingNode)
                        {
                            await folderManager.AppendEventAsync(groupRunFolder, new EventEntry
                            {
                                Timestamp = DateTime.UtcNow.ToString("o"),
                                Code = "TestCase.Resumed",
                                Level = "info",
                                Message = $"Test case '{testCaseManifest.Id}' (node '{node.NodeId}') resumed after reboot",
                                Data = new Dictionary<string, object?>
                                {
                                    ["nodeId"] = node.NodeId,
                                    ["testId"] = testCaseManifest.Id,
                                    ["testVersion"] = testCaseManifest.Version,
                                    ["runId"] = runId,
                                    ["phase"] = resumeSession?.NextPhase
                                }
                            });

                            if (!string.IsNullOrEmpty(parentPlanRunFolder))
                            {
                                await folderManager.AppendEventAsync(parentPlanRunFolder, new EventEntry
                                {
                                    Timestamp = DateTime.UtcNow.ToString("o"),
                                    Code = "TestCase.Resumed",
                                    Level = "info",
                                    Message = $"Test case '{testCaseManifest.Id}' (node '{node.NodeId}') resumed after reboot",
                                    Data = new Dictionary<string, object?>
                                    {
                                        ["nodeId"] = node.NodeId,
                                        ["testId"] = testCaseManifest.Id,
                                        ["testVersion"] = testCaseManifest.Version,
                                        ["runId"] = runId,
                                        ["phase"] = resumeSession?.NextPhase
                                    }
                                });
                            }
                        }

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
                        var context = isResumingNode
                            ? ResumeContextConverter.ToRunContext(resumeContext!, resumeContext!.RunId, resumeSession!.NextPhase, true)
                            : new RunContext
                            {
                                RunId = runId,
                                Phase = 0,
                                Manifest = testCaseManifest,
                                TestCasePath = testCasePath,
                                EffectiveInputs = inputResult.EffectiveInputs,
                                EffectiveEnvironment = effectiveEnv,
                                SecretInputs = inputResult.SecretInputs,
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
                                InputTemplates = inputResult.InputTemplates,
                                RunnerExecutablePath = runnerExecutablePath
                            };

                        // Write children.jsonl entry BEFORE execution so UI can start tailing immediately
                        // Using Passed as placeholder; will be overwritten after execution
                        await folderManager.AppendChildAsync(groupRunFolder, new ChildEntry
                        {
                            RunId = runId,
                            NodeId = node.NodeId,
                            TestId = testCaseManifest.Id,
                            TestVersion = testCaseManifest.Version,
                            Status = RunStatus.Planned
                        });

                        nodeResult = await runner.ExecuteAsync(context);
                        if (!childRunIds.Contains(runId))
                        {
                            childRunIds.Add(runId);
                        }

                        if (nodeResult.Status == RunStatus.RebootRequired && nodeResult.Reboot is not null)
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
                                    ["nextPhase"] = nodeResult.Reboot.NextPhase,
                                    ["reason"] = nodeResult.Reboot.Reason
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
                                        ["nextPhase"] = nodeResult.Reboot.NextPhase,
                                        ["reason"] = nodeResult.Reboot.Reason
                                    }
                                });
                            }

                            var caseRunFolder = Path.Combine(_runsRoot, runId);
                            var rebootContext = ResumeContextConverter.FromRunContext(context, caseRunFolder);

                            LastRebootContext = new SuiteRebootContext
                            {
                                SuiteRunId = groupRunId,
                                NodeIndex = nodeIndex,
                                Iteration = iteration,
                                NextPhase = nodeResult.Reboot.NextPhase,
                                Reason = nodeResult.Reboot.Reason,
                                DelaySec = nodeResult.Reboot.DelaySec,
                                OriginTestId = nodeResult.Reboot.OriginTestId,
                                ResumeContext = rebootContext
                            };

                            if (string.IsNullOrEmpty(parentPlanRunId))
                            {
                                await HandleSuiteRebootAsync(groupRunId, groupRunFolder, nodeResult, nodeIndex, iteration, rebootContext, runnerExecutablePath);
                            }

                            return new GroupResult
                            {
                                SchemaVersion = "1.5.0",
                                RunType = RunType.TestSuite,
                                RunId = groupRunId,
                                SuiteId = suite.Manifest.Id,
                                SuiteVersion = suite.Manifest.Version,
                                PlanId = planId,
                                PlanVersion = planVersion,
                                Status = RunStatus.RebootRequired,
                                StartTime = startTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                                EndTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                                Message = "Reboot requested by test case.",
                                Reboot = nodeResult.Reboot
                            };
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
                            if (!continueOnFailure && nodeResult.Status != RunStatus.Passed && nodeResult.Status != RunStatus.RebootRequired)
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
                RunId = groupRunId,
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
                RunId = groupRunId,
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

    private async Task HandleSuiteRebootAsync(
        string groupRunId,
        string groupRunFolder,
        TestCaseResult nodeResult,
        int nodeIndex,
        int iteration,
        ResumeRunContext resumeContext,
        string runnerExecutablePath)
    {
        var resumeToken = Guid.NewGuid().ToString("N");
        var session = new RebootResumeSession
        {
            RunId = groupRunId,
            EntityType = "TestSuite",
            CurrentNodeIndex = nodeIndex,
            CurrentIteration = iteration,
            NextPhase = nodeResult.Reboot!.NextPhase,
            ResumeToken = resumeToken,
            ResumeCount = 0,
            State = "PendingResume",
            RunFolder = groupRunFolder,
            Context = resumeContext,
            CasesRoot = _discovery.ResolvedTestCaseRoot,
            SuitesRoot = _discovery.ResolvedTestSuiteRoot,
            PlansRoot = _discovery.ResolvedTestPlanRoot,
            AssetsRoot = _assetsRoot
        };

        await session.SaveAsync();
        ResumeTaskScheduler.CreateResumeTask(groupRunId, resumeToken, runnerExecutablePath, _runsRoot);
        RebootExecutor.RestartMachine(nodeResult.Reboot?.DelaySec);
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

    private static (List<TestCaseResult> Results, List<string> RunIds, StatusCounts Counts) LoadExistingSuiteState(string groupRunFolder)
    {
        var results = new List<TestCaseResult>();
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

            results.Add(new TestCaseResult { Status = status });
            UpdateCounts(counts, status);
        }

        return (results, runIds.ToList(), counts);
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
