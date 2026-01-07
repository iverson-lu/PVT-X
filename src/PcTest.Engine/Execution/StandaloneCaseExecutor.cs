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
/// Executes standalone Test Case runs per spec section 2.2.
/// </summary>
public sealed class StandaloneCaseExecutor
{
    private readonly DiscoveryResult _discovery;
    private readonly string _runsRoot;
    private readonly string _assetsRoot;
    private readonly IExecutionReporter _reporter;
    private readonly CancellationToken _cancellationToken;

    public StandaloneCaseExecutor(
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
    /// Executes a standalone Test Case run.
    /// Per spec section 12.3: standalone runs must NOT have parentRunId, suiteId, planId, nodeId.
    /// </summary>
    public async Task<TestCaseResult> ExecuteAsync(
        DiscoveredTestCase testCase,
        RunRequest runRequest,
        ResumeInfo? resumeInfo = null)
    {
        // Validate standalone RunRequest constraints
        if (runRequest.NodeOverrides is not null && runRequest.NodeOverrides.Count > 0)
        {
            throw new ValidationException(ErrorCodes.RunRequestUnknownNodeId,
                "Standalone TestCase run must not include nodeOverrides");
        }

        // Generate runId early so we can report it
        var runId = resumeInfo?.RunId ?? TestCaseRunner.GenerateRunId();
        var nodeId = "standalone";

        // Report planned nodes (single node for standalone case)
        var plannedNodes = new List<PlannedNode>
        {
            new PlannedNode
            {
                NodeId = nodeId,
                TestId = testCase.Manifest.Id,
                TestVersion = testCase.Manifest.Version,
                NodeType = RunType.TestCase
            }
        };
        _reporter.OnRunPlanned(runId, RunType.TestCase, plannedNodes);

        // Compute effective environment
        var envResolver = new EnvironmentResolver();
        var effectiveEnv = envResolver.ComputeStandaloneEnvironment(runRequest.EnvironmentOverrides);

        // Resolve inputs
        var inputResolver = new InputResolver();
        var inputResult = inputResolver.ResolveStandaloneInputs(
            testCase.Manifest,
            runRequest.CaseInputs,
            effectiveEnv);

        if (!inputResult.Success)
        {
            throw new ValidationException(new ValidationResult().Also(r =>
                inputResult.Errors.ForEach(r.AddError)));
        }

        // Report node started
        _reporter.OnNodeStarted(runId, nodeId);

        var startTime = DateTime.UtcNow;

        // Execute
        var runner = new TestCaseRunner(_cancellationToken);

        var context = new RunContext
        {
            RunId = runId,
            Manifest = testCase.Manifest,
            TestCasePath = testCase.FolderPath,
            EffectiveInputs = inputResult.EffectiveInputs,
            EffectiveEnvironment = effectiveEnv,
            SecretInputs = inputResult.SecretInputs,
            TimeoutSec = testCase.Manifest.TimeoutSec,
            RunsRoot = _runsRoot,
            AssetsRoot = _assetsRoot,
            InputTemplates = inputResult.InputTemplates,
            Phase = resumeInfo?.Phase ?? 0,
            RunFolderPath = resumeInfo?.RunFolderPath,
            AppendOutput = resumeInfo?.AppendOutput ?? false,
            CaseInputs = runRequest.CaseInputs,
            EnvironmentOverrides = runRequest.EnvironmentOverrides,
            CasesRoot = _discovery.ResolvedTestCaseRoot,
            SuitesRoot = _discovery.ResolvedTestSuiteRoot,
            PlansRoot = _discovery.ResolvedTestPlanRoot,
            RunEntityType = "TestCase",
            RunEntityId = testCase.Identity
            // NodeId, SuiteId, PlanId, ParentRunId all null for standalone
        };

        TestCaseResult result;
        try
        {
            result = await runner.ExecuteAsync(context);
        }
        catch (RebootRequestedException)
        {
            throw;
        }

        var endTime = DateTime.UtcNow;

        // Report node finished
        _reporter.OnNodeFinished(runId, new NodeFinishedState
        {
            NodeId = nodeId,
            Status = result.Status,
            StartTime = startTime,
            EndTime = endTime,
            Message = result.Error?.Message
        });

        // Append to index.jsonl (Engine is the single writer)
        var folderManager = new GroupRunFolderManager(_runsRoot);
        folderManager.AppendIndexEntry(new IndexEntry
        {
            RunId = runId,
            RunType = RunType.TestCase,
            // nodeId MUST NOT be present for standalone per spec section 12.3
            TestId = testCase.Manifest.Id,
            TestVersion = testCase.Manifest.Version,
            // suiteId/suiteVersion/planId/planVersion MUST NOT be present
            // parentRunId MUST be omitted
            StartTime = result.StartTime,
            EndTime = result.EndTime,
            Status = result.Status
        });

        // Report run finished
        _reporter.OnRunFinished(runId, result.Status);

        return result;
    }
}
