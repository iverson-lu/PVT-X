using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Models;
using PcTest.Engine;
using PcTest.Runner;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class DiscoveryTests
{
    [Fact]
    public void Discover_DuplicateTestCaseIdentity_ThrowsWithConflictPaths()
    {
        using var temp = new TempRoot();
        var root = temp.CreateDirectory("cases");
        var caseA = Path.Combine(root, "A");
        var caseB = Path.Combine(root, "B");
        Directory.CreateDirectory(caseA);
        Directory.CreateDirectory(caseB);
        WriteManifest(Path.Combine(caseA, "test.manifest.json"), "CpuStress", "1.0.0");
        WriteManifest(Path.Combine(caseB, "test.manifest.json"), "CpuStress", "1.0.0");

        var suiteRoot = temp.CreateDirectory("suites");
        var planRoot = temp.CreateDirectory("plans");

        var ex = Assert.Throws<EngineException>(() => new DiscoveryService().Discover(root, suiteRoot, planRoot));
        Assert.Equal("Identity.NonUnique", ex.Code);
        var payload = JsonSerializer.Serialize(ex.Payload, JsonUtil.SerializerOptions);
        Assert.Contains("CpuStress", payload);
        Assert.Contains("conflictPaths", payload);
    }

    [Fact]
    public void Discover_SuiteRefOutOfRoot_ThrowsSuiteTestCaseRefInvalid()
    {
        using var temp = new TempRoot();
        var caseRoot = temp.CreateDirectory("cases");
        var suiteRoot = temp.CreateDirectory("suites");
        var planRoot = temp.CreateDirectory("plans");
        var suiteDir = Path.Combine(suiteRoot, "SuiteA");
        Directory.CreateDirectory(suiteDir);
        var suiteManifest = new
        {
            schemaVersion = "1.5.0",
            id = "SuiteA",
            name = "SuiteA",
            version = "1.0.0",
            testCases = new[]
            {
                new { nodeId = "node", @ref = "../outside", inputs = new { } }
            }
        };
        JsonUtil.WriteJsonFile(Path.Combine(suiteDir, "suite.manifest.json"), suiteManifest);

        var ex = Assert.Throws<EngineException>(() => new DiscoveryService().Discover(caseRoot, suiteRoot, planRoot));
        Assert.Equal("Suite.TestCaseRef.Invalid", ex.Code);
        var payload = JsonSerializer.Serialize(ex.Payload, JsonUtil.SerializerOptions);
        Assert.Contains("OutOfRoot", payload);
        Assert.Contains("suitePath", payload);
    }

    [Fact]
    public void Discover_SuiteRefSymlinkOutOfRoot_Throws()
    {
        using var temp = new TempRoot();
        var caseRoot = temp.CreateDirectory("cases");
        var suiteRoot = temp.CreateDirectory("suites");
        var planRoot = temp.CreateDirectory("plans");
        var outside = temp.CreateDirectory("outside");

        try
        {
            Directory.CreateSymbolicLink(Path.Combine(caseRoot, "link"), outside);
        }
        catch
        {
            return;
        }

        var suiteDir = Path.Combine(suiteRoot, "SuiteA");
        Directory.CreateDirectory(suiteDir);
        var suiteManifest = new
        {
            schemaVersion = "1.5.0",
            id = "SuiteA",
            name = "SuiteA",
            version = "1.0.0",
            testCases = new[] { new { nodeId = "node", @ref = "link" } }
        };
        JsonUtil.WriteJsonFile(Path.Combine(suiteDir, "suite.manifest.json"), suiteManifest);

        var ex = Assert.Throws<EngineException>(() => new DiscoveryService().Discover(caseRoot, suiteRoot, planRoot));
        Assert.Equal("Suite.TestCaseRef.Invalid", ex.Code);
        var payload = JsonSerializer.Serialize(ex.Payload, JsonUtil.SerializerOptions);
        Assert.Contains("OutOfRoot", payload);
    }

    [Fact]
    public async Task RunSuite_InputsResolveWithOverridesPriority()
    {
        using var temp = new TempRoot();
        var caseRoot = temp.CreateDirectory("cases");
        var suiteRoot = temp.CreateDirectory("suites");
        var planRoot = temp.CreateDirectory("plans");
        var runsRoot = temp.CreateDirectory("runs");

        var testCaseDir = Path.Combine(caseRoot, "CpuStress");
        Directory.CreateDirectory(testCaseDir);
        JsonUtil.WriteJsonFile(Path.Combine(testCaseDir, "test.manifest.json"), new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "CpuStress",
            Name = "CPU",
            Category = "Thermal",
            Version = "1.0.0",
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "DurationSec", Type = "int", Required = true, Default = JsonDocument.Parse("30").RootElement.Clone() }
            }
        });

        var suiteDir = Path.Combine(suiteRoot, "SuiteA");
        Directory.CreateDirectory(suiteDir);
        JsonUtil.WriteJsonFile(Path.Combine(suiteDir, "suite.manifest.json"), new TestSuiteManifest
        {
            SchemaVersion = "1.5.0",
            Id = "SuiteA",
            Name = "SuiteA",
            Version = "1.0.0",
            TestCases = new List<TestCaseNode>
            {
                new() { NodeId = "node", Ref = "CpuStress", Inputs = new Dictionary<string, JsonElement> { ["DurationSec"] = JsonDocument.Parse("60").RootElement.Clone() } }
            }
        });

        var discovery = new DiscoveryService().Discover(caseRoot, suiteRoot, planRoot);
        var fakeRunner = new CapturingRunner();
        var engine = new EngineRunner(discovery, runsRoot, fakeRunner);

        var request = new RunRequest
        {
            Suite = "SuiteA@1.0.0",
            NodeOverrides = new Dictionary<string, NodeOverride>
            {
                ["node"] = new NodeOverride { Inputs = new Dictionary<string, JsonElement> { ["DurationSec"] = JsonDocument.Parse("90").RootElement.Clone() } }
            }
        };

        await engine.RunSuiteAsync(request);
        Assert.Equal(90, fakeRunner.LastRequest?.EffectiveInputs["DurationSec"]);
    }

    [Fact]
    public async Task RunPlan_RejectsInputOverrides()
    {
        using var temp = new TempRoot();
        var caseRoot = temp.CreateDirectory("cases");
        var suiteRoot = temp.CreateDirectory("suites");
        var planRoot = temp.CreateDirectory("plans");
        var runsRoot = temp.CreateDirectory("runs");

        var testCaseDir = Path.Combine(caseRoot, "CpuStress");
        Directory.CreateDirectory(testCaseDir);
        WriteManifest(Path.Combine(testCaseDir, "test.manifest.json"), "CpuStress", "1.0.0");

        var suiteDir = Path.Combine(suiteRoot, "SuiteA");
        Directory.CreateDirectory(suiteDir);
        JsonUtil.WriteJsonFile(Path.Combine(suiteDir, "suite.manifest.json"), new
        {
            schemaVersion = "1.5.0",
            id = "SuiteA",
            name = "SuiteA",
            version = "1.0.0",
            testCases = new[] { new { nodeId = "node", @ref = "CpuStress" } }
        });

        var planDir = Path.Combine(planRoot, "PlanA");
        Directory.CreateDirectory(planDir);
        JsonUtil.WriteJsonFile(Path.Combine(planDir, "plan.manifest.json"), new
        {
            schemaVersion = "1.5.0",
            id = "PlanA",
            name = "PlanA",
            version = "1.0.0",
            suites = new[] { "SuiteA@1.0.0" }
        });

        var discovery = new DiscoveryService().Discover(caseRoot, suiteRoot, planRoot);
        var engine = new EngineRunner(discovery, runsRoot, new CapturingRunner());
        var request = new RunRequest
        {
            Plan = "PlanA@1.0.0",
            CaseInputs = new Dictionary<string, JsonElement> { ["DurationSec"] = JsonDocument.Parse("5").RootElement.Clone() }
        };

        var ex = await Assert.ThrowsAsync<EngineException>(() => engine.RunPlanAsync(request));
        Assert.Equal("RunRequest.Invalid", ex.Code);
    }

    [Fact]
    public async Task RunSuite_WritesGroupRunArtifactsAndIndex()
    {
        using var temp = new TempRoot();
        var caseRoot = temp.CreateDirectory("cases");
        var suiteRoot = temp.CreateDirectory("suites");
        var planRoot = temp.CreateDirectory("plans");
        var runsRoot = temp.CreateDirectory("runs");

        var testCaseDir = Path.Combine(caseRoot, "CpuStress");
        Directory.CreateDirectory(testCaseDir);
        WriteManifest(Path.Combine(testCaseDir, "test.manifest.json"), "CpuStress", "1.0.0");

        var suiteDir = Path.Combine(suiteRoot, "SuiteA");
        Directory.CreateDirectory(suiteDir);
        JsonUtil.WriteJsonFile(Path.Combine(suiteDir, "suite.manifest.json"), new
        {
            schemaVersion = "1.5.0",
            id = "SuiteA",
            name = "SuiteA",
            version = "1.0.0",
            testCases = new[] { new { nodeId = "node", @ref = "CpuStress" } }
        });

        var discovery = new DiscoveryService().Discover(caseRoot, suiteRoot, planRoot);
        var engine = new EngineRunner(discovery, runsRoot, new CapturingRunner());

        await engine.RunSuiteAsync(new RunRequest { Suite = "SuiteA@1.0.0" });

        var groupFolder = Directory.GetDirectories(runsRoot).First(path => Path.GetFileName(path).StartsWith("S-"));
        Assert.True(File.Exists(Path.Combine(groupFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "controls.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "environment.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "runRequest.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "children.jsonl")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "result.json")));
        Assert.True(File.Exists(Path.Combine(runsRoot, "index.jsonl")));
    }

    private static void WriteManifest(string path, string id, string version)
    {
        JsonUtil.WriteJsonFile(path, new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = id,
            Name = id,
            Category = "Thermal",
            Version = version,
            Parameters = new List<ParameterDefinition>()
        });
    }
}

internal sealed class CapturingRunner : ITestCaseRunner
{
    public RunnerRequest? LastRequest { get; private set; }

    public Task<TestCaseRunResult> RunAsync(RunnerRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        var result = new TestCaseRunResult
        {
            RunId = "R-TEST",
            RunFolder = Path.Combine(request.RunsRoot, "R-TEST"),
            StartTime = DateTimeOffset.UtcNow,
            Result = new TestCaseResult
            {
                TestId = request.Manifest.Id,
                TestVersion = request.Manifest.Version,
                Status = RunStatus.Passed,
                StartTime = DateTimeOffset.UtcNow.ToString("O"),
                EndTime = DateTimeOffset.UtcNow.ToString("O"),
                EffectiveInputs = request.RedactedInputs
            }
        };
        return Task.FromResult(result);
    }
}

internal sealed class TempRoot : IDisposable
{
    private readonly string _root;

    public TempRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "pctest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public string CreateDirectory(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
