using System.Text.Json;
using PcTest.Contracts;
using PcTest.Engine;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class InputResolutionTests
{
    [Fact]
    public void ResolveInputs_UsesCorrectPrecedence_ForSuite()
    {
        var manifest = new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "Cpu",
            Name = "CPU",
            Category = "Thermal",
            Version = "1.0.0",
            Parameters = new[]
            {
                new ParameterDefinition { Name = "DurationSec", Type = "int", Required = true, Default = JsonDocument.Parse("1").RootElement }
            }
        };

        var suiteInputs = TestHelpers.InputsFromJson("{\"DurationSec\": 5}");
        var overrides = TestHelpers.InputsFromJson("{\"DurationSec\": 7}");
        var result = Resolution.ResolveInputs(manifest, suiteInputs, overrides, new Dictionary<string, string>(), "node");

        Assert.Equal(7, result.EffectiveInputs["DurationSec"]);
    }

    [Fact]
    public void ResolveInputs_EnvRef_ConvertsAndValidatesEnums()
    {
        var manifest = new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "Cpu",
            Name = "CPU",
            Category = "Thermal",
            Version = "1.0.0",
            Parameters = new[]
            {
                new ParameterDefinition { Name = "Mode", Type = "enum", Required = true, EnumValues = new[] { "A", "B" } }
            }
        };

        var inputs = TestHelpers.InputsFromJson("{\"Mode\": {\"$env\": \"MODE\", \"required\": true }}");
        var env = new Dictionary<string, string> { ["MODE"] = "A" };
        var result = Resolution.ResolveInputs(manifest, inputs, null, env, "node");

        Assert.Equal("A", result.EffectiveInputs["Mode"]);

        var badEnv = new Dictionary<string, string> { ["MODE"] = "C" };
        Assert.Throws<ValidationException>(() => Resolution.ResolveInputs(manifest, inputs, null, badEnv, "node"));
    }

    [Fact]
    public void ResolveInputs_Standalone_UsesCaseInputsOverDefaults()
    {
        var manifest = new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "Cpu",
            Name = "CPU",
            Category = "Thermal",
            Version = "1.0.0",
            Parameters = new[]
            {
                new ParameterDefinition { Name = "DurationSec", Type = "int", Required = true, Default = JsonDocument.Parse("3").RootElement }
            }
        };

        var caseInputs = TestHelpers.InputsFromJson("{\"DurationSec\": 10}");
        var result = Resolution.ResolveInputs(manifest, null, caseInputs, new Dictionary<string, string>(), null);

        Assert.Equal(10, result.EffectiveInputs["DurationSec"]);
    }
}
