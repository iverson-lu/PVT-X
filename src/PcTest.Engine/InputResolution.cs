using System.Text.Json.Nodes;
using PcTest.Contracts;

namespace PcTest.Engine;

public sealed class InputResolutionResult
{
    public Dictionary<string, object?> EffectiveInputs { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, object?> InputTemplates { get; } = new(StringComparer.Ordinal);
    public HashSet<string> SecretInputs { get; } = new(StringComparer.Ordinal);
    public List<ValidationError> Errors { get; } = new();
    public List<ValidationError> Warnings { get; } = new();
}

public static class InputResolver
{
    public static InputResolutionResult Resolve(
        IReadOnlyDictionary<string, ParameterDefinition> parameters,
        IReadOnlyDictionary<string, JsonNode?> defaults,
        IReadOnlyDictionary<string, JsonNode?> overrides,
        IReadOnlyDictionary<string, string> effectiveEnvironment,
        string? nodeId)
    {
        var result = new InputResolutionResult();
        var merged = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var kvp in defaults)
        {
            merged[kvp.Key] = kvp.Value;
        }
        foreach (var kvp in overrides)
        {
            merged[kvp.Key] = kvp.Value;
        }

        foreach (var kvp in merged)
        {
            if (!parameters.ContainsKey(kvp.Key))
            {
                result.Errors.Add(new ValidationError(ErrorCodes.InputsUnknown, "Unknown input name.", new { name = kvp.Key, nodeId }));
                continue;
            }

            var parameter = parameters[kvp.Key];
            result.InputTemplates[kvp.Key] = kvp.Value;
            if (Validation.IsEnvRef(kvp.Value, out var envRef))
            {
                var envValue = ResolveEnvValue(envRef, effectiveEnvironment, parameter.Type, parameter.EnumValues, result.Errors, nodeId, kvp.Key);
                if (envValue.success && envValue.hasValue)
                {
                    result.EffectiveInputs[kvp.Key] = envValue.value;
                    if (envRef.Secret)
                    {
                        result.SecretInputs.Add(kvp.Key);
                    }
                }
            }
            else
            {
                try
                {
                    var value = ConvertLiteral(parameter.Type, parameter.EnumValues, kvp.Value);
                    result.EffectiveInputs[kvp.Key] = value;
                }
                catch (Exception ex)
                {
                    result.Errors.Add(new ValidationError(ErrorCodes.EnvRefResolveFailed, ex.Message, new { name = kvp.Key, nodeId }));
                }
            }
        }

        foreach (var parameter in parameters.Values)
        {
            if (parameter.Required && !result.EffectiveInputs.ContainsKey(parameter.Name))
            {
                result.Errors.Add(new ValidationError(ErrorCodes.InputsMissingRequired, "Missing required input.", new { name = parameter.Name, nodeId }));
            }
        }

        return result;
    }

    private static (bool success, bool hasValue, object? value) ResolveEnvValue(
        EnvRef envRef,
        IReadOnlyDictionary<string, string> effectiveEnvironment,
        string type,
        string[]? enumValues,
        List<ValidationError> errors,
        string? nodeId,
        string name)
    {
        if (!effectiveEnvironment.TryGetValue(envRef.Env, out var rawValue) || rawValue == "")
        {
            if (envRef.Default is not null)
            {
                try
                {
                    var literal = ConvertLiteral(type, enumValues, envRef.Default);
                    return (true, true, literal);
                }
                catch (Exception ex)
                {
                    errors.Add(new ValidationError(ErrorCodes.EnvRefResolveFailed, ex.Message, new { name, nodeId }));
                    return (false, false, null);
                }
            }
            if (envRef.Required)
            {
                errors.Add(new ValidationError(ErrorCodes.EnvRefResolveFailed, "Required env is missing.", new { name, nodeId }));
                return (false, false, null);
            }
            return (true, false, null);
        }

        try
        {
            var value = ConvertEnv(type, enumValues, rawValue);
            return (true, true, value);
        }
        catch (Exception ex)
        {
            errors.Add(new ValidationError(ErrorCodes.EnvRefResolveFailed, ex.Message, new { name, nodeId }));
            return (false, false, null);
        }
    }

    private static object? ConvertEnv(string type, string[]? enumValues, string value)
    {
        var result = Validation.ConvertEnvValue(type, value);
        ValidateEnum(type, enumValues, result);
        return result;
    }

    private static object? ConvertLiteral(string type, string[]? enumValues, JsonNode? value)
    {
        var result = Validation.ConvertValue(type, value);
        ValidateEnum(type, enumValues, result);
        return result;
    }

    private static void ValidateEnum(string type, string[]? enumValues, object? value)
    {
        if (enumValues is null || enumValues.Length == 0)
        {
            return;
        }
        if (type == "enum")
        {
            if (value is not string s || !enumValues.Contains(s, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("Enum value is not in enumValues.");
            }
        }
        if (type == "enum[]")
        {
            if (value is not string[] values)
            {
                throw new InvalidOperationException("Enum array must be string array.");
            }
            foreach (var item in values)
            {
                if (!enumValues.Contains(item, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException("Enum value is not in enumValues.");
                }
            }
        }
    }
}
