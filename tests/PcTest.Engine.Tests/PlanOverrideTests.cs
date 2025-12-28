using PcTest.Engine;
using PcTest.Runner;
using PcTest.Contracts;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class PlanOverrideTests
{
    [Fact]
    public void PlanRunRequest_WithInputs_IsRejected()
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

        TestHelpers.WriteJson(Path.Combine(planRoot, "plan.manifest.json"), new
        {
            schemaVersion = "1.5.0",
            id = "Plan",
            name = "Plan",
            version = "1.0.0",
            suites = Array.Empty<object>()
        });

        var engine = new PcTestEngine(new RunnerService());
        var ex = Assert.Throws<ValidationException>(() => engine.RunPlan(
            new EngineOptions(caseRoot, suiteRoot, planRoot, runsRoot),
            new RunRequest
            {
                Plan = "Plan@1.0.0",
                CaseInputs = TestHelpers.InputsFromJson("{\"DurationSec\":1}")
            }));

        Assert.Equal("RunRequest.Plan.Invalid", ex.Code);
    }
}
