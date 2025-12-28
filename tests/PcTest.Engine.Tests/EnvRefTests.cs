using System.Text.Json;
using PcTest.Contracts;
using PcTest.Engine;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class EnvRefTests
{
    [Fact]
    public void EnvRefConvertsTypesAndValidatesEnum()
    {
        var resolver = new InputResolver();
        var manifest = new TestCaseManifest
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
                    Type = "enum",
                    Required = true,
                    EnumValues = new[] { "A", "B" }
                },
                new ParameterDefinition
                {
                    Name = "Count",
                    Type = "int",
                    Required = true
                }
            }
        };

        var inputs = new Dictionary<string, InputValue>
        {
            ["Mode"] = new InputValue(new EnvRef
            {
                Env = "MODE",
                Required = true,
                Secret = false
            }),
            ["Count"] = new InputValue(new EnvRef
            {
                Env = "COUNT",
                Required = true,
                Secret = false
            })
        };

        var environment = new Dictionary<string, string>
        {
            ["MODE"] = "A",
            ["COUNT"] = "42"
        };

        var resolved = resolver.Resolve(manifest, inputs, environment, null);
        Assert.Equal(2, resolved.Values.Count);
        Assert.Equal(42, resolved.Values["Count"]);
        Assert.Equal("A", resolved.Values["Mode"]);
    }

    [Fact]
    public void EnvRefRejectsInvalidEnum()
    {
        var resolver = new InputResolver();
        var manifest = new TestCaseManifest
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
                    Type = "enum",
                    Required = true,
                    EnumValues = new[] { "A", "B" }
                }
            }
        };

        var inputs = new Dictionary<string, InputValue>
        {
            ["Mode"] = new InputValue(new EnvRef
            {
                Env = "MODE",
                Required = true,
                Secret = false
            })
        };

        var environment = new Dictionary<string, string>
        {
            ["MODE"] = "C"
        };

        Assert.Throws<ValidationException>(() => resolver.Resolve(manifest, inputs, environment, null));
    }

    [Fact]
    public void EnvRefSecretAddsRedactionMetadata()
    {
        var resolver = new InputResolver();
        var manifest = new TestCaseManifest
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
                    Name = "Token",
                    Type = "string",
                    Required = true
                }
            }
        };

        var inputs = new Dictionary<string, InputValue>
        {
            ["Token"] = new InputValue(new EnvRef
            {
                Env = "TOKEN",
                Required = true,
                Secret = true
            })
        };

        var environment = new Dictionary<string, string>
        {
            ["TOKEN"] = "secret-value"
        };

        var resolved = resolver.Resolve(manifest, inputs, environment, null);
        Assert.True(resolved.SecretInputs.ContainsKey("Token"));
    }
}
