using PcTest.Contracts;
using PcTest.Engine;
using PcTest.Runner;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class RunRequestTests
{
    [Fact]
    public async Task PlanRunRequest_Rejects_CaseInputs()
    {
        using TempDirectory temp = new();
        string caseRoot = temp.CreateSubdirectory("cases");
        string suiteRoot = temp.CreateSubdirectory("suites");
        string planRoot = temp.CreateSubdirectory("plans");
        string runsRoot = temp.CreateSubdirectory("runs");

        CreateTestCase(caseRoot, "CaseA", "1.0.0", "A");
        CreateSuite(suiteRoot, "SuiteA", "1.0.0", "A");
        CreatePlan(planRoot, "PlanA", "1.0.0", "SuiteA@1.0.0");

        DiscoveryResult discovery = new DiscoveryService().Discover(new DiscoveryOptions
        {
            TestCaseRoot = caseRoot,
            TestSuiteRoot = suiteRoot,
            TestPlanRoot = planRoot
        });

        ResolvedTestPlan plan = discovery.TestPlans.Single();
        RunRequest request = new()
        {
            Plan = plan.Identity.ToString(),
            CaseInputs = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["DurationSec"] = System.Text.Json.JsonDocument.Parse("1").RootElement
            }
        };

        RunnerService runner = new(new FakeProcessRunner());
        EngineService engine = new(runner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RunPlanAsync(plan, discovery.TestSuites, discovery.TestCases, request, new RunConfiguration
        {
            RunsRoot = runsRoot,
            TestCaseRoot = caseRoot,
            TestSuiteRoot = suiteRoot,
            TestPlanRoot = planRoot
        }, CancellationToken.None));
    }

    private static void CreateTestCase(string root, string id, string version, string folderName)
    {
        string folder = Path.Combine(root, folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "test.manifest.json"), $"{{\"schemaVersion\":\"1.5.0\",\"id\":\"{id}\",\"name\":\"{id}\",\"category\":\"Demo\",\"version\":\"{version}\"}}");
        File.WriteAllText(Path.Combine(folder, "run.ps1"), "Write-Output 'ok'\nexit 0");
    }

    private static void CreateSuite(string root, string id, string version, string caseRef)
    {
        string folder = Path.Combine(root, id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "suite.manifest.json"), $"{{\"schemaVersion\":\"1.5.0\",\"id\":\"{id}\",\"name\":\"{id}\",\"version\":\"{version}\",\"testCases\":[{{\"nodeId\":\"n1\",\"ref\":\"{caseRef}\"}}]}}");
    }

    private static void CreatePlan(string root, string id, string version, string suiteRef)
    {
        string folder = Path.Combine(root, id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "plan.manifest.json"), $"{{\"schemaVersion\":\"1.5.0\",\"id\":\"{id}\",\"name\":\"{id}\",\"version\":\"{version}\",\"suites\":[\"{suiteRef}\"]}}");
    }
}

internal sealed class FakeProcessRunner : IProcessRunner
{
    public Task<ProcessResult> RunAsync(System.Diagnostics.ProcessStartInfo startInfo, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ProcessResult
        {
            ExitCode = 0,
            TimedOut = false,
            Aborted = false,
            Stdout = string.Empty,
            Stderr = string.Empty
        });
    }
}
