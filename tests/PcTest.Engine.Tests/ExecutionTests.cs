using PcTest.Contracts;
using PcTest.Engine;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class ExecutionTests
{
    [Fact]
    public async Task StandaloneRun_CreatesOnlyCaseFolderAndIndex()
    {
        string root = TestHelpers.CreateTempDirectory();
        string cases = Path.Combine(root, "cases");
        string suites = Path.Combine(root, "suites");
        string plans = Path.Combine(root, "plans");
        string runs = Path.Combine(root, "runs");
        Directory.CreateDirectory(cases);
        Directory.CreateDirectory(suites);
        Directory.CreateDirectory(plans);
        Directory.CreateDirectory(runs);

        TestHelpers.WriteTestCase(cases, "CpuStress", "CpuStress", "1.0.0");
        EngineService engine = new();
        ValidationResult<DiscoveryResult> discovery = engine.Discover(cases, suites, plans);
        Assert.True(discovery.IsSuccess);

        RunRequest request = new() { TestCase = "CpuStress@1.0.0" };
        ValidationResult<string> run = await engine.RunAsync(discovery.Value!, request, runs, CancellationToken.None);
        Assert.True(run.IsSuccess);

        string runId = run.Value!;
        string runFolder = Path.Combine(runs, runId);
        Assert.True(Directory.Exists(runFolder));
        Assert.True(File.Exists(Path.Combine(runs, "index.jsonl")));
        Assert.True(File.Exists(Path.Combine(runFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(runFolder, "result.json")));
        Assert.False(Directory.EnumerateDirectories(runs).Any(dir => Path.GetFileName(dir).StartsWith("G-", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task SuiteRun_WritesGroupFolderAndChildren()
    {
        string root = TestHelpers.CreateTempDirectory();
        string cases = Path.Combine(root, "cases");
        string suites = Path.Combine(root, "suites");
        string plans = Path.Combine(root, "plans");
        string runs = Path.Combine(root, "runs");
        Directory.CreateDirectory(cases);
        Directory.CreateDirectory(suites);
        Directory.CreateDirectory(plans);
        Directory.CreateDirectory(runs);

        TestHelpers.WriteTestCase(cases, "CpuStress", "CpuStress", "1.0.0");
        SuiteManifest suite = new()
        {
            SchemaVersion = "1.5.0",
            Id = "SuiteA",
            Name = "SuiteA",
            Version = "1.0.0",
            TestCases = new[] { new SuiteTestCaseNode { NodeId = "n1", Ref = "CpuStress" } }
        };
        TestHelpers.WriteSuite(suites, "SuiteA", suite);

        EngineService engine = new();
        ValidationResult<DiscoveryResult> discovery = engine.Discover(cases, suites, plans);
        Assert.True(discovery.IsSuccess);

        RunRequest request = new() { Suite = "SuiteA@1.0.0" };
        ValidationResult<string> run = await engine.RunAsync(discovery.Value!, request, runs, CancellationToken.None);
        Assert.True(run.IsSuccess);

        string groupRunId = run.Value!;
        string groupFolder = Path.Combine(runs, groupRunId);
        Assert.True(Directory.Exists(groupFolder));
        Assert.True(File.Exists(Path.Combine(groupFolder, "children.jsonl")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "result.json")));
    }
}
