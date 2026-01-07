using PcTest.Contracts.Results;
using PcTest.Runner;

namespace PcTest.Engine.Execution;

/// <summary>
/// Executes a resumed Test Case run based on a persisted session.json.
/// </summary>
public sealed class ResumeCaseExecutor
{
    private readonly string _runsRoot;
    private readonly IExecutionReporter _reporter;
    private readonly CancellationToken _cancellationToken;

    public ResumeCaseExecutor(
        string runsRoot,
        IExecutionReporter reporter,
        CancellationToken cancellationToken = default)
    {
        _runsRoot = PathUtils.NormalizePath(runsRoot);
        _reporter = reporter ?? NullExecutionReporter.Instance;
        _cancellationToken = cancellationToken;
    }

    public async Task<TestCaseResult> ExecuteAsync(string runId, string resumeToken)
    {
        var caseRunFolder = Path.Combine(_runsRoot, runId);
        if (!Directory.Exists(caseRunFolder))
        {
            throw new InvalidOperationException($"Run folder not found: {caseRunFolder}");
        }

        var session = RebootResumeManager.LoadSession(caseRunFolder);
        if (!string.Equals(session.RunId, runId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("session.json runId does not match requested runId.");
        }

        if (!string.Equals(session.ResumeToken, resumeToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid resume token.");
        }

        if (string.IsNullOrWhiteSpace(session.TestCasePath))
        {
            throw new InvalidOperationException("session.json is missing testCasePath.");
        }

        if (string.IsNullOrWhiteSpace(session.AssetsRoot))
        {
            throw new InvalidOperationException("session.json is missing assetsRoot.");
        }

        session.ResumeCount += 1;
        if (session.ResumeCount > 1)
        {
            session.State = "Finalized";
            await RebootResumeManager.SaveSessionAsync(caseRunFolder, session);
            await RebootResumeManager.DeleteResumeTaskAsync(runId);
            throw new InvalidOperationException("Resume loop detected; aborting run.");
        }

        await RebootResumeManager.SaveSessionAsync(caseRunFolder, session);

        _reporter.OnNodeStarted(runId, session.CurrentCaseId ?? runId);

        var runner = new TestCaseRunner(_cancellationToken);
        var context = new RunContext
        {
            RunId = session.RunId,
            Manifest = session.Manifest,
            TestCasePath = session.TestCasePath ?? string.Empty,
            EffectiveInputs = session.EffectiveInputs,
            EffectiveEnvironment = session.EffectiveEnvironment,
            SecretInputs = session.SecretInputs,
            SecretEnvVars = session.SecretEnvVars,
            WorkingDir = session.WorkingDir,
            TimeoutSec = session.TimeoutSec,
            RunsRoot = _runsRoot,
            AssetsRoot = session.AssetsRoot,
            NodeId = session.NodeId,
            SuiteId = session.SuiteId,
            SuiteVersion = session.SuiteVersion,
            PlanId = session.PlanId,
            PlanVersion = session.PlanVersion,
            ParentRunId = session.ParentRunId,
            InputTemplates = session.InputTemplates,
            Phase = session.NextPhase,
            ExistingRunFolder = caseRunFolder,
            IsResume = true
        };

        var result = await runner.ExecuteAsync(context);

        _reporter.OnNodeFinished(runId, new NodeFinishedState
        {
            NodeId = session.CurrentCaseId ?? runId,
            Status = result.Status,
            StartTime = DateTime.Parse(result.StartTime),
            EndTime = DateTime.Parse(result.EndTime),
            Message = result.Error?.Message
        });

        return result;
    }
}
