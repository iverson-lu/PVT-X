using PcTest.Contracts;
using PcTest.Engine;
using Xunit;

namespace PcTest.Engine.Tests;

public class PlanRunRequestTests
{
    [Fact]
    public void PlanRunRequest_RejectsInputsOverrides()
    {
        using var temp = new TempFolder();
        var planRoot = temp.CreateSubfolder("plans");
        var suiteRoot = temp.CreateSubfolder("suites");
        var caseRoot = temp.CreateSubfolder("cases");

        var planFolder = Path.Combine(planRoot, "PlanA");
        Directory.CreateDirectory(planFolder);
        JsonUtilities.WriteJson(Path.Combine(planFolder, "plan.manifest.json"), new TestPlanManifest
        {
            Id = "PlanA",
            Name = "Plan",
            Version = "1.0.0",
            Suites = new() { "SuiteA@1.0.0" }
        });

        var suiteFolder = Path.Combine(suiteRoot, "SuiteA");
        Directory.CreateDirectory(suiteFolder);
        JsonUtilities.WriteJson(Path.Combine(suiteFolder, "suite.manifest.json"), new TestSuiteManifest
        {
            Id = "SuiteA",
            Name = "Suite",
            Version = "1.0.0",
            TestCases = new List<TestCaseNode>()
        });

        var discovery = new DiscoveryService().Discover(new DiscoveryRoots
        {
            TestCaseRoot = caseRoot,
            TestSuiteRoot = suiteRoot,
            TestPlanRoot = planRoot
        });

        var engine = new PcTestEngine();
        var options = new EngineRunOptions
        {
            RunsRoot = temp.CreateSubfolder("runs"),
            TestCaseRoot = caseRoot,
            Discovery = discovery
        };
        var runRequest = new RunRequest
        {
            Plan = "PlanA@1.0.0",
            CaseInputs = new Dictionary<string, System.Text.Json.Nodes.JsonNode?> { ["X"] = System.Text.Json.Nodes.JsonValue.Create(1) }
        };

        Assert.Throws<InvalidOperationException>(() => engine.RunPlan(options, runRequest));
    }
}
