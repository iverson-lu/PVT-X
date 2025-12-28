using System.Text.Json;
using PcTest.Contracts;
using PcTest.Engine;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class DiscoveryTests
{
    [Fact]
    public void Discover_FailsOnDuplicateIdentity()
    {
        string root = TestHelpers.CreateTempDirectory();
        string cases = Path.Combine(root, "cases");
        string suites = Path.Combine(root, "suites");
        string plans = Path.Combine(root, "plans");
        Directory.CreateDirectory(cases);
        Directory.CreateDirectory(suites);
        Directory.CreateDirectory(plans);

        TestHelpers.WriteTestCase(cases, "A", "Dup", "1.0.0");
        TestHelpers.WriteTestCase(cases, "B", "Dup", "1.0.0");

        DiscoveryService service = new();
        ValidationResult<DiscoveryResult> result = service.Discover(cases, suites, plans);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Code == "Identity.NotUnique");
    }

    [Fact]
    public void Discover_FailsOnSuiteRefOutOfRoot()
    {
        string root = TestHelpers.CreateTempDirectory();
        string cases = Path.Combine(root, "cases");
        string suites = Path.Combine(root, "suites");
        string plans = Path.Combine(root, "plans");
        Directory.CreateDirectory(cases);
        Directory.CreateDirectory(suites);
        Directory.CreateDirectory(plans);

        TestHelpers.WriteTestCase(cases, "CpuStress", "CpuStress", "1.0.0");

        SuiteManifest suite = new()
        {
            SchemaVersion = "1.5.0",
            Id = "BadSuite",
            Name = "Bad",
            Version = "1.0.0",
            TestCases = new[]
            {
                new SuiteTestCaseNode { NodeId = "bad", Ref = ".." }
            }
        };
        TestHelpers.WriteSuite(suites, "BadSuite", suite);

        DiscoveryService service = new();
        ValidationResult<DiscoveryResult> result = service.Discover(cases, suites, plans);

        Assert.False(result.IsSuccess);
        ValidationError? error = result.Errors.FirstOrDefault(e => e.Code == "Suite.TestCaseRef.Invalid");
        Assert.NotNull(error);
        Assert.Equal("OutOfRoot", error!.Payload["reason"]);
    }

    [Fact]
    public void Inputs_PrioritySuiteAndOverrides()
    {
        ParameterDefinition[] parameters =
        {
            new() { Name = "DurationSec", Type = "int", Required = true, Default = JsonSerializer.SerializeToElement(1) }
        };

        Dictionary<string, JsonElement> suiteInputs = new()
        {
            ["DurationSec"] = JsonSerializer.SerializeToElement(2)
        };

        Dictionary<string, JsonElement> overrides = new()
        {
            ["DurationSec"] = JsonSerializer.SerializeToElement(3)
        };

        InputResolver resolver = new();
        Dictionary<string, JsonElement> merged = new Dictionary<string, JsonElement>(suiteInputs, StringComparer.Ordinal);
        merged["DurationSec"] = overrides["DurationSec"];

        ValidationResult<ResolvedInputs> result = resolver.Resolve(parameters, merged, new Dictionary<string, string>(), "node");

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.ResolvedValues["DurationSec"]);
    }

    [Fact]
    public void EnvRef_ConvertsAndValidatesEnum()
    {
        ParameterDefinition[] parameters =
        {
            new() { Name = "Mode", Type = "enum", Required = true, EnumValues = new[] { "A", "B" } }
        };

        Dictionary<string, JsonElement> inputs = new()
        {
            ["Mode"] = CreateEnvRef("MODE")
        };

        InputResolver resolver = new();
        Dictionary<string, string> env = new(StringComparer.OrdinalIgnoreCase)
        {
            ["MODE"] = "C"
        };

        ValidationResult<ResolvedInputs> result = resolver.Resolve(parameters, inputs, env, "node");

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Code == "EnvRef.ResolveFailed");
    }

    private static JsonElement CreateEnvRef(string name)
    {
        using JsonDocument document = JsonDocument.Parse($"{{\"$env\":\"{name}\",\"required\":true}}");
        return document.RootElement.Clone();
    }
}
