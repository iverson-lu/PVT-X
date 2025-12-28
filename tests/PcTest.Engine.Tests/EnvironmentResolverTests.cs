using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Contracts.Requests;
using PcTest.Engine.Resolution;
using Xunit;

namespace PcTest.Engine.Tests;

/// <summary>
/// Tests for EnvironmentResolver per spec section 7.3.
/// </summary>
public class EnvironmentResolverTests
{
    private readonly EnvironmentResolver _resolver = new();

    [Fact]
    public void ComputeStandaloneEnvironment_NoOverrides_InheritsOsEnv()
    {
        // Set up a system env var temporarily
        const string testKey = "TEST_STANDALONE_VAR";
        const string testValue = "test-value";
        Environment.SetEnvironmentVariable(testKey, testValue);

        try
        {
            var result = _resolver.ComputeStandaloneEnvironment(null);

            Assert.True(result.ContainsKey(testKey));
            Assert.Equal(testValue, result[testKey]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(testKey, null);
        }
    }

    [Fact]
    public void ComputeStandaloneEnvironment_WithOverrides_OverridesOsEnv()
    {
        const string testKey = "TEST_OVERRIDE_VAR";
        const string osValue = "os-value";
        const string overrideValue = "override-value";
        Environment.SetEnvironmentVariable(testKey, osValue);

        try
        {
            var overrides = new EnvironmentOverrides
            {
                Env = new Dictionary<string, string> { [testKey] = overrideValue }
            };

            var result = _resolver.ComputeStandaloneEnvironment(overrides);

            Assert.Equal(overrideValue, result[testKey]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(testKey, null);
        }
    }

    [Fact]
    public void ComputeSuiteEnvironment_SuiteEnvOverridesOs()
    {
        const string testKey = "TEST_SUITE_VAR";
        const string osValue = "os-value";
        const string suiteValue = "suite-value";
        Environment.SetEnvironmentVariable(testKey, osValue);

        try
        {
            var suite = new TestSuiteManifest
            {
                Id = "TestSuite",
                Version = "1.0.0",
                Name = "Test Suite",
                Environment = new SuiteEnvironment
                {
                    Env = new Dictionary<string, string> { [testKey] = suiteValue }
                }
            };

            var result = _resolver.ComputeSuiteEnvironment(suite, null);

            Assert.Equal(suiteValue, result[testKey]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(testKey, null);
        }
    }

    [Fact]
    public void ComputeSuiteEnvironment_RunRequestOverridesSuite()
    {
        var suite = new TestSuiteManifest
        {
            Id = "TestSuite",
            Version = "1.0.0",
            Name = "Test Suite",
            Environment = new SuiteEnvironment
            {
                Env = new Dictionary<string, string> { ["SHARED_VAR"] = "suite-value" }
            }
        };
        var overrides = new EnvironmentOverrides
        {
            Env = new Dictionary<string, string> { ["SHARED_VAR"] = "request-value" }
        };

        var result = _resolver.ComputeSuiteEnvironment(suite, overrides);

        Assert.Equal("request-value", result["SHARED_VAR"]);
    }

    [Fact]
    public void ComputePlanEnvironment_Priority_RequestOverridesPlanOverridesSuiteOverridesOs()
    {
        // Spec section 7.3: RunRequest.env > Plan.env > Suite.env > OS environment
        var suite = new TestSuiteManifest
        {
            Id = "TestSuite",
            Version = "1.0.0",
            Name = "Test Suite",
            Environment = new SuiteEnvironment
            {
                Env = new Dictionary<string, string>
                {
                    ["VAR_A"] = "suite-a",
                    ["VAR_B"] = "suite-b",
                    ["VAR_C"] = "suite-c"
                }
            }
        };
        var plan = new TestPlanManifest
        {
            Id = "TestPlan",
            Version = "1.0.0",
            Name = "Test Plan",
            Environment = new PlanEnvironment
            {
                Env = new Dictionary<string, string>
                {
                    ["VAR_A"] = "plan-a", // Overrides suite
                    ["VAR_B"] = "plan-b"  // Overrides suite
                }
            }
        };
        var overrides = new EnvironmentOverrides
        {
            Env = new Dictionary<string, string>
            {
                ["VAR_A"] = "request-a" // Overrides plan
            }
        };

        var result = _resolver.ComputePlanEnvironment(plan, suite, overrides);

        Assert.Equal("request-a", result["VAR_A"]); // Request wins
        Assert.Equal("plan-b", result["VAR_B"]);     // Plan wins over suite
        Assert.Equal("suite-c", result["VAR_C"]);    // Suite wins (no override)
    }

    [Fact]
    public void ComputePlanEnvironment_NullEnvironments_Works()
    {
        var suite = new TestSuiteManifest
        {
            Id = "TestSuite",
            Version = "1.0.0",
            Name = "Test Suite"
            // No environment
        };
        var plan = new TestPlanManifest
        {
            Id = "TestPlan",
            Version = "1.0.0",
            Name = "Test Plan"
            // No environment
        };

        var result = _resolver.ComputePlanEnvironment(plan, suite, null);

        // Should still have OS environment vars
        Assert.True(result.Count > 0);
    }
}
