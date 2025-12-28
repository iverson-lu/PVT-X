using System.Text.Json;
using PcTest.Contracts;
using PcTest.Engine;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class EngineTests
{
    [Fact]
    public void DiscoveryRejectsDuplicateIdentities()
    {
        using var temp = new TempFolder();
        var caseRoot = temp.CreateSubfolder("cases");
        var suiteRoot = temp.CreateSubfolder("suites");
        var planRoot = temp.CreateSubfolder("plans");

        var manifest = """
        {
          "schemaVersion": "1.5.0",
          "id": "DupCase",
          "name": "Dup",
          "category": "Test",
          "version": "1.0.0"
        }
        """;

        var caseA = Path.Combine(caseRoot, "A");
        var caseB = Path.Combine(caseRoot, "B");
        Directory.CreateDirectory(caseA);
        Directory.CreateDirectory(caseB);
        File.WriteAllText(Path.Combine(caseA, "test.manifest.json"), manifest);
        File.WriteAllText(Path.Combine(caseB, "test.manifest.json"), manifest);

        var ex = Assert.Throws<PcTestException>(() => DiscoveryService.Discover(new DiscoveryOptions
        {
            TestCaseRoot = caseRoot,
            TestSuiteRoot = suiteRoot,
            TestPlanRoot = planRoot
        }));

        Assert.Contains(ex.Errors, error => error.Code == "Identity.Duplicate");
    }

    [Fact]
    public async Task SuiteRefOutOfRootViaSymlinkIsRejected()
    {
        using var temp = new TempFolder();
        var caseRoot = temp.CreateSubfolder("cases");
        var suiteRoot = temp.CreateSubfolder("suites");
        var planRoot = temp.CreateSubfolder("plans");
        var outsideRoot = temp.CreateSubfolder("outside");

        var manifest = """
        {
          "schemaVersion": "1.5.0",
          "id": "CpuStress",
          "name": "CPU",
          "category": "Thermal",
          "version": "1.0.0"
        }
        """;

        Directory.CreateDirectory(Path.Combine(outsideRoot, "Escape"));
        File.WriteAllText(Path.Combine(outsideRoot, "Escape", "test.manifest.json"), manifest);

        var linkPath = Path.Combine(caseRoot, "Escape");
        Directory.CreateDirectory(caseRoot);
        // Assumption: symbolic link creation is supported in the test environment to cover reparse point handling.
        Directory.CreateSymbolicLink(linkPath, Path.Combine(outsideRoot, "Escape"));

        var suiteManifest = """
        {
          "schemaVersion": "1.5.0",
          "id": "ThermalSuite",
          "name": "Thermal",
          "version": "1.0.0",
          "testCases": [
            { "nodeId": "cpu", "ref": "Escape" }
          ]
        }
        """;
        Directory.CreateDirectory(Path.Combine(suiteRoot, "Thermal"));
        File.WriteAllText(Path.Combine(suiteRoot, "Thermal", "suite.manifest.json"), suiteManifest);

        var discovery = DiscoveryService.Discover(new DiscoveryOptions
        {
            TestCaseRoot = caseRoot,
            TestSuiteRoot = suiteRoot,
            TestPlanRoot = planRoot
        });

        var runner = new StubRunner();
        var engine = new EngineService(runner, new EngineOptions { RunsRoot = temp.CreateSubfolder("runs") });
        var request = new RunRequest { Suite = "ThermalSuite@1.0.0" };

        var ex = await Assert.ThrowsAsync<PcTestException>(() => engine.ExecuteAsync(discovery, request, CancellationToken.None));
        Assert.Contains(ex.Errors, error => error.Code == SpecConstants.SuiteTestCaseRefInvalid);
    }

    [Fact]
    public async Task PlanRunRequestRejectsInputOverrides()
    {
        using var temp = new TempFolder();
        var caseRoot = temp.CreateSubfolder("cases");
        var suiteRoot = temp.CreateSubfolder("suites");
        var planRoot = temp.CreateSubfolder("plans");

        var planManifest = """
        {
          "schemaVersion": "1.5.0",
          "id": "Plan",
          "name": "Plan",
          "version": "1.0.0",
          "suites": []
        }
        """;
        Directory.CreateDirectory(Path.Combine(planRoot, "Plan"));
        File.WriteAllText(Path.Combine(planRoot, "Plan", "plan.manifest.json"), planManifest);

        var discovery = DiscoveryService.Discover(new DiscoveryOptions
        {
            TestCaseRoot = caseRoot,
            TestSuiteRoot = suiteRoot,
            TestPlanRoot = planRoot
        });

        var engine = new EngineService(new StubRunner(), new EngineOptions { RunsRoot = temp.CreateSubfolder("runs") });
        var request = new RunRequest { Plan = "Plan@1.0.0", CaseInputs = new Dictionary<string, JsonElement>() };

        var ex = await Assert.ThrowsAsync<PcTestException>(() => engine.ExecuteAsync(discovery, request, CancellationToken.None));
        Assert.Contains(ex.Errors, error => error.Code == "RunRequest.Invalid");
    }

    [Fact]
    public void InputPriorityOrderIsRespected()
    {
        var parameters = new[]
        {
            new ParameterDefinition { Name = "DurationSec", Type = "int", Required = true, Default = JsonUtils.Parse("5") }
        };

        var inputs = new Dictionary<string, JsonElement>
        {
            ["DurationSec"] = JsonUtils.Parse("10")
        };

        var overrides = new Dictionary<string, JsonElement>
        {
            ["DurationSec"] = JsonUtils.Parse("20")
        };

        var env = new Dictionary<string, string>();
        var result = InputResolver.ResolveInputs(inputs, overrides, parameters, env, "node");

        Assert.Equal(20, result.EffectiveInputs["DurationSec"]);
    }

    [Fact]
    public void EnvRefConversionAndEnumValidationWorks()
    {
        var parameters = new[]
        {
            new ParameterDefinition { Name = "Mode", Type = "enum", Required = true, EnumValues = new[] { "A", "B" } },
            new ParameterDefinition { Name = "Flags", Type = "boolean", Required = true },
            new ParameterDefinition { Name = "Modes", Type = "enum[]", Required = false, EnumValues = new[] { "A", "B" } }
        };

        var inputs = new Dictionary<string, JsonElement>
        {
            ["Mode"] = JsonUtils.Parse("{\"$env\":\"MODE\",\"required\":true}"),
            ["Flags"] = JsonUtils.Parse("{\"$env\":\"FLAG\"}"),
            ["Modes"] = JsonUtils.Parse("{\"$env\":\"MODES\"}")
        };

        var env = new Dictionary<string, string>
        {
            ["MODE"] = "A",
            ["FLAG"] = "true",
            ["MODES"] = "[\"A\",\"B\"]"
        };

        var resolved = InputResolver.ResolveInputs(inputs, null, parameters, env, "node");
        Assert.Equal("A", resolved.EffectiveInputs["Mode"]);
        Assert.Equal(true, resolved.EffectiveInputs["Flags"]);
        var modes = Assert.IsAssignableFrom<IEnumerable<object?>>(resolved.EffectiveInputs["Modes"]!);
        Assert.Equal(new object?[] { "A", "B" }, modes.ToArray());
    }

    [Fact]
    public void EnvRefEnumRejectsInvalidValue()
    {
        var parameters = new[]
        {
            new ParameterDefinition { Name = "Mode", Type = "enum", Required = true, EnumValues = new[] { "A", "B" } }
        };

        var inputs = new Dictionary<string, JsonElement>
        {
            ["Mode"] = JsonUtils.Parse("{\"$env\":\"MODE\",\"required\":true}")
        };

        var env = new Dictionary<string, string> { ["MODE"] = "C" };
        var ex = Assert.Throws<PcTestException>(() => InputResolver.ResolveInputs(inputs, null, parameters, env, "node"));
        Assert.Contains(ex.Errors, error => error.Code == SpecConstants.EnvRefResolveFailed);
    }
}

internal sealed class StubRunner : ICaseRunner
{
    public Task<CaseRunResult> RunCaseAsync(CaseRunRequest request, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Runner should not be invoked in this test.");
    }
}

internal sealed class TempFolder : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "PcTest", Guid.NewGuid().ToString("N"));

    public TempFolder()
    {
        Directory.CreateDirectory(Root);
    }

    public string CreateSubfolder(string name)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, true);
        }
    }
}
