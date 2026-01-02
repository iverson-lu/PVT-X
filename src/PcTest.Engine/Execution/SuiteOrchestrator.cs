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
    private readonly IExecutionReporter _reporter;
    private readonly CancellationToken _cancellationToken;

    public SuiteOrchestrator(
        DiscoveryResult discovery,
        string runsRoot,
        IExecutionReporter reporter,
        CancellationToken cancellationToken = default)
    {
        _discovery = discovery;
        _runsRoot = PathUtils.NormalizePath(runsRoot);
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
        string? parentPlanRunFolder = null)
    {
        var startTime = DateTime.UtcNow;
        var groupRunId = GroupRunFolderManager.GenerateGroupRunId("S");
        var folderManager = new GroupRunFolderManager(_runsRoot);
        var groupRunFolder = folderManager.CreateGroupRunFolder(groupRunId);

        var childRunIds = new List<string>();
        var childResults = new List<TestCaseResult>();
        var statusCounts = new StatusCounts();

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
            await folderManager.WriteManifestAsync(groupRunFolder, manifestSnapshot);

            var controls = suite.Manifest.Controls ?? new SuiteControls();
            await folderManager.WriteControlsAsync(groupRunFolder, controls);

            // Compute effective environment
            var envResolver = new EnvironmentResolver();
            var effectiveEnv = envResolver.ComputeSuiteEnvironment(
                suite.Manifest, runRequest.EnvironmentOverrides);
            await folderManager.WriteEnvironmentAsync(groupRunFolder, effectiveEnv);

            await folderManager.WriteRunRequestAsync(groupRunFolder, runRequest);

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

            // Also write to parent plan folder if executing within a plan
            if (!string.IsNullOrEmpty(parentPlanRunFolder))
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

            var repeat = Math.Max(1, controls.Repeat);
            var continueOnFailure = controls.ContinueOnFailure;
            var retryOnError = Math.Max(0, controls.RetryOnError);

            // Report planned nodes (only report once for first iteration)
            // When under a plan, report with parent suite information for nested display
            var plannedNodes = new List<PlannedNode>();
            foreach (var node in suite.Manifest.TestCases)
            {
                var (testCaseManifest, _, _) = refResolver.ResolveRef(suite.ManifestPath, node.Ref);
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
                foreach (var node in suite.Manifest.TestCases)
                {
                    if (_cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    // Resolve ref
                    var (testCaseManifest, testCasePath, refError) = refResolver.ResolveRef(
                        suite.ManifestPath, node.Ref);

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
                            break;
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
                            break;
                        continue;
                    }

                    // Execute with retry
                    TestCaseResult? nodeResult = null;
                    var attempts = 1 + retryOnError;

                    // Report node started
                    _reporter.OnNodeStarted(
                        string.IsNullOrEmpty(parentPlanRunId) ? groupRunId : parentPlanRunId,
                        node.NodeId);

                    var nodeStartTime = DateTime.UtcNow;

                    for (var attempt = 0; attempt < attempts; attempt++)
                    {
                        var runId = TestCaseRunner.GenerateRunId();

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
                        var context = new RunContext
                        {
                            RunId = runId,
                            Manifest = testCaseManifest,
                            TestCasePath = testCasePath,
                            EffectiveInputs = inputResult.EffectiveInputs,
                            EffectiveEnvironment = effectiveEnv,
                            SecretInputs = inputResult.SecretInputs,
                            WorkingDir = suite.Manifest.Environment?.WorkingDir,
                            TimeoutSec = testCaseManifest.TimeoutSec,
                            RunsRoot = _runsRoot,
                            NodeId = node.NodeId,
                            SuiteId = suite.Manifest.Id,
                            SuiteVersion = suite.Manifest.Version,
                            PlanId = planId,
                            PlanVersion = planVersion,
                            ParentRunId = groupRunId,
                            InputTemplates = inputResult.InputTemplates
                        };

                        // Write children.jsonl entry BEFORE execution so UI can start tailing immediately
                        // Using Passed as placeholder; will be overwritten after execution
                        await folderManager.AppendChildAsync(groupRunFolder, new ChildEntry
                        {
                            RunId = runId,
                            NodeId = node.NodeId,
                            TestId = testCaseManifest.Id,
                            TestVersion = testCaseManifest.Version,
                            Status = RunStatus.Passed
                        });

                        nodeResult = await runner.ExecuteAsync(context);
                        childRunIds.Add(runId);

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
                        if (!continueOnFailure && nodeResult.Status != RunStatus.Passed)
                            break;
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
        }
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
}
