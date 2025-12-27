using System.Text.Json;
using PcTest.Contracts;
using PcTest.Engine;
using PcTest.Runner;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class EngineTests
{
    [Fact]
    public void DiscoveryFailsOnDuplicateIdentity()
    {
        using var temp = new TempFolder();
        var root = Path.Combine(temp.Path, "TestCases");
        Directory.CreateDirectory(root);
        CreateTestCase(root, "Dup", "1.0.0", "A");
        CreateTestCase(root, "Dup", "1.0.0", "B");

        var discovery = new DiscoveryService();
        var roots = new EngineRoots(root, temp.Path, temp.Path, temp.Path);

        var ex = Assert.Throws<EngineException>(() => discovery.Discover(roots));
        Assert.Equal("Discovery.Identity.NonUnique", ex.Code);
        Assert.Equal("TestCase", ex.Payload["entityType"]);
    }

    [Fact]
    public void SuiteRefResolverReportsOutOfRoot()
    {
        using var temp = new TempFolder();
        var testRoot = Path.Combine(temp.Path, "TestCases");
        Directory.CreateDirectory(testRoot);
        var suitePath = Path.Combine(temp.Path, "suite.manifest.json");
        var resolver = new SuiteRefResolver();

        var ex = Assert.Throws<EngineException>(() => resolver.ResolveTestCaseManifest(suitePath, testRoot, ".."));
        Assert.Equal(SchemaConstants.SuiteTestCaseRefError, ex.Code);
        Assert.Equal("OutOfRoot", ex.Payload["reason"]);
    }

    [Fact]
    public void SuiteRefResolverReportsMissingManifest()
    {
        using var temp = new TempFolder();
        var testRoot = Path.Combine(temp.Path, "TestCases");
        Directory.CreateDirectory(testRoot);
        Directory.CreateDirectory(Path.Combine(testRoot, "NoManifest"));
        var suitePath = Path.Combine(temp.Path, "suite.manifest.json");
        var resolver = new SuiteRefResolver();

        var ex = Assert.Throws<EngineException>(() => resolver.ResolveTestCaseManifest(suitePath, testRoot, "NoManifest"));
        Assert.Equal("MissingManifest", ex.Payload["reason"]);
    }

    [Fact]
    public void SuiteRefResolverReportsNotFound()
    {
        using var temp = new TempFolder();
        var testRoot = Path.Combine(temp.Path, "TestCases");
        Directory.CreateDirectory(testRoot);
        var resolver = new SuiteRefResolver();

        var ex = Assert.Throws<EngineException>(() => resolver.ResolveTestCaseManifest("suite.manifest.json", testRoot, "MissingFolder"));
        Assert.Equal("NotFound", ex.Payload["reason"]);
    }

    [Fact]
    public void SuiteRefResolverResolvesSymlinkOutOfRoot()
    {
        using var temp = new TempFolder();
        var testRoot = Path.Combine(temp.Path, "TestCases");
        var outside = Path.Combine(temp.Path, "Outside");
        Directory.CreateDirectory(testRoot);
        Directory.CreateDirectory(outside);
        var linkPath = Path.Combine(testRoot, "LinkOut");
        Directory.CreateSymbolicLink(linkPath, outside);

        var resolver = new SuiteRefResolver();
        var ex = Assert.Throws<EngineException>(() => resolver.ResolveTestCaseManifest("suite.manifest.json", testRoot, "LinkOut"));
        Assert.Equal("OutOfRoot", ex.Payload["reason"]);
    }

    [Fact]
    public async Task PlanRunRequestRejectsInputOverrides()
    {
        using var temp = new TempFolder();
        var roots = CreateRoots(temp);
        CreateTestCase(roots.ResolvedTestCaseRoot, "Cpu", "1.0.0", "Case");
        CreateSuite(roots.ResolvedTestSuiteRoot, "Suite", "1.0.0", "Cpu");
        CreatePlan(roots.ResolvedTestPlanRoot, "Plan", "1.0.0", "Suite@1.0.0");

        var engine = CreateEngine();
        var request = new RunRequest
        {
            Plan = "Plan@1.0.0",
            NodeOverrides = new Dictionary<string, NodeOverride>
            {
                ["node"] = new NodeOverride { Inputs = new Dictionary<string, object?> { ["DurationSec"] = 5 } }
            }
        };

        var ex = await Assert.ThrowsAsync<EngineException>(() => engine.RunAsync(roots, request, CancellationToken.None));
        Assert.Equal("RunRequest.Invalid", ex.Code);
    }

    [Fact]
    public void InputsRespectPriority()
    {
        var manifest = new TestCaseManifest
        {
            SchemaVersion = SchemaConstants.SchemaVersion,
            Id = "Cpu",
            Name = "Cpu",
            Category = "Cat",
            Version = "1.0.0",
            Parameters = new[]
            {
                new ParameterDefinition { Name = "Duration", Type = "int", Required = true, Default = 1 }
            }
        };

        var resolver = new InputResolver();
        var env = new Dictionary<string, string>();
        var resolved = resolver.ResolveInputs(manifest,
            new Dictionary<string, object?> { ["Duration"] = 2 },
            new Dictionary<string, object?> { ["Duration"] = 3 },
            env,
            "node");

        Assert.Equal(3, resolved.EffectiveInputs["Duration"]);
    }

    [Fact]
    public void EnvRefConvertsAndValidatesEnum()
    {
        var manifest = new TestCaseManifest
        {
            SchemaVersion = SchemaConstants.SchemaVersion,
            Id = "Cpu",
            Name = "Cpu",
            Category = "Cat",
            Version = "1.0.0",
            Parameters = new[]
            {
                new ParameterDefinition { Name = "Mode", Type = "enum", Required = true, EnumValues = new[] { "A", "B" } },
                new ParameterDefinition { Name = "Count", Type = "int", Required = true }
            }
        };

        var inputs = new Dictionary<string, object?>
        {
            ["Mode"] = new Dictionary<string, object?> { ["$env"] = "MODE" },
            ["Count"] = new Dictionary<string, object?> { ["$env"] = "COUNT" }
        };

        var env = new Dictionary<string, string> { ["MODE"] = "A", ["COUNT"] = "5" };
        var resolver = new InputResolver();
        var resolved = resolver.ResolveInputs(manifest, inputs, new Dictionary<string, object?>(), env, "node");

        Assert.Equal("A", resolved.EffectiveInputs["Mode"]);
        Assert.Equal(5, resolved.EffectiveInputs["Count"]);
    }

    [Fact]
    public void EnvRefSecretProducesWarningEvent()
    {
        var manifest = new TestCaseManifest
        {
            SchemaVersion = SchemaConstants.SchemaVersion,
            Id = "Cpu",
            Name = "Cpu",
            Category = "Cat",
            Version = "1.0.0",
            Parameters = new[]
            {
                new ParameterDefinition { Name = "Token", Type = "string", Required = true }
            }
        };

        var inputs = new Dictionary<string, object?>
        {
            ["Token"] = new Dictionary<string, object?> { ["$env"] = "TOKEN", ["secret"] = true }
        };

        var env = new Dictionary<string, string> { ["TOKEN"] = "value" };
        var resolver = new InputResolver();
        var resolved = resolver.ResolveInputs(manifest, inputs, new Dictionary<string, object?>(), env, "node");

        Assert.Contains(resolved.Events, ev => ev.Code == SchemaConstants.EnvRefSecretOnCommandLine);
    }

    [Fact]
    public async Task StandaloneIndexEntryOmitsSuiteFields()
    {
        using var temp = new TempFolder();
        var roots = CreateRoots(temp);
        CreateTestCase(roots.ResolvedTestCaseRoot, "Cpu", "1.0.0", "Case");

        var engine = CreateEngine();
        var request = new RunRequest { TestCase = "Cpu@1.0.0" };
        var runId = await engine.RunAsync(roots, request, CancellationToken.None);

        var indexLine = File.ReadAllLines(Path.Combine(roots.ResolvedRunsRoot, "index.jsonl")).Single(line => line.Contains(runId));
        using var doc = JsonDocument.Parse(indexLine);
        Assert.False(doc.RootElement.TryGetProperty("nodeId", out _));
        Assert.False(doc.RootElement.TryGetProperty("suiteId", out _));
        Assert.False(doc.RootElement.TryGetProperty("planId", out _));
    }

    [Fact]
    public async Task StandaloneRunDoesNotCreateGroupFolder()
    {
        using var temp = new TempFolder();
        var roots = CreateRoots(temp);
        CreateTestCase(roots.ResolvedTestCaseRoot, "Cpu", "1.0.0", "Case");

        var engine = CreateEngine();
        var request = new RunRequest { TestCase = "Cpu@1.0.0" };
        await engine.RunAsync(roots, request, CancellationToken.None);

        var groupFolders = Directory.EnumerateDirectories(roots.ResolvedRunsRoot).Where(path => Path.GetFileName(path).StartsWith("G-", StringComparison.Ordinal)).ToList();
        Assert.Empty(groupFolders);
    }

    [Fact]
    public async Task PlanRunWritesSuiteAndCaseIndexEntries()
    {
        using var temp = new TempFolder();
        var roots = CreateRoots(temp);
        CreateTestCase(roots.ResolvedTestCaseRoot, "Cpu", "1.0.0", "Case");
        CreateSuite(roots.ResolvedTestSuiteRoot, "Suite", "1.0.0", "Cpu");
        CreatePlan(roots.ResolvedTestPlanRoot, "Plan", "1.0.0", "Suite@1.0.0");

        var engine = CreateEngine();
        var request = new RunRequest { Plan = "Plan@1.0.0" };
        await engine.RunAsync(roots, request, CancellationToken.None);

        var indexLines = File.ReadAllLines(Path.Combine(roots.ResolvedRunsRoot, "index.jsonl"));
        Assert.Contains(indexLines, line => line.Contains("\"runType\":\"TestPlan\""));
        Assert.Contains(indexLines, line => line.Contains("\"runType\":\"TestSuite\""));
        Assert.Contains(indexLines, line => line.Contains("\"runType\":\"TestCase\""));
    }

    [Fact]
    public async Task SuiteRunWritesGroupRunLayout()
    {
        using var temp = new TempFolder();
        var roots = CreateRoots(temp);
        CreateTestCase(roots.ResolvedTestCaseRoot, "Cpu", "1.0.0", "Case");
        CreateSuiteWithControls(roots.ResolvedTestSuiteRoot, "Suite", "1.0.0", "Cpu");

        var engine = CreateEngine();
        var request = new RunRequest { Suite = "Suite@1.0.0" };
        var groupRunId = await engine.RunAsync(roots, request, CancellationToken.None);
        var groupFolder = Path.Combine(roots.ResolvedRunsRoot, groupRunId);

        Assert.True(File.Exists(Path.Combine(groupFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "controls.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "environment.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "runRequest.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "children.jsonl")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "result.json")));
    }

    private static EngineRoots CreateRoots(TempFolder temp)
    {
        var caseRoot = Path.Combine(temp.Path, "TestCases");
        var suiteRoot = Path.Combine(temp.Path, "TestSuites");
        var planRoot = Path.Combine(temp.Path, "TestPlans");
        var runsRoot = Path.Combine(temp.Path, "Runs");
        Directory.CreateDirectory(caseRoot);
        Directory.CreateDirectory(suiteRoot);
        Directory.CreateDirectory(planRoot);
        Directory.CreateDirectory(runsRoot);
        return new EngineRoots(caseRoot, suiteRoot, planRoot, runsRoot);
    }

    private static void CreateTestCase(string root, string id, string version, string folder)
    {
        var folderPath = Path.Combine(root, folder);
        Directory.CreateDirectory(folderPath);
        var manifest = new TestCaseManifest
        {
            SchemaVersion = SchemaConstants.SchemaVersion,
            Id = id,
            Name = id,
            Category = "Cat",
            Version = version,
            TimeoutSec = 1,
            Parameters = new[]
            {
                new ParameterDefinition { Name = "DurationSec", Type = "int", Required = true, Default = 1 }
            }
        };
        File.WriteAllText(Path.Combine(folderPath, "test.manifest.json"), JsonSerializer.Serialize(manifest, JsonUtilities.SerializerOptions));
        File.WriteAllText(Path.Combine(folderPath, "run.ps1"), "Write-Output 'ok'; exit 0");
    }

    private static void CreateSuite(string root, string id, string version, string caseRef)
    {
        var folderPath = Path.Combine(root, id);
        Directory.CreateDirectory(folderPath);
        var suite = new TestSuiteManifest
        {
            SchemaVersion = SchemaConstants.SchemaVersion,
            Id = id,
            Name = id,
            Version = version,
            TestCases = new[]
            {
                new TestCaseNode { NodeId = "node", Ref = caseRef, Inputs = new Dictionary<string, object?> { ["DurationSec"] = 1 } }
            }
        };
        File.WriteAllText(Path.Combine(folderPath, "suite.manifest.json"), JsonSerializer.Serialize(suite, JsonUtilities.SerializerOptions));
    }

    private static void CreateSuiteWithControls(string root, string id, string version, string caseRef)
    {
        var folderPath = Path.Combine(root, id);
        Directory.CreateDirectory(folderPath);
        var suite = new TestSuiteManifest
        {
            SchemaVersion = SchemaConstants.SchemaVersion,
            Id = id,
            Name = id,
            Version = version,
            Controls = new Dictionary<string, object?> { ["repeat"] = 1 },
            Environment = new SuiteEnvironment { Env = new Dictionary<string, string> { ["LAB_MODE"] = "1" } },
            TestCases = new[]
            {
                new TestCaseNode { NodeId = "node", Ref = caseRef, Inputs = new Dictionary<string, object?> { ["DurationSec"] = 1 } }
            }
        };
        File.WriteAllText(Path.Combine(folderPath, "suite.manifest.json"), JsonSerializer.Serialize(suite, JsonUtilities.SerializerOptions));
    }

    private static void CreatePlan(string root, string id, string version, string suiteRef)
    {
        var folderPath = Path.Combine(root, id);
        Directory.CreateDirectory(folderPath);
        var plan = new TestPlanManifest
        {
            SchemaVersion = SchemaConstants.SchemaVersion,
            Id = id,
            Name = id,
            Version = version,
            Suites = new[] { suiteRef }
        };
        File.WriteAllText(Path.Combine(folderPath, "plan.manifest.json"), JsonSerializer.Serialize(plan, JsonUtilities.SerializerOptions));
    }

    private static EngineService CreateEngine()
    {
        var runner = new TestCaseRunner(new FakeExecutor());
        return new EngineService(runner);
    }

    private sealed class FakeExecutor : IPowerShellExecutor
    {
        public Task<PowerShellExecutionResult> ExecuteAsync(PowerShellExecutionRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PowerShellExecutionResult(0, "ok", string.Empty, false, false));
        }
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pctest-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch
            {
                // ignore
            }
        }
    }
}
