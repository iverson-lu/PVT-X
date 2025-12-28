using PcTest.Contracts;
using PcTest.Engine;
using PcTest.Runner;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class RunFolderLayoutTests
{
    [Fact]
    public void SuiteRun_CreatesExpectedLayout_AndIndexIsEngineOwned()
    {
        var root = TestHelpers.CreateTempDirectory();
        var caseRoot = Path.Combine(root, "cases");
        var suiteRoot = Path.Combine(root, "suites");
        var planRoot = Path.Combine(root, "plans");
        var runsRoot = Path.Combine(root, "runs");
        Directory.CreateDirectory(caseRoot);
        Directory.CreateDirectory(suiteRoot);
        Directory.CreateDirectory(planRoot);
        Directory.CreateDirectory(runsRoot);

        TestHelpers.WriteJson(Path.Combine(caseRoot, "CpuStress", "test.manifest.json"), new
        {
            schemaVersion = "1.5.0",
            id = "CpuStress",
            name = "CPU",
            category = "Thermal",
            version = "1.0.0"
        });
        File.WriteAllText(Path.Combine(caseRoot, "CpuStress", "run.ps1"), "exit 0");

        TestHelpers.WriteJson(Path.Combine(suiteRoot, "suite.manifest.json"), new
        {
            schemaVersion = "1.5.0",
            id = "Suite",
            name = "Suite",
            version = "1.0.0",
            testCases = new[]
            {
                new { nodeId = "n1", @ref = "CpuStress" }
            }
        });

        var engine = new PcTestEngine(new RunnerService());
        var summary = engine.RunSuite(new EngineOptions(caseRoot, suiteRoot, planRoot, runsRoot), new RunRequest { Suite = "Suite@1.0.0" });

        var groupFolder = Path.Combine(runsRoot, summary.RunId);
        Assert.True(File.Exists(Path.Combine(groupFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "children.jsonl")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "result.json")));

        var caseRunFolder = Directory.EnumerateDirectories(runsRoot)
            .First(dir => dir != groupFolder);
        Assert.True(File.Exists(Path.Combine(caseRunFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(caseRunFolder, "params.json")));
        Assert.True(File.Exists(Path.Combine(caseRunFolder, "result.json")));
        Assert.False(File.Exists(Path.Combine(caseRunFolder, "index.jsonl")));

        Assert.True(File.Exists(Path.Combine(runsRoot, "index.jsonl")));
    }
}
