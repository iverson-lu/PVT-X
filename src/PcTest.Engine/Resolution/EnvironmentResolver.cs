using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Contracts.Requests;
using PcTest.Contracts.Validation;

namespace PcTest.Engine.Resolution;

/// <summary>
/// Computes Effective Environment per spec section 7.3.
/// 
/// Environment Variable Priority Order (low to high):
/// 1. OS Environment (baseline)
/// 2. Plan Environment (plan.manifest.json → environment.env)
/// 3. Suite Environment (suite.manifest.json → environment.env)
/// 4. RunRequest / CLI EnvironmentOverrides (highest priority)
/// 
/// Later sources override earlier sources for the same key.
/// </summary>
public sealed class EnvironmentResolver
{
    /// <summary>
    /// Computes effective environment for a Plan run per spec section 7.3.
    /// Order: RunRequest.env > Plan.env > Suite.env > OS environment
    /// </summary>
    public Dictionary<string, string> ComputePlanEnvironment(
        TestPlanManifest plan,
        TestSuiteManifest suite,
        EnvironmentOverrides? runRequestOverrides)
    {
        var result = GetOsEnvironment();

        // Apply Suite environment
        if (suite.Environment?.Env is not null)
        {
            foreach (var (key, value) in suite.Environment.Env)
            {
                result[key] = value;
            }
        }

        // Apply Plan environment
        if (plan.Environment?.Env is not null)
        {
            foreach (var (key, value) in plan.Environment.Env)
            {
                result[key] = value;
            }
        }

        // Apply RunRequest overrides
        if (runRequestOverrides?.Env is not null)
        {
            foreach (var (key, value) in runRequestOverrides.Env)
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Computes effective environment for a Suite run per spec section 7.3.
    /// Order: RunRequest.env > Suite.env > OS environment
    /// </summary>
    public Dictionary<string, string> ComputeSuiteEnvironment(
        TestSuiteManifest suite,
        EnvironmentOverrides? runRequestOverrides)
    {
        var result = GetOsEnvironment();

        // Apply Suite environment
        if (suite.Environment?.Env is not null)
        {
            foreach (var (key, value) in suite.Environment.Env)
            {
                result[key] = value;
            }
        }

        // Apply RunRequest overrides
        if (runRequestOverrides?.Env is not null)
        {
            foreach (var (key, value) in runRequestOverrides.Env)
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Computes effective environment for a Suite run within a Plan context per spec section 7.3.
    /// Order: RunRequest.env > Suite.env > Plan.env > OS environment
    /// </summary>
    public Dictionary<string, string> ComputeSuiteEnvironment(
        TestPlanManifest? plan,
        TestSuiteManifest suite,
        EnvironmentOverrides? runRequestOverrides)
    {
        var result = GetOsEnvironment();

        // Apply Plan environment
        if (plan?.Environment?.Env is not null)
        {
            foreach (var (key, value) in plan.Environment.Env)
            {
                result[key] = value;
            }
        }

        // Apply Suite environment
        if (suite.Environment?.Env is not null)
        {
            foreach (var (key, value) in suite.Environment.Env)
            {
                result[key] = value;
            }
        }

        // Apply RunRequest overrides
        if (runRequestOverrides?.Env is not null)
        {
            foreach (var (key, value) in runRequestOverrides.Env)
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Computes effective environment for a standalone TestCase run per spec section 7.3.
    /// Order: RunRequest.env > OS environment
    /// </summary>
    public Dictionary<string, string> ComputeStandaloneEnvironment(
        EnvironmentOverrides? runRequestOverrides)
    {
        var result = GetOsEnvironment();

        // Apply RunRequest overrides
        if (runRequestOverrides?.Env is not null)
        {
            foreach (var (key, value) in runRequestOverrides.Env)
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Validates environment keys per spec section 7.3.
    /// Empty keys MUST fail validation.
    /// </summary>
    public ValidationResult ValidateEnvironment(Dictionary<string, string>? env, string location)
    {
        var result = new ValidationResult();

        if (env is null)
            return result;

        foreach (var key in env.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                result.AddError(new ValidationError
                {
                    Code = ErrorCodes.EnvironmentKeyEmpty,
                    Message = "Environment key cannot be empty or whitespace",
                    Location = location
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Validates Plan environment per spec section 6.4.
    /// Plan environment MUST be env-only.
    /// </summary>
    public ValidationResult ValidatePlanEnvironment(PlanEnvironment? env, string location)
    {
        var result = new ValidationResult();

        if (env is null)
            return result;

        // Check for invalid keys (anything other than env)
        if (env.ExtensionData is not null && env.ExtensionData.Count > 0)
        {
            foreach (var key in env.ExtensionData.Keys)
            {
                result.AddError(new ValidationError
                {
                    Code = ErrorCodes.PlanEnvironmentInvalidKey,
                    Message = $"Plan environment must be env-only; invalid key: '{key}'",
                    Location = location,
                    Data = new Dictionary<string, object?> { ["key"] = key }
                });
            }
        }

        // Validate env keys
        if (env.Env is not null)
        {
            result.Merge(ValidateEnvironment(env.Env, location));
        }

        return result;
    }

    private static Dictionary<string, string> GetOsEnvironment()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Environment.GetEnvironmentVariables())
        {
            if (entry is System.Collections.DictionaryEntry de &&
                de.Key is string key &&
                de.Value is string value)
            {
                result[key] = value;
            }
        }
        return result;
    }
}
