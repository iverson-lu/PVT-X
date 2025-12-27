using System.Text.Json.Nodes;
using PcTest.Contracts;
using PcTest.Engine;
using Xunit;

namespace PcTest.Engine.Tests;

public class SuiteInputPriorityTests
{
    [Fact]
    public void SuiteInputs_OverrideDefaults_AndNodeOverridesWin()
    {
        using var temp = new TempFolder();
        var caseRoot = temp.CreateSubfolder("cases");
        var suiteRoot = temp.CreateSubfolder("suites");
        var planRoot = temp.CreateSubfolder("plans");
        var runsRoot = temp.CreateSubfolder("runs");

        var caseFolder = Path.Combine(caseRoot, "CaseA");
        Directory.CreateDirectory(caseFolder);
        JsonUtilities.WriteJson(Path.Combine(caseFolder, "test.manifest.json"), new TestCaseManifest
        {
            Id = "CaseA",
            Name = "Case",
            Category = "Cat",
            Version = "1.0.0",
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "Duration", Type = "int", Required = true, Default = JsonValue.Create(1) }
            }
        });

        var suiteFolder = Path.Combine(suiteRoot, "SuiteA");
        Directory.CreateDirectory(suiteFolder);
        JsonUtilities.WriteJson(Path.Combine(suiteFolder, "suite.manifest.json"), new TestSuiteManifest
        {
            Id = "SuiteA",
            Name = "Suite",
            Version = "1.0.0",
            TestCases = new List<TestCaseNode>
            {
                new()
                {
                    NodeId = "node-1",
                    Ref = "CaseA",
                    Inputs = new Dictionary<string, JsonNode?> { ["Duration"] = JsonValue.Create(2) }
                }
            }
        });

        var discovery = new DiscoveryService().Discover(new DiscoveryRoots
        {
            TestCaseRoot = caseRoot,
            TestSuiteRoot = suiteRoot,
            TestPlanRoot = planRoot
        });
        var fakeRunner = new FakeRunner();
        var engine = new PcTestEngine();
        var options = new EngineRunOptions
        {
            RunsRoot = runsRoot,
            TestCaseRoot = caseRoot,
            Discovery = discovery,
            Runner = fakeRunner
        };

        var runRequest = new RunRequest
        {
            Suite = "SuiteA@1.0.0",
            NodeOverrides = new Dictionary<string, NodeOverride>
            {
                ["node-1"] = new NodeOverride
                {
                    Inputs = new Dictionary<string, JsonNode?> { ["Duration"] = JsonValue.Create(3) }
                }
            }
        };

        engine.RunSuite(options, runRequest);
        Assert.Single(fakeRunner.Requests);
        Assert.Equal(3, fakeRunner.Requests[0].EffectiveInputs["Duration"]);
    }
}
