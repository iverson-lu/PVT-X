using PcTest.Contracts;
using PcTest.Engine;
using PcTest.Runner;
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class EngineTests
{
    [Fact]
    public void IdentityParser_ParsesIdAndVersion()
    {
        var identity = IdentityParser.Parse("CpuStress@1.0.0");
        Assert.Equal("CpuStress", identity.Id);
        Assert.Equal("1.0.0", identity.Version);
    }

    [Theory]
    [InlineData("Bad Identity")]
    [InlineData("missingversion@")]
    [InlineData("@missingid")]
    [InlineData("two@parts@here")]
    public void IdentityParser_RejectsInvalid(string value)
    {
        var exception = Assert.Throws<PcTestException>(() => IdentityParser.Parse(value));
        Assert.Equal("Identity.ParseFailed", exception.Code);
    }

    [Fact]
    public void Discovery_FailsOnDuplicateIdentity()
    {
        using var temp = new TempFolder();
        var root = temp.Path;
        var caseRoot = Path.Combine(root, "TestCases");
        Directory.CreateDirectory(Path.Combine(caseRoot, "CaseA"));
        Directory.CreateDirectory(Path.Combine(caseRoot, "CaseB"));

        var manifest = new
        {
            schemaVersion = "1.5.0",
            id = "DupCase",
            name = "Dup",
            category = "Cat",
            version = "1.0.0"
        };

        JsonUtilities.WriteFile(Path.Combine(caseRoot, "CaseA", "test.manifest.json"), manifest);
        JsonUtilities.WriteFile(Path.Combine(caseRoot, "CaseB", "test.manifest.json"), manifest);

        var service = new DiscoveryService();
        var exception = Assert.Throws<PcTestException>(() => service.Discover(caseRoot, root, root));
        Assert.Equal("Identity.Duplicate", exception.Code);
        var payload = Assert.IsType<Dictionary<string, object?>>(exception.Payload);
        Assert.Equal("TestCase", payload["entityType"]);
    }

    [Fact]
    public void SuiteRefResolver_ReportsMissingManifest()
    {
        using var temp = new TempFolder();
        var root = temp.Path;
        var caseRoot = Path.Combine(root, "TestCases");
        Directory.CreateDirectory(Path.Combine(caseRoot, "Missing"));
        var suitePath = Path.Combine(root, "suite.manifest.json");
        File.WriteAllText(suitePath, "{}");

        var exception = Assert.Throws<PcTestException>(() => SuiteRefResolver.ResolveTestCaseRef(suitePath, caseRoot, "Missing"));
        Assert.Equal("Suite.TestCaseRef.Invalid", exception.Code);
        var payload = Assert.IsType<Dictionary<string, object?>>(exception.Payload);
        Assert.Equal("MissingManifest", payload["reason"]);
    }

    [Fact]
    public void SuiteRefResolver_RejectsOutOfRootSymlink()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using var temp = new TempFolder();
        var root = temp.Path;
        var caseRoot = Path.Combine(root, "TestCases");
        Directory.CreateDirectory(caseRoot);
        var outside = Path.Combine(root, "Outside");
        Directory.CreateDirectory(Path.Combine(outside, "Case"));
        JsonUtilities.WriteFile(Path.Combine(outside, "Case", "test.manifest.json"), new { schemaVersion = "1.5.0", id = "X", name = "X", category = "X", version = "1.0.0" });
        var linkPath = Path.Combine(caseRoot, "LinkOut");

        try
        {
            Directory.CreateSymbolicLink(linkPath, Path.Combine(outside, "Case"));
        }
        catch
        {
            return;
        }

        var suitePath = Path.Combine(root, "suite.manifest.json");
        File.WriteAllText(suitePath, "{}");
        var exception = Assert.Throws<PcTestException>(() => SuiteRefResolver.ResolveTestCaseRef(suitePath, caseRoot, "LinkOut"));
        Assert.Equal("Suite.TestCaseRef.Invalid", exception.Code);
        var payload = Assert.IsType<Dictionary<string, object?>>(exception.Payload);
        Assert.Equal("OutOfRoot", payload["reason"]);
    }

    [Fact]
    public void PlanRunRequest_DisallowsInputs()
    {
        var request = new RunRequest
        {
            Plan = "PlanA@1.0.0",
            CaseInputs = new Dictionary<string, JsonElement> { ["X"] = JsonSerializer.SerializeToElement(1) }
        };

        var orchestrator = new EngineOrchestrator();
        var options = new EngineOptions { TestCaseRoot = "none", TestSuiteRoot = "none", TestPlanRoot = "none", RunsRoot = "runs" };
        var exception = Assert.Throws<PcTestException>(() => orchestrator.RunPlan(options, request));
        Assert.Equal("RunRequest.Invalid", exception.Code);
    }

    [Fact]
    public void Inputs_RespectPrecedence()
    {
        var manifest = new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "Cpu",
            Name = "Cpu",
            Category = "Thermal",
            Version = "1.0.0",
            Parameters =
            [
                new ParameterDefinition { Name = "DurationSec", Type = "int", Required = true, Default = JsonSerializer.SerializeToElement(5) }
            ]
        };

        var suiteInputs = new Dictionary<string, JsonElement> { ["DurationSec"] = JsonSerializer.SerializeToElement(10) };
        var overrides = new Dictionary<string, JsonElement> { ["DurationSec"] = JsonSerializer.SerializeToElement(20) };
        var env = new Dictionary<string, string>();
        var (effectiveInputs, _, _) = InputResolver.ResolveInputs(manifest, suiteInputs, overrides, env, "node1");
        Assert.Equal(20, effectiveInputs["DurationSec"]);
    }

    [Fact]
    public void EnvRef_ConvertsTypesAndValidatesEnums()
    {
        var manifest = new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "Cpu",
            Name = "Cpu",
            Category = "Thermal",
            Version = "1.0.0",
            Parameters =
            [
                new ParameterDefinition { Name = "Mode", Type = "enum", Required = true, EnumValues = ["A", "B"] }
            ]
        };

        var env = new Dictionary<string, string> { ["MODE"] = "A" };
        var inputs = new Dictionary<string, JsonElement>
        {
            ["Mode"] = JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["$env"] = "MODE" })
        };

        var (effective, _, _) = InputResolver.ResolveInputs(manifest, inputs, null, env, null);
        Assert.Equal("A", effective["Mode"]);

        env["MODE"] = "C";
        var exception = Assert.Throws<PcTestException>(() => InputResolver.ResolveInputs(manifest, inputs, null, env, null));
        Assert.Equal("Input.EnumInvalid", exception.Code);
    }

    [Fact]
    public void Engine_WritesGroupRunFoldersAndIndex()
    {
        using var temp = new TempFolder();
        var testCaseRoot = Path.Combine(temp.Path, "TestCases");
        var testSuiteRoot = Path.Combine(temp.Path, "TestSuites");
        var testPlanRoot = Path.Combine(temp.Path, "TestPlans");
        var runsRoot = Path.Combine(temp.Path, "Runs");
        Directory.CreateDirectory(testCaseRoot);
        Directory.CreateDirectory(testSuiteRoot);
        Directory.CreateDirectory(testPlanRoot);

        var caseFolder = Path.Combine(testCaseRoot, "CaseA");
        Directory.CreateDirectory(caseFolder);
        JsonUtilities.WriteFile(Path.Combine(caseFolder, "test.manifest.json"), new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "CaseA",
            Name = "CaseA",
            Category = "Cat",
            Version = "1.0.0",
            Parameters = [new ParameterDefinition { Name = "DurationSec", Type = "int", Required = true, Default = JsonSerializer.SerializeToElement(1) }]
        });

        var suiteFolder = Path.Combine(testSuiteRoot, "SuiteA");
        Directory.CreateDirectory(suiteFolder);
        JsonUtilities.WriteFile(Path.Combine(suiteFolder, "suite.manifest.json"), new TestSuiteManifest
        {
            SchemaVersion = "1.5.0",
            Id = "SuiteA",
            Name = "SuiteA",
            Version = "1.0.0",
            TestCases =
            [
                new SuiteTestCaseNode { NodeId = "node-1", Ref = "CaseA", Inputs = new Dictionary<string, JsonElement>() }
            ]
        });

        var orchestrator = new EngineOrchestrator(new DiscoveryService(), new TestCaseRunner(new FakeProcessRunner()));
        var suiteRun = orchestrator.RunSuite(new EngineOptions
        {
            TestCaseRoot = testCaseRoot,
            TestSuiteRoot = testSuiteRoot,
            TestPlanRoot = testPlanRoot,
            RunsRoot = runsRoot
        }, new RunRequest { Suite = "SuiteA@1.0.0" });
        Assert.Equal("TestSuite", suiteRun.Summary.RunType);

        var runFolders = Directory.GetDirectories(runsRoot);
        Assert.True(runFolders.Length >= 2);
        var groupFolder = runFolders.Single(path => File.Exists(Path.Combine(path, "children.jsonl")));
        var caseFolderRun = runFolders.Single(path => File.Exists(Path.Combine(path, "params.json")));

        Assert.True(File.Exists(Path.Combine(groupFolder, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(groupFolder, "result.json")));
        Assert.False(File.Exists(Path.Combine(groupFolder, "params.json")));
        Assert.False(File.Exists(Path.Combine(caseFolderRun, "controls.json")));

        var indexPath = Path.Combine(runsRoot, "index.jsonl");
        var indexLines = File.ReadAllLines(indexPath);
        Assert.True(indexLines.Length >= 2);
        Assert.Contains("\"runType\": \"TestSuite\"", indexLines[1]);
    }

    [Fact]
    public void StandaloneRun_DoesNotIncludeSuiteOrPlanInIndex()
    {
        using var temp = new TempFolder();
        var testCaseRoot = Path.Combine(temp.Path, "TestCases");
        Directory.CreateDirectory(testCaseRoot);
        var runsRoot = Path.Combine(temp.Path, "Runs");

        var caseFolder = Path.Combine(testCaseRoot, "CaseA");
        Directory.CreateDirectory(caseFolder);
        JsonUtilities.WriteFile(Path.Combine(caseFolder, "test.manifest.json"), new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "CaseA",
            Name = "CaseA",
            Category = "Cat",
            Version = "1.0.0",
            Parameters = [new ParameterDefinition { Name = "DurationSec", Type = "int", Required = true, Default = JsonSerializer.SerializeToElement(1) }]
        });

        var orchestrator = new EngineOrchestrator(new DiscoveryService(), new TestCaseRunner(new FakeProcessRunner()));
        orchestrator.RunTestCase(new EngineOptions
        {
            TestCaseRoot = testCaseRoot,
            TestSuiteRoot = temp.Path,
            TestPlanRoot = temp.Path,
            RunsRoot = runsRoot
        }, new RunRequest { TestCase = "CaseA@1.0.0" });

        var indexPath = Path.Combine(runsRoot, "index.jsonl");
        var indexLine = File.ReadAllLines(indexPath).Single();
        using var doc = JsonDocument.Parse(indexLine);
        Assert.False(doc.RootElement.TryGetProperty("suiteId", out _));
        Assert.False(doc.RootElement.TryGetProperty("planId", out _));
        Assert.False(doc.RootElement.TryGetProperty("nodeId", out _));
    }
}

internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pctest-" + Guid.NewGuid().ToString("N"));
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
        }
    }
}

internal sealed class FakeProcessRunner : IProcessRunner
{
    public ProcessResult Run(ProcessStartInfo startInfo, TimeSpan? timeout)
    {
        return new ProcessResult
        {
            ExitCode = 0,
            TimedOut = false,
            Aborted = false,
            StandardOutput = string.Empty,
            StandardError = string.Empty,
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow
        };
    }
}
