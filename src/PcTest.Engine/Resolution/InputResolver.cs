using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Contracts.Requests;
using PcTest.Contracts.Validation;
using PcTest.Engine.Discovery;

namespace PcTest.Engine.Resolution;

/// <summary>
/// Resolves inputs and environment per spec sections 7.1, 7.3, 7.4.
/// </summary>
public sealed class InputResolver
{
    /// <summary>
    /// Metadata about a resolved input.
    /// </summary>
    public sealed class ResolvedInput
    {
        public string Name { get; init; } = string.Empty;
        public object? Value { get; init; }
        public bool IsSecret { get; init; }
        public JsonElement? OriginalTemplate { get; init; }
    }

    /// <summary>
    /// Result of input resolution.
    /// </summary>
    public sealed class ResolutionResult
    {
        public Dictionary<string, object?> EffectiveInputs { get; } = new();
        public Dictionary<string, bool> SecretInputs { get; } = new();
        public Dictionary<string, JsonElement> InputTemplates { get; } = new();
        public List<ValidationError> Errors { get; } = new();
        public bool Success => Errors.Count == 0;
    }

    /// <summary>
    /// Computes effective inputs for a suite-triggered Test Case run per spec section 7.1.
    /// Order: TestCase.defaults &lt; Suite.node.inputs &lt; RunRequest.nodeOverrides[nodeId].inputs
    /// </summary>
    public ResolutionResult ResolveSuiteTriggeredInputs(
        TestCaseManifest testCase,
        TestCaseNode suiteNode,
        Dictionary<string, NodeOverride>? nodeOverrides,
        Dictionary<string, string> effectiveEnvironment)
    {
        var result = new ResolutionResult();

        // Start with TestCase defaults
        var merged = new Dictionary<string, JsonElement>();
        if (testCase.Parameters is not null)
        {
            foreach (var param in testCase.Parameters)
            {
                if (param.Default.HasValue)
                {
                    merged[param.Name] = param.Default.Value;
                }
            }
        }

        // Apply Suite node inputs
        if (suiteNode.Inputs is not null)
        {
            foreach (var (name, value) in suiteNode.Inputs)
            {
                merged[name] = value;
            }
        }

        // Apply RunRequest nodeOverrides
        if (nodeOverrides is not null &&
            nodeOverrides.TryGetValue(suiteNode.NodeId, out var nodeOverride) &&
            nodeOverride.Inputs is not null)
        {
            foreach (var (name, value) in nodeOverride.Inputs)
            {
                merged[name] = value;
            }
        }

        // Store templates before EnvRef resolution
        foreach (var (name, value) in merged)
        {
            result.InputTemplates[name] = value;
        }

        // Resolve EnvRef and convert types
        ResolveAndConvert(result, testCase, merged, effectiveEnvironment);

        return result;
    }

    /// <summary>
    /// Computes effective inputs for a standalone Test Case run per spec section 7.1.
    /// Order: TestCase.defaults &lt; RunRequest.caseInputs
    /// </summary>
    public ResolutionResult ResolveStandaloneInputs(
        TestCaseManifest testCase,
        Dictionary<string, JsonElement>? caseInputs,
        Dictionary<string, string> effectiveEnvironment)
    {
        var result = new ResolutionResult();

        // Start with TestCase defaults
        var merged = new Dictionary<string, JsonElement>();
        if (testCase.Parameters is not null)
        {
            foreach (var param in testCase.Parameters)
            {
                if (param.Default.HasValue)
                {
                    merged[param.Name] = param.Default.Value;
                }
            }
        }

        // Apply RunRequest caseInputs
        if (caseInputs is not null)
        {
            foreach (var (name, value) in caseInputs)
            {
                merged[name] = value;
            }
        }

        // Store templates
        foreach (var (name, value) in merged)
        {
            result.InputTemplates[name] = value;
        }

        // Resolve EnvRef and convert types
        ResolveAndConvert(result, testCase, merged, effectiveEnvironment);

        return result;
    }

    private void ResolveAndConvert(
        ResolutionResult result,
        TestCaseManifest testCase,
        Dictionary<string, JsonElement> merged,
        Dictionary<string, string> effectiveEnvironment)
    {
        var paramMap = testCase.Parameters?.ToDictionary(p => p.Name) ?? new();

        foreach (var (name, jsonValue) in merged)
        {
            // Check if parameter exists in TestCase
            if (!paramMap.TryGetValue(name, out var paramDef))
            {
                result.Errors.Add(new ValidationError
                {
                    Code = ErrorCodes.ParameterUnknown,
                    Message = $"Unknown parameter '{name}' for TestCase '{testCase.Id}'",
                    Data = new Dictionary<string, object?> { ["parameter"] = name, ["testId"] = testCase.Id }
                });
                continue;
            }

            // Check if it's an EnvRef
            if (EnvRef.IsEnvRef(jsonValue))
            {
                var envRef = EnvRef.FromJsonElement(jsonValue);
                if (envRef is not null)
                {
                    var (resolved, error) = ResolveEnvRef(envRef, paramDef, effectiveEnvironment, name);
                    if (error is not null)
                    {
                        result.Errors.Add(error);
                    }
                    else
                    {
                        result.EffectiveInputs[name] = resolved;
                        result.SecretInputs[name] = envRef.Secret;
                    }
                    continue;
                }
            }

            // Convert JSON literal to typed value
            var (success, converted, convertError) = TypeConverter.ConvertJsonElement(
                jsonValue, paramDef.Type, paramDef.EnumValues);

            if (!success)
            {
                result.Errors.Add(new ValidationError
                {
                    Code = ErrorCodes.ParameterTypeInvalid,
                    Message = $"Parameter '{name}' conversion failed: {convertError}",
                    Data = new Dictionary<string, object?> { ["parameter"] = name, ["type"] = paramDef.Type }
                });
            }
            else
            {
                result.EffectiveInputs[name] = converted;
                result.SecretInputs[name] = false;
            }
        }

        // Check required parameters
        if (testCase.Parameters is not null)
        {
            foreach (var param in testCase.Parameters.Where(p => p.Required))
            {
                if (!result.EffectiveInputs.ContainsKey(param.Name))
                {
                    result.Errors.Add(new ValidationError
                    {
                        Code = ErrorCodes.ParameterRequired,
                        Message = $"Required parameter '{param.Name}' is missing",
                        Data = new Dictionary<string, object?> { ["parameter"] = param.Name }
                    });
                }
            }
        }
    }

    /// <summary>
    /// Resolves an EnvRef per spec section 7.4.
    /// </summary>
    private (object? Value, ValidationError? Error) ResolveEnvRef(
        EnvRef envRef,
        ParameterDefinition paramDef,
        Dictionary<string, string> effectiveEnvironment,
        string paramName)
    {
        if (string.IsNullOrEmpty(envRef.EnvVarName))
        {
            return (null, new ValidationError
            {
                Code = ErrorCodes.EnvRefResolveFailed,
                Message = $"EnvRef for parameter '{paramName}' has empty $env",
                Data = new Dictionary<string, object?> { ["parameter"] = paramName }
            });
        }

        // Look up in effective environment
        effectiveEnvironment.TryGetValue(envRef.EnvVarName, out var envValue);

        // Check if empty (null or empty string per spec)
        var isEmpty = string.IsNullOrEmpty(envValue);

        if (isEmpty)
        {
            // Use default if available
            if (envRef.Default.HasValue)
            {
                var (success, converted, error) = TypeConverter.ConvertJsonElement(
                    envRef.Default.Value, paramDef.Type, paramDef.EnumValues);
                if (!success)
                {
                    return (null, new ValidationError
                    {
                        Code = ErrorCodes.EnvRefResolveFailed,
                        Message = $"EnvRef default for '{paramName}' conversion failed: {error}",
                        Data = new Dictionary<string, object?> { ["parameter"] = paramName }
                    });
                }
                return (converted, null);
            }

            // Check if required
            if (envRef.Required)
            {
                return (null, new ValidationError
                {
                    Code = ErrorCodes.EnvRefResolveFailed,
                    Message = $"Required EnvRef '{envRef.EnvVarName}' for parameter '{paramName}' is missing",
                    Data = new Dictionary<string, object?>
                    {
                        ["parameter"] = paramName,
                        ["envVar"] = envRef.EnvVarName
                    }
                });
            }

            // Not required and no default - return null (parameter will be omitted)
            return (null, null);
        }

        // At this point, envValue is guaranteed to be non-null and non-empty
        // because isEmpty was false above
        var actualValue = envValue ?? string.Empty;

        // Convert string value to target type
        var (convertSuccess, convertedValue, convertError) = TypeConverter.ConvertString(
            actualValue, paramDef.Type, paramDef.EnumValues);

        if (!convertSuccess)
        {
            return (null, new ValidationError
            {
                Code = ErrorCodes.EnvRefResolveFailed,
                Message = $"EnvRef '{envRef.EnvVarName}' for parameter '{paramName}' conversion failed: {convertError}",
                Data = new Dictionary<string, object?>
                {
                    ["parameter"] = paramName,
                    ["envVar"] = envRef.EnvVarName,
                    ["value"] = envValue,
                    ["targetType"] = paramDef.Type
                }
            });
        }

        return (convertedValue, null);
    }
}
