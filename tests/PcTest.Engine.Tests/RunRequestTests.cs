using PcTest.Contracts;
using PcTest.Engine;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class RunRequestTests
{
    [Fact]
    public async Task PlanRunRequest_RejectsNodeOverrides()
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
        PlanManifest plan = new()
        {
            SchemaVersion = "1.5.0",
            Id = "PlanA",
            Name = "PlanA",
            Version = "1.0.0",
            Suites = new[] { "SuiteA@1.0.0" }
        };
        TestHelpers.WritePlan(plans, "PlanA", plan);

        EngineService engine = new();
        ValidationResult<DiscoveryResult> discovery = engine.Discover(cases, suites, plans);
        Assert.True(discovery.IsSuccess);

        RunRequest request = new()
        {
            Plan = "PlanA@1.0.0",
            NodeOverrides = new Dictionary<string, RunNodeOverride>()
        };

        ValidationResult<string> result = await engine.RunAsync(discovery.Value!, request, runs, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Code == "RunRequest.Invalid");
    }
}
