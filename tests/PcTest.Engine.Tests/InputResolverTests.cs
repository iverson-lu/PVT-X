using System.Text.Json;
using PcTest.Contracts.Models;
using PcTest.Engine;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class InputResolverTests
{
    [Fact]
    public void EnvRef_ResolvesAndRedactsSecrets()
    {
        var manifest = new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "Case",
            Name = "Case",
            Category = "Cat",
            Version = "1.0.0",
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "Token", Type = "string", Required = true }
            }
        };

        var envRefJson = JsonDocument.Parse("{\"$env\":\"TOKEN\",\"secret\":true,\"required\":true}").RootElement.Clone();
        var inputs = new Dictionary<string, JsonElement> { ["Token"] = envRefJson };
        var environment = new Dictionary<string, string> { ["TOKEN"] = "super-secret" };

        var resolved = InputResolver.Resolve(manifest, inputs, environment, null);
        Assert.Equal("super-secret", resolved.Values["Token"]);
        Assert.Equal("***", resolved.RedactedValues["Token"]);
        Assert.Contains("Token", resolved.SecretInputs);
    }

    [Fact]
    public void EnvRef_ValidatesEnumConversion()
    {
        var manifest = new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "Case",
            Name = "Case",
            Category = "Cat",
            Version = "1.0.0",
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "Mode", Type = "enum", Required = true, EnumValues = new List<string> { "A", "B" } }
            }
        };

        var envRefJson = JsonDocument.Parse("{\"$env\":\"MODE\",\"required\":true}").RootElement.Clone();
        var inputs = new Dictionary<string, JsonElement> { ["Mode"] = envRefJson };
        var environment = new Dictionary<string, string> { ["MODE"] = "C" };

        Assert.Throws<EngineException>(() => InputResolver.Resolve(manifest, inputs, environment, null));
    }

    [Fact]
    public void EnvRef_ParsesPrimitiveArrays()
    {
        var manifest = new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = "Case",
            Name = "Case",
            Category = "Cat",
            Version = "1.0.0",
            Parameters = new List<ParameterDefinition>
            {
                new() { Name = "Flags", Type = "boolean[]", Required = true }
            }
        };

        var envRefJson = JsonDocument.Parse("{\"$env\":\"FLAGS\",\"required\":true}").RootElement.Clone();
        var inputs = new Dictionary<string, JsonElement> { ["Flags"] = envRefJson };
        var environment = new Dictionary<string, string> { ["FLAGS"] = "[true,false,true]" };

        var resolved = InputResolver.Resolve(manifest, inputs, environment, null);
        var values = Assert.IsType<bool[]>(resolved.Values["Flags"]);
        Assert.Equal(new[] { true, false, true }, values);
    }
}
