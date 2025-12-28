using System.Text.Json;
using PcTest.Contracts;
using Xunit;

namespace PcTest.Contracts.Tests;

public sealed class InputResolverTests
{
    [Fact]
    public void Resolves_Precedence_For_Suite()
    {
        List<ParameterDefinition> parameters = new()
        {
            new ParameterDefinition { Name = "DurationSec", Type = "int", Required = true, Default = JsonDocument.Parse("1").RootElement },
            new ParameterDefinition { Name = "Mode", Type = "enum", Required = false, EnumValues = new[] { "A", "B" }, Default = JsonDocument.Parse("\"A\"").RootElement }
        };

        Dictionary<string, JsonElement> defaults = InputResolver.ExtractDefaults(parameters);
        Dictionary<string, JsonElement> suiteInputs = new(StringComparer.OrdinalIgnoreCase)
        {
            ["DurationSec"] = JsonDocument.Parse("2").RootElement
        };
        Dictionary<string, JsonElement> overrides = new(StringComparer.OrdinalIgnoreCase)
        {
            ["DurationSec"] = JsonDocument.Parse("3").RootElement,
            ["Mode"] = JsonDocument.Parse("\"B\"").RootElement
        };

        EffectiveInputsResult result = InputResolver.ResolveEffectiveInputs(parameters, defaults, suiteInputs, overrides, new Dictionary<string, string>(), out ValidationResult validation);

        Assert.True(validation.IsValid);
        Assert.Equal(3, result.Inputs["DurationSec"]);
        Assert.Equal("B", result.Inputs["Mode"]);
    }

    [Fact]
    public void EnvRef_Converts_And_Validates_Enum()
    {
        List<ParameterDefinition> parameters = new()
        {
            new ParameterDefinition { Name = "Mode", Type = "enum", Required = true, EnumValues = new[] { "A", "B" } },
            new ParameterDefinition { Name = "Flag", Type = "boolean", Required = true }
        };

        Dictionary<string, JsonElement> defaults = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, JsonElement> overrides = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Mode"] = JsonDocument.Parse("{\"envRef\":\"MODE\"}").RootElement,
            ["Flag"] = JsonDocument.Parse("{\"envRef\":\"FLAG\"}").RootElement
        };

        Dictionary<string, string> env = new(StringComparer.OrdinalIgnoreCase)
        {
            ["MODE"] = "B",
            ["FLAG"] = "1"
        };

        EffectiveInputsResult result = InputResolver.ResolveEffectiveInputs(parameters, defaults, null, overrides, env, out ValidationResult validation);
        Assert.True(validation.IsValid);
        Assert.Equal("B", result.Inputs["Mode"]);
        Assert.Equal(true, result.Inputs["Flag"]);
    }

    [Fact]
    public void EnvRef_Rejects_Invalid_Enum()
    {
        List<ParameterDefinition> parameters = new()
        {
            new ParameterDefinition { Name = "Mode", Type = "enum", Required = true, EnumValues = new[] { "A", "B" } }
        };

        Dictionary<string, JsonElement> defaults = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, JsonElement> overrides = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Mode"] = JsonDocument.Parse("{\"envRef\":\"MODE\"}").RootElement
        };

        Dictionary<string, string> env = new(StringComparer.OrdinalIgnoreCase)
        {
            ["MODE"] = "C"
        };

        _ = InputResolver.ResolveEffectiveInputs(parameters, defaults, null, overrides, env, out ValidationResult validation);
        Assert.False(validation.IsValid);
    }
}
