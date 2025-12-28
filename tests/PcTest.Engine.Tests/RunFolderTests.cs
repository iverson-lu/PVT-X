using PcTest.Contracts;
using PcTest.Engine;
using PcTest.Runner;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class RunFolderTests
{
    [Fact]
    public async Task SuiteRunCreatesGroupFolderAndIndex()
    {
        var root = EngineTestUtilities.CreateTempDirectory();
        var casesRoot = Path.Combine(root, "TestCases");
        var suitesRoot = Path.Combine(root, "TestSuites");
        var plansRoot = Path.Combine(root, "TestPlans");
        Directory.CreateDirectory(casesRoot);
        Directory.CreateDirectory(suitesRoot);
        Directory.CreateDirectory(plansRoot);

        var caseFolder = Path.Combine(casesRoot, "Case");
        Directory.CreateDirectory(caseFolder);
        EngineTestUtilities.WriteJson(Path.Combine(caseFolder, "test.manifest.json"), EngineTestUtilities.SampleTestCase("Case", "1.0.0"));

        var suiteFolder = Path.Combine(suitesRoot, "Suite");
        Directory.CreateDirectory(suiteFolder);
        EngineTestUtilities.WriteJson(Path.Combine(suiteFolder, "suite.manifest.json"), EngineTestUtilities.SampleSuite("Suite", "1.0.0", "Case"));

        var runner = new TestCaseRunner(new FakeProcessRunner(), new SequenceRunIdGenerator("case-1", "case-2"));
        var engine = new EngineRunner(runner);
        var runsRoot = Path.Combine(root, "Runs");
        var context = new EngineRunContext
        {
            DiscoveryRoots = new DiscoveryRoots
            {
                ResolvedTestCaseRoot = casesRoot,
                ResolvedTestSuiteRoot = suitesRoot,
                ResolvedTestPlanRoot = plansRoot
            },
            RunRequest = new RunRequest { Suite = "Suite@1.0.0" },
            RunsRoot = runsRoot,
            PowerShellPath = "pwsh",
            GroupRunIdFactory = new FixedRunIdGenerator("group-1")
        };

        await engine.RunAsync(context, CancellationToken.None);

        var groupFolder = Path.Combine(runsRoot, "group-1");
        Assert.True(File.Exists(Path.Combine(groupFolder, "index.jsonl")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "environment.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "runRequest.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "children.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "events.jsonl")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "result.json")));

        var caseFolderPath = Path.Combine(runsRoot, "case-1");
        Assert.False(File.Exists(Path.Combine(caseFolderPath, "index.jsonl")));
    }

    [Fact]
    public async Task StandaloneRunDoesNotCreateGroupFolder()
    {
        var root = EngineTestUtilities.CreateTempDirectory();
        var casesRoot = Path.Combine(root, "TestCases");
        var suitesRoot = Path.Combine(root, "TestSuites");
        var plansRoot = Path.Combine(root, "TestPlans");
        Directory.CreateDirectory(casesRoot);
        Directory.CreateDirectory(suitesRoot);
        Directory.CreateDirectory(plansRoot);

        var caseFolder = Path.Combine(casesRoot, "Case");
        Directory.CreateDirectory(caseFolder);
        EngineTestUtilities.WriteJson(Path.Combine(caseFolder, "test.manifest.json"), EngineTestUtilities.SampleTestCase("Case", "1.0.0"));

        var runner = new TestCaseRunner(new FakeProcessRunner(), new FixedRunIdGenerator("case-1"));
        var engine = new EngineRunner(runner);
        var runsRoot = Path.Combine(root, "Runs");
        var context = new EngineRunContext
        {
            DiscoveryRoots = new DiscoveryRoots
            {
                ResolvedTestCaseRoot = casesRoot,
                ResolvedTestSuiteRoot = suitesRoot,
                ResolvedTestPlanRoot = plansRoot
            },
            RunRequest = new RunRequest { TestCase = "Case@1.0.0" },
            RunsRoot = runsRoot,
            PowerShellPath = "pwsh",
            GroupRunIdFactory = new FixedRunIdGenerator("group-1")
        };

        await engine.RunAsync(context, CancellationToken.None);

        var groupFolder = Path.Combine(runsRoot, "group-1");
        Assert.False(Directory.Exists(groupFolder));
        Assert.True(Directory.Exists(Path.Combine(runsRoot, "case-1")));
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(System.Diagnostics.ProcessStartInfo startInfo, TimeSpan? timeout, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ProcessRunResult
            {
                ExitCode = 0,
                TimedOut = false,
                Aborted = false,
                StandardOutput = string.Empty,
                StandardError = string.Empty
            });
        }
    }

    private sealed class FixedRunIdGenerator : IRunIdGenerator
    {
        private readonly string _id;

        public FixedRunIdGenerator(string id)
        {
            _id = id;
        }

        public string NewRunId() => _id;
    }

    private sealed class SequenceRunIdGenerator : IRunIdGenerator
    {
        private readonly Queue<string> _ids;

        public SequenceRunIdGenerator(params string[] ids)
        {
            _ids = new Queue<string>(ids);
        }

        public string NewRunId() => _ids.Dequeue();
    }
}
