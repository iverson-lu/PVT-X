using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Contracts.Requests;
using PcTest.Contracts.Results;
using PcTest.Contracts.Validation;
using PcTest.Engine.Discovery;
using PcTest.Engine.Resolution;

namespace PcTest.Engine.Execution;

/// <summary>
/// Orchestrator for Test Plan execution per spec section 10.
/// </summary>
public sealed class PlanOrchestrator
{
    private readonly DiscoveryResult _discovery;
    private readonly string _runsRoot;
    private readonly IExecutionReporter _reporter;
    private readonly CancellationToken _cancellationToken;

    public PlanOrchestrator(
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
    /// Executes a Plan with the given RunRequest.
    /// Per spec section 8.3: Plan RunRequest must NOT include nodeOverrides or caseInputs.
    /// </summary>
    public async Task<GroupResult> ExecuteAsync(
        DiscoveredTestPlan plan,
        RunRequest runRequest)
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
        var groupRunId = GroupRunFolderManager.GenerateGroupRunId("P");
        var folderManager = new GroupRunFolderManager(_runsRoot);
        var groupRunFolder = folderManager.CreateGroupRunFolder(groupRunId);

        var childRunIds = new List<string>();
        var childResults = new List<GroupResult>();
        var statusCounts = new StatusCounts();

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
            await folderManager.WriteManifestAsync(groupRunFolder, manifestSnapshot);

            // Compute effective environment
            var envResolver = new EnvironmentResolver();

            await folderManager.WriteRunRequestAsync(groupRunFolder, runRequest);

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

            // Execute each Suite in order per spec section 6.4
            foreach (var suiteIdentity in plan.Manifest.Suites)
            {
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
                var suiteOrchestrator = new SuiteOrchestrator(_discovery, _runsRoot, _reporter, _cancellationToken);
                var suiteResult = await suiteOrchestrator.ExecuteAsync(
                    suite,
                    suiteRunRequest,
                    plan.Manifest.Id,
                    plan.Manifest.Version,
                    groupRunId,
                    suiteIdentity);

                childResults.Add(suiteResult);
                childRunIds.AddRange(suiteResult.ChildRunIds);
                UpdateCounts(statusCounts, suiteResult.Status);

                // Forward suite completion event to plan events.jsonl
                await folderManager.AppendEventAsync(groupRunFolder, new EventEntry
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Code = "TestSuite.Completed",
                    Level = suiteResult.Status == RunStatus.Passed ? "info" : "warning",
                    Message = $"Test suite '{suite.Manifest.Id}' completed with status: {suiteResult.Status}",
                    Data = new Dictionary<string, object?>
                    {
                        ["suiteId"] = suite.Manifest.Id,
                        ["suiteVersion"] = suite.Manifest.Version,
                        ["status"] = suiteResult.Status.ToString(),
                        ["childCaseCount"] = suiteResult.ChildRunIds.Count
                    }
                });

                // Append to children.jsonl
                await folderManager.AppendChildAsync(groupRunFolder, new ChildEntry
                {
                    RunId = suiteResult.ChildRunIds.FirstOrDefault() ?? "",
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
