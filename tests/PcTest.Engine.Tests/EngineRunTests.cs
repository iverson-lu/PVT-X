using PcTest.Contracts;
using PcTest.Engine;
using PcTest.Runner;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class EngineRunTests
{
    [Fact]
    public async Task SuiteRun_Writes_Group_And_Case_Folders()
    {
        using TempDirectory temp = new();
        string caseRoot = temp.CreateSubdirectory("cases");
        string suiteRoot = temp.CreateSubdirectory("suites");
        string runsRoot = temp.CreateSubdirectory("runs");

        CreateTestCase(caseRoot, "CaseA", "1.0.0", "A");
        CreateSuite(suiteRoot, "SuiteA", "1.0.0", "A");

        DiscoveryResult discovery = new DiscoveryService().Discover(new DiscoveryOptions
        {
            TestCaseRoot = caseRoot,
            TestSuiteRoot = suiteRoot,
            TestPlanRoot = string.Empty
        });

        RunnerService runner = new(new FakeProcessRunner());
        EngineService engine = new(runner);

        RunRequest request = new() { Suite = "SuiteA@1.0.0" };
        SuiteExecutionResult result = await engine.RunSuiteAsync(discovery.TestSuites.Single(), discovery.TestCases, request, new RunConfiguration
        {
            RunsRoot = runsRoot,
            TestCaseRoot = caseRoot,
            TestSuiteRoot = suiteRoot
        }, null, null, null, CancellationToken.None);

        string[] folders = Directory.GetDirectories(runsRoot);
        Assert.Equal(2, folders.Length);

        string groupFolder = folders.Single(path => Path.GetFileName(path).StartsWith("G-", StringComparison.Ordinal));
        string caseFolder = folders.Single(path => Path.GetFileName(path).StartsWith("R-", StringComparison.Ordinal));

        Assert.True(File.Exists(Path.Combine(groupFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "controls.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "environment.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "runRequest.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "children.jsonl")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "result.json")));
        Assert.False(File.Exists(Path.Combine(groupFolder, "params.json")));

        Assert.True(File.Exists(Path.Combine(caseFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(caseFolder, "params.json")));
        Assert.True(File.Exists(Path.Combine(caseFolder, "stdout.log")));
        Assert.True(File.Exists(Path.Combine(caseFolder, "stderr.log")));
        Assert.True(File.Exists(Path.Combine(caseFolder, "env.json")));
        Assert.True(File.Exists(Path.Combine(caseFolder, "result.json")));
        Assert.False(File.Exists(Path.Combine(caseFolder, "controls.json")));
        Assert.False(File.Exists(Path.Combine(caseFolder, "children.jsonl")));

        Assert.True(File.Exists(Path.Combine(runsRoot, "index.jsonl")));
        Assert.Equal(result.RunId, Path.GetFileName(groupFolder));
    }

    [Fact]
    public async Task SuiteRepeat_BelowOne_Is_Treated_As_One()
    {
        using TempDirectory temp = new();
        string caseRoot = temp.CreateSubdirectory("cases");
        string suiteRoot = temp.CreateSubdirectory("suites");
        string runsRoot = temp.CreateSubdirectory("runs");

        CreateTestCase(caseRoot, "CaseA", "1.0.0", "A");
        CreateSuiteWithRepeat(suiteRoot, "SuiteRepeat", "1.0.0", "A", 0);

        DiscoveryResult discovery = new DiscoveryService().Discover(new DiscoveryOptions
        {
            TestCaseRoot = caseRoot,
            TestSuiteRoot = suiteRoot,
            TestPlanRoot = string.Empty
        });

        RunnerService runner = new(new FakeProcessRunner());
        EngineService engine = new(runner);

        RunRequest request = new() { Suite = "SuiteRepeat@1.0.0" };
        SuiteExecutionResult result = await engine.RunSuiteAsync(discovery.TestSuites.Single(), discovery.TestCases, request, new RunConfiguration
        {
            RunsRoot = runsRoot,
            TestCaseRoot = caseRoot,
            TestSuiteRoot = suiteRoot
        }, null, null, null, CancellationToken.None);

        string groupFolder = Path.Combine(runsRoot, result.RunId);
        string[] children = File.ReadAllLines(Path.Combine(groupFolder, "children.jsonl"));
        Assert.Single(children);
    }

    private static void CreateTestCase(string root, string id, string version, string folderName)
    {
        string folder = Path.Combine(root, folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "test.manifest.json"), $"{{\"schemaVersion\":\"1.5.0\",\"id\":\"{id}\",\"name\":\"{id}\",\"category\":\"Demo\",\"version\":\"{version}\",\"parameters\":[{{\"name\":\"DurationSec\",\"type\":\"int\",\"required\":true,\"default\":1}}]}}");
        File.WriteAllText(Path.Combine(folder, "run.ps1"), "Write-Output 'ok'\nexit 0");
    }

    private static void CreateSuite(string root, string id, string version, string caseRef)
    {
        string folder = Path.Combine(root, id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "suite.manifest.json"), $"{{\"schemaVersion\":\"1.5.0\",\"id\":\"{id}\",\"name\":\"{id}\",\"version\":\"{version}\",\"controls\":{{\"continueOnFailure\":false}},\"environment\":{{\"env\":{{\"LAB\":\"1\"}}}},\"testCases\":[{{\"nodeId\":\"n1\",\"ref\":\"{caseRef}\",\"inputs\":{{\"DurationSec\":1}}}}]}}");
    }

    private static void CreateSuiteWithRepeat(string root, string id, string version, string caseRef, int repeat)
    {
        string folder = Path.Combine(root, id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "suite.manifest.json"), $"{{\"schemaVersion\":\"1.5.0\",\"id\":\"{id}\",\"name\":\"{id}\",\"version\":\"{version}\",\"controls\":{{\"repeat\":{repeat}}},\"testCases\":[{{\"nodeId\":\"n1\",\"ref\":\"{caseRef}\",\"inputs\":{{\"DurationSec\":1}}}}]}}");
    }
}
