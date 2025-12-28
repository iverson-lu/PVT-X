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
    private readonly CancellationToken _cancellationToken;

    public StandaloneCaseExecutor(
        DiscoveryResult discovery,
        string runsRoot,
        CancellationToken cancellationToken = default)
    {
        _discovery = discovery;
        _runsRoot = PathUtils.NormalizePath(runsRoot);
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Executes a standalone Test Case run.
    /// Per spec section 12.3: standalone runs must NOT have parentRunId, suiteId, planId, nodeId.
    /// </summary>
    public async Task<TestCaseResult> ExecuteAsync(
        DiscoveredTestCase testCase,
        RunRequest runRequest)
    {
        // Validate standalone RunRequest constraints
        if (runRequest.NodeOverrides is not null && runRequest.NodeOverrides.Count > 0)
        {
            throw new ValidationException(ErrorCodes.RunRequestUnknownNodeId,
                "Standalone TestCase run must not include nodeOverrides");
        }

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

        // Execute
        var runId = TestCaseRunner.GenerateRunId();
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
            InputTemplates = inputResult.InputTemplates
            // NodeId, SuiteId, PlanId, ParentRunId all null for standalone
        };

        var result = await runner.ExecuteAsync(context);

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

        return result;
    }
}
