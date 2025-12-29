using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Contracts.Requests;
using PcTest.Engine.Resolution;
using Xunit;

namespace PcTest.Engine.Tests;

/// <summary>
/// Tests for input resolution per spec section 7.1.
/// </summary>
public class InputResolverTests
{
    private readonly InputResolver _resolver = new();

    [Fact]
    public void ResolveSuiteTriggeredInputs_DefaultsFirst_ThenSuiteInputs_ThenOverrides()
    {
        // Spec section 7.1: TestCase.defaults < Suite.node.inputs < RunRequest.nodeOverrides
        var testCase = CreateTestCase(new[]
        {
            new ParameterDefinition { Name = "Param1", Type = "int", Required = false, Default = JsonDocument.Parse("10").RootElement },
            new ParameterDefinition { Name = "Param2", Type = "int", Required = false, Default = JsonDocument.Parse("20").RootElement },
            new ParameterDefinition { Name = "Param3", Type = "int", Required = false, Default = JsonDocument.Parse("30").RootElement }
        });

        var suiteNode = new TestCaseNode
        {
            NodeId = "test-node",
            Ref = "Test",
            Inputs = new Dictionary<string, JsonElement>
            {
                ["Param1"] = JsonDocument.Parse("100").RootElement, // Override default
                ["Param2"] = JsonDocument.Parse("200").RootElement  // Override default
            }
        };

        var nodeOverrides = new Dictionary<string, NodeOverride>
        {
            ["test-node"] = new NodeOverride
            {
                Inputs = new Dictionary<string, JsonElement>
                {
                    ["Param1"] = JsonDocument.Parse("1000").RootElement // Override suite input
                }
            }
        };

        var result = _resolver.ResolveSuiteTriggeredInputs(testCase, suiteNode, nodeOverrides, new Dictionary<string, string>());

        Assert.True(result.Success);
        Assert.Equal(1000, result.EffectiveInputs["Param1"]); // Override wins
        Assert.Equal(200, result.EffectiveInputs["Param2"]);  // Suite input wins
        Assert.Equal(30, result.EffectiveInputs["Param3"]);   // Default wins
    }

    [Fact]
    public void ResolveStandaloneInputs_DefaultsFirst_ThenCaseInputs()
    {
        // Spec section 7.1: TestCase.defaults < RunRequest.caseInputs
        var testCase = CreateTestCase(new[]
        {
            new ParameterDefinition { Name = "Param1", Type = "int", Required = false, Default = JsonDocument.Parse("10").RootElement },
            new ParameterDefinition { Name = "Param2", Type = "int", Required = false, Default = JsonDocument.Parse("20").RootElement }
        });

        var caseInputs = new Dictionary<string, JsonElement>
        {
            ["Param1"] = JsonDocument.Parse("100").RootElement
        };

        var result = _resolver.ResolveStandaloneInputs(testCase, caseInputs, new Dictionary<string, string>());

        Assert.True(result.Success);
        Assert.Equal(100, result.EffectiveInputs["Param1"]); // Override wins
        Assert.Equal(20, result.EffectiveInputs["Param2"]);  // Default wins
    }

    [Fact]
    public void ResolveInputs_UnknownParameter_ReturnsError()
    {
        var testCase = CreateTestCase(new[]
        {
            new ParameterDefinition { Name = "KnownParam", Type = "int", Required = false }
        });

        var caseInputs = new Dictionary<string, JsonElement>
        {
            ["UnknownParam"] = JsonDocument.Parse("100").RootElement
        };

        var result = _resolver.ResolveStandaloneInputs(testCase, caseInputs, new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Code == ErrorCodes.ParameterUnknown);
    }

    [Fact]
    public void ResolveInputs_RequiredMissing_ReturnsError()
    {
        var testCase = CreateTestCase(new[]
        {
            new ParameterDefinition { Name = "RequiredParam", Type = "int", Required = true }
        });

        var result = _resolver.ResolveStandaloneInputs(testCase, null, new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Code == ErrorCodes.ParameterRequired);
    }

    [Fact]
    public void ResolveInputs_EnvRef_ResolvesFromEnvironment()
    {
        var testCase = CreateTestCase(new[]
        {
            new ParameterDefinition { Name = "Duration", Type = "int", Required = false }
        });

        var envRef = JsonDocument.Parse("{\"$env\": \"TEST_DURATION\"}").RootElement;
        var caseInputs = new Dictionary<string, JsonElement> { ["Duration"] = envRef };
        var env = new Dictionary<string, string> { ["TEST_DURATION"] = "42" };

        var result = _resolver.ResolveStandaloneInputs(testCase, caseInputs, env);

        Assert.True(result.Success);
        Assert.Equal(42, result.EffectiveInputs["Duration"]);
    }

    [Fact]
    public void ResolveInputs_EnvRef_WithDefault_UsesDefaultWhenMissing()
    {
        var testCase = CreateTestCase(new[]
        {
            new ParameterDefinition { Name = "Duration", Type = "int", Required = false }
        });

        var envRef = JsonDocument.Parse("{\"$env\": \"MISSING_VAR\", \"default\": 99}").RootElement;
        var caseInputs = new Dictionary<string, JsonElement> { ["Duration"] = envRef };

        var result = _resolver.ResolveStandaloneInputs(testCase, caseInputs, new Dictionary<string, string>());

        Assert.True(result.Success);
        Assert.Equal(99, result.EffectiveInputs["Duration"]);
    }

    [Fact]
    public void ResolveInputs_EnvRef_Required_FailsWhenMissing()
    {
        var testCase = CreateTestCase(new[]
        {
            new ParameterDefinition { Name = "Duration", Type = "int", Required = false }
        });

        var envRef = JsonDocument.Parse("{\"$env\": \"MISSING_VAR\", \"required\": true}").RootElement;
        var caseInputs = new Dictionary<string, JsonElement> { ["Duration"] = envRef };

        var result = _resolver.ResolveStandaloneInputs(testCase, caseInputs, new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Code == ErrorCodes.EnvRefResolveFailed);
    }

    [Fact]
    public void ResolveInputs_EnvRef_Secret_MarkedInMetadata()
    {
        var testCase = CreateTestCase(new[]
        {
            new ParameterDefinition { Name = "Password", Type = "string", Required = false }
        });

        var envRef = JsonDocument.Parse("{\"$env\": \"SECRET_PASSWORD\", \"secret\": true}").RootElement;
        var caseInputs = new Dictionary<string, JsonElement> { ["Password"] = envRef };
        var env = new Dictionary<string, string> { ["SECRET_PASSWORD"] = "secret123" };

        var result = _resolver.ResolveStandaloneInputs(testCase, caseInputs, env);

        Assert.True(result.Success);
        Assert.True(result.SecretInputs["Password"]);
        Assert.Equal("secret123", result.EffectiveInputs["Password"]); // Real value for execution
    }

    [Fact]
    public void ResolveInputs_EnvRef_TypeConversion_Boolean()
    {
        var testCase = CreateTestCase(new[]
        {
            new ParameterDefinition { Name = "Enabled", Type = "boolean", Required = false }
        });

        var envRef = JsonDocument.Parse("{\"$env\": \"IS_ENABLED\"}").RootElement;
        var caseInputs = new Dictionary<string, JsonElement> { ["Enabled"] = envRef };
        var env = new Dictionary<string, string> { ["IS_ENABLED"] = "true" };

        var result = _resolver.ResolveStandaloneInputs(testCase, caseInputs, env);

        Assert.True(result.Success);
        Assert.Equal(true, result.EffectiveInputs["Enabled"]);
    }

    [Fact]
    public void ResolveInputs_EnvRef_EnumValidation_Fails()
    {
        var testCase = CreateTestCase(new[]
        {
            new ParameterDefinition
            {
                Name = "Mode",
                Type = "enum",
                Required = false,
                EnumValues = new List<string> { "A", "B", "C" }
            }
        });

        var envRef = JsonDocument.Parse("{\"$env\": \"TEST_MODE\"}").RootElement;
        var caseInputs = new Dictionary<string, JsonElement> { ["Mode"] = envRef };
        var env = new Dictionary<string, string> { ["TEST_MODE"] = "D" }; // Invalid

        var result = _resolver.ResolveStandaloneInputs(testCase, caseInputs, env);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Code == ErrorCodes.EnvRefResolveFailed);
    }
    [Fact]
    public void ResolveInputs_BooleanParameter_ReturnsNativeBool()
    {
        var testCase = CreateTestCase(new[]
        {
            new ParameterDefinition { Name = "ShouldPass", Type = "boolean", Required = false }
        });

        var caseInputs = new Dictionary<string, JsonElement>
        {
            ["ShouldPass"] = JsonDocument.Parse("true").RootElement
        };

        var result = _resolver.ResolveStandaloneInputs(testCase, caseInputs, new Dictionary<string, string>());

        Assert.True(result.Success);
        var value = result.EffectiveInputs["ShouldPass"];
        Assert.NotNull(value);
        Assert.IsType<bool>(value);  // Must be native bool, not JsonElement
        Assert.True((bool)value);
    }

    private static TestCaseManifest CreateTestCase(ParameterDefinition[] parameters)
    {
        return new TestCaseManifest
        {
            Id = "TestCase",
            Version = "1.0.0",
            Name = "Test",
            Category = "Test",
            Parameters = parameters.ToList()
        };
    }
}
