using System.Text.Json.Nodes;
using PcTest.Contracts;
using PcTest.Engine;
using Xunit;

namespace PcTest.Engine.Tests;

public class InputResolutionTests
{
    [Fact]
    public void Inputs_UsePriorityOrder()
    {
        var parameters = new Dictionary<string, ParameterDefinition>(StringComparer.Ordinal)
        {
            ["Duration"] = new() { Name = "Duration", Type = "int", Required = true, Default = JsonValue.Create(1) }
        };
        var defaults = new Dictionary<string, JsonNode?> { ["Duration"] = JsonValue.Create(1) };
        var suiteInputs = new Dictionary<string, JsonNode?> { ["Duration"] = JsonValue.Create(2) };
        var overrides = new Dictionary<string, JsonNode?> { ["Duration"] = JsonValue.Create(3) };
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var result = InputResolver.Resolve(parameters, defaults, overrides, environment, "node-a");
        Assert.Empty(result.Errors);
        Assert.Equal(3, result.EffectiveInputs["Duration"]);
    }

    [Fact]
    public void EnvRef_ResolvesAndValidatesEnum()
    {
        var parameters = new Dictionary<string, ParameterDefinition>(StringComparer.Ordinal)
        {
            ["Mode"] = new() { Name = "Mode", Type = "enum", Required = true, EnumValues = new[] { "A", "B" } }
        };
        var defaults = new Dictionary<string, JsonNode?>();
        var overrides = new Dictionary<string, JsonNode?>
        {
            ["Mode"] = JsonNode.Parse("{\"$env\":\"MODE\",\"required\":true}")
        };
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["MODE"] = "B" };

        var result = InputResolver.Resolve(parameters, defaults, overrides, environment, "node-a");
        Assert.Empty(result.Errors);
        Assert.Equal("B", result.EffectiveInputs["Mode"]);
    }

    [Fact]
    public void EnvRef_RejectsInvalidEnum()
    {
        var parameters = new Dictionary<string, ParameterDefinition>(StringComparer.Ordinal)
        {
            ["Mode"] = new() { Name = "Mode", Type = "enum", Required = true, EnumValues = new[] { "A" } }
        };
        var overrides = new Dictionary<string, JsonNode?>
        {
            ["Mode"] = JsonNode.Parse("{\"$env\":\"MODE\"}")
        };
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["MODE"] = "B" };

        var result = InputResolver.Resolve(parameters, new Dictionary<string, JsonNode?>(), overrides, environment, "node-a");
        Assert.Contains(result.Errors, e => e.Code == ErrorCodes.EnvRefResolveFailed);
    }
}
