using System.Diagnostics;
using System.Text.Json;
using PcTest.Contracts;
using PcTest.Engine;
using PcTest.Runner;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class EngineTests
{
    [Fact]
    public void Discovery_RejectsDuplicateIdentities()
    {
        using var temp = new TempWorkspace();
        string tcRoot = Path.Combine(temp.Root, "TestCases");
        Directory.CreateDirectory(tcRoot);
        CreateTestCase(tcRoot, "CaseA", "1.0.0");
        CreateTestCase(Path.Combine(tcRoot, "Other"), "CaseA", "1.0.0");

        var discovery = new DiscoveryService();
        var ex = Assert.Throws<PcTestException>(() => discovery.Discover(tcRoot, temp.SuiteRoot, temp.PlanRoot));
        Assert.Equal("Discovery.Identity.Duplicate", ex.Code);
        Assert.Equal("TestCase", ex.Payload["entityType"]);
    }

    [Fact]
    public void SuiteRefResolver_OutOfRoot_IsRejected()
    {
        using var temp = new TempWorkspace();
        string tcRoot = temp.CaseRoot;
        string suitePath = Path.Combine(temp.SuiteRoot, "suite.manifest.json");
        var ex = Assert.Throws<PcTestException>(() =>
            SuiteRefResolver.ResolveSuiteTestCaseRef(tcRoot, suitePath, ".."));
        Assert.Equal("Suite.TestCaseRef.Invalid", ex.Code);
        Assert.Equal("OutOfRoot", ex.Payload["reason"]);
    }

    [Fact]
    public void PlanRunRequest_DisallowsInputs()
    {
        using var temp = new TempWorkspace();
        var request = new RunRequest
        {
            Plan = "Plan@1.0",
            CaseInputs = new Dictionary<string, JsonElement> { ["Value"] = JsonDocument.Parse("1").RootElement }
        };

        var engine = new EngineService(new DiscoveryService(), new RunnerService(new FakeProcessRunner(), validatePowerShell: false));
        var roots = new ResolvedRoots(temp.CaseRoot, temp.SuiteRoot, temp.PlanRoot, temp.RunsRoot);
        var ex = Assert.Throws<PcTestException>(() => engine.RunAsync(request, roots, CancellationToken.None).GetAwaiter().GetResult());
        Assert.Equal("RunRequest.Invalid", ex.Code);
    }

    [Fact]
    public void Inputs_Priority_Order_IsApplied()
    {
        using var temp = new TempWorkspace();
        string tcPath = CreateTestCase(temp.CaseRoot, "CaseA", "1.0.0", defaultValue: 1);
        string suitePath = CreateSuite(temp.SuiteRoot, "SuiteA", "1.0.0", "CaseA", 2);

        var discovery = new DiscoveryService();
        DiscoveryResult result = discovery.Discover(temp.CaseRoot, temp.SuiteRoot, temp.PlanRoot);
        var request = new RunRequest
        {
            Suite = "SuiteA@1.0.0",
            NodeOverrides = new Dictionary<string, NodeOverride>
            {
                ["node-1"] = new NodeOverride
                {
                    Inputs = new Dictionary<string, JsonElement>
                    {
                        ["Value"] = JsonDocument.Parse("3").RootElement
                    }
                }
            }
        };

        var runner = new FakeRunner();
        var engine = new EngineService(discovery, runner);
        engine.RunAsync(request, new ResolvedRoots(temp.CaseRoot, temp.SuiteRoot, temp.PlanRoot, temp.RunsRoot), CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(3, runner.LastInputs["Value"]);
    }

    [Fact]
    public void Standalone_IndexEntry_ExcludesSuitePlanFields()
    {
        using var temp = new TempWorkspace();
        CreateTestCase(temp.CaseRoot, "CaseA", "1.0.0");
        var discovery = new DiscoveryService();
        var runner = new FakeRunner();
        var engine = new EngineService(discovery, runner);

        var request = new RunRequest { TestCase = "CaseA@1.0.0" };
        engine.RunAsync(request, new ResolvedRoots(temp.CaseRoot, temp.SuiteRoot, temp.PlanRoot, temp.RunsRoot), CancellationToken.None).GetAwaiter().GetResult();

        string indexPath = Path.Combine(temp.RunsRoot, "index.jsonl");
        string line = File.ReadAllLines(indexPath).Single();
        Assert.DoesNotContain("suiteId", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("planId", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("parentRunId", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunFolder_Ownership_IsRespected()
    {
        using var temp = new TempWorkspace();
        CreateTestCase(temp.CaseRoot, "CaseA", "1.0.0");
        CreateSuite(temp.SuiteRoot, "SuiteA", "1.0.0", "CaseA", 1);

        var runner = new RunnerService(new FakeProcessRunner(), validatePowerShell: false);
        var engine = new EngineService(new DiscoveryService(), runner);
        var request = new RunRequest { Suite = "SuiteA@1.0.0" };
        engine.RunAsync(request, new ResolvedRoots(temp.CaseRoot, temp.SuiteRoot, temp.PlanRoot, temp.RunsRoot), CancellationToken.None).GetAwaiter().GetResult();

        string groupFolder = Directory.GetDirectories(temp.RunsRoot).First(path => Path.GetFileName(path).StartsWith("G-", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(groupFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "controls.json")));

        string caseFolder = Directory.GetDirectories(temp.RunsRoot).First(path => Path.GetFileName(path).StartsWith("R-", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(caseFolder, "controls.json")));
        Assert.False(File.Exists(Path.Combine(caseFolder, "children.jsonl")));
    }

    [Fact]
    public void EnvRef_Enum_IsValidated()
    {
        var testCase = new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "Case",
            Name = "Case",
            Category = "Cat",
            Version = "1.0.0",
            Parameters = new[]
            {
                new ParameterDefinition
                {
                    Name = "Mode",
                    Type = ParameterType.Enum,
                    Required = true,
                    EnumValues = new[] { "A", "B" }
                }
            }
        };

        var inputs = new Dictionary<string, JsonElement>
        {
            ["Mode"] = JsonDocument.Parse("{\"env\":\"MODE\",\"type\":\"enum\"}").RootElement
        };

        var env = new Dictionary<string, string> { ["MODE"] = "C" };
        var ex = Assert.Throws<PcTestException>(() => InputResolver.ResolveInputs(testCase, inputs, null, env));
        Assert.Equal("Inputs.Enum", ex.Code);
    }

    private static string CreateTestCase(string root, string id, string version, int defaultValue = 1)
    {
        Directory.CreateDirectory(root);
        string folder = Path.Combine(root, id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "run.ps1"), "Write-Output 'ok'");
        var manifest = new
        {
            schemaVersion = "1.5.0",
            id,
            name = id,
            category = "Cat",
            version,
            parameters = new[] { new { name = "Value", type = "int", required = true, @default = defaultValue } }
        };
        JsonUtilities.WriteJsonFile(Path.Combine(folder, "test.manifest.json"), manifest);
        return folder;
    }

    private static string CreateSuite(string root, string id, string version, string testCaseId, int suiteValue)
    {
        Directory.CreateDirectory(root);
        string folder = Path.Combine(root, id);
        Directory.CreateDirectory(folder);
        var manifest = new
        {
            schemaVersion = "1.5.0",
            id,
            name = id,
            version,
            testCases = new[]
            {
                new { nodeId = "node-1", @ref = testCaseId, inputs = new { Value = suiteValue } }
            }
        };
        JsonUtilities.WriteJsonFile(Path.Combine(folder, "suite.manifest.json"), manifest);
        return folder;
    }
}

internal sealed class TempWorkspace : IDisposable
{
    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "pctest-" + Guid.NewGuid().ToString("N"));
        CaseRoot = Path.Combine(Root, "TestCases");
        SuiteRoot = Path.Combine(Root, "TestSuites");
        PlanRoot = Path.Combine(Root, "TestPlans");
        RunsRoot = Path.Combine(Root, "Runs");
        Directory.CreateDirectory(CaseRoot);
        Directory.CreateDirectory(SuiteRoot);
        Directory.CreateDirectory(PlanRoot);
        Directory.CreateDirectory(RunsRoot);
    }

    public string Root { get; }
    public string CaseRoot { get; }
    public string SuiteRoot { get; }
    public string PlanRoot { get; }
    public string RunsRoot { get; }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, true);
        }
    }
}

internal sealed class FakeProcessRunner : IProcessRunner
{
    public Task<ProcessRunResult> RunAsync(ProcessStartInfo startInfo, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ProcessRunResult(0, string.Empty, string.Empty, false));
    }
}

internal sealed class FakeRunner : IRunner
{
    public IReadOnlyDictionary<string, object> LastInputs { get; private set; } = new Dictionary<string, object>();

    public Task<RunnerResult> RunTestCaseAsync(RunnerRequest request, CancellationToken cancellationToken)
    {
        LastInputs = request.EffectiveInputs;
        return Task.FromResult(new RunnerResult("R-1", "Passed", DateTimeOffset.UtcNow.ToString("O"), DateTimeOffset.UtcNow.ToString("O"), request.NodeId, request.ParentRunId));
    }
}
