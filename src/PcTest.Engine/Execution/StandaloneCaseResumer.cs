using System.Text;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Contracts.Results;
using PcTest.Runner;

namespace PcTest.Engine.Execution;

public sealed class StandaloneCaseResumer
{
    private readonly IExecutionReporter _reporter;
    private readonly CancellationToken _cancellationToken;

    public StandaloneCaseResumer(
        IExecutionReporter reporter,
        CancellationToken cancellationToken = default)
    {
        _reporter = reporter ?? NullExecutionReporter.Instance;
        _cancellationToken = cancellationToken;
    }

    public async Task<TestCaseResult> ResumeAsync(RebootSession session)
    {
        var manifestPath = Path.Combine(session.TestCasePath, "test.manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Test manifest not found: {manifestPath}");
        }

        var manifestJson = await File.ReadAllTextAsync(manifestPath, Encoding.UTF8);
        var manifest = JsonDefaults.Deserialize<TestCaseManifest>(manifestJson);
        if (manifest is null)
        {
            throw new InvalidOperationException($"Failed to parse manifest at {manifestPath}");
        }

        var runner = new TestCaseRunner(_cancellationToken);
        var effectiveInputs = session.EffectiveInputs.ToDictionary(
            kv => kv.Key,
            kv => (object?)kv.Value);

        var context = new RunContext
        {
            RunId = session.RunId,
            Manifest = manifest,
            TestCasePath = session.TestCasePath,
            EffectiveInputs = effectiveInputs,
            EffectiveEnvironment = new Dictionary<string, string>(session.EffectiveEnvironment),
            SecretInputs = new Dictionary<string, bool>(session.SecretInputs),
            SecretEnvVars = new HashSet<string>(session.SecretEnvVars),
            WorkingDir = session.WorkingDir,
            TimeoutSec = session.TimeoutSec,
            RunsRoot = session.RunsRoot,
            AssetsRoot = session.AssetsRoot,
            InputTemplates = session.InputTemplates,
            Phase = session.NextPhase,
            ExistingRunFolder = session.CaseRunFolder
        };

        var startTime = DateTime.UtcNow;
        var result = await runner.ExecuteAsync(context);
        var endTime = DateTime.UtcNow;

        var folderManager = new GroupRunFolderManager(session.RunsRoot);
        folderManager.AppendIndexEntry(new IndexEntry
        {
            RunId = session.RunId,
            RunType = RunType.TestCase,
            TestId = manifest.Id,
            TestVersion = manifest.Version,
            StartTime = result.StartTime,
            EndTime = result.EndTime,
            Status = result.Status
        });

        _reporter.OnNodeFinished(session.RunId, new NodeFinishedState
        {
            NodeId = "standalone",
            Status = result.Status,
            StartTime = startTime,
            EndTime = endTime,
            Message = result.Error?.Message
        });

        _reporter.OnRunFinished(session.RunId, result.Status);

        return result;
    }
}
