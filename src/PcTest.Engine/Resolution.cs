using System.Globalization;
using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine;

public sealed record InputResolutionResult(
    IReadOnlyDictionary<string, object?> EffectiveInputs,
    IReadOnlyDictionary<string, object?> RedactedInputs,
    IReadOnlyCollection<string> SecretInputs,
    IReadOnlyDictionary<string, JsonElement> InputTemplates);

public static class Resolution
{
    public static IReadOnlyDictionary<string, string> ComputeEffectiveEnvironment(
        IReadOnlyDictionary<string, string>? osEnv,
        IReadOnlyDictionary<string, string>? suiteEnv,
        IReadOnlyDictionary<string, string>? planEnv,
        IReadOnlyDictionary<string, string>? overrides)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Apply(IReadOnlyDictionary<string, string>? env)
        {
            if (env is null)
            {
                return;
            }

            foreach (var (key, value) in env)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new ValidationException("Environment.Key.Invalid", new Dictionary<string, object>
                    {
                        ["key"] = key
                    });
                }

                result[key] = value;
            }
        }

        Apply(osEnv);
        Apply(suiteEnv);
        Apply(planEnv);
        Apply(overrides);

        return result;
    }

    public static InputResolutionResult ResolveInputs(
        TestCaseManifest manifest,
        IReadOnlyDictionary<string, JsonElement>? baseInputs,
        IReadOnlyDictionary<string, JsonElement>? overrideInputs,
        IReadOnlyDictionary<string, string> effectiveEnvironment,
        string? nodeId)
    {
        var parameters = manifest.Parameters ?? Array.Empty<ParameterDefinition>();
        var parameterMap = parameters.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var effective = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var redacted = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var secretInputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var templates = new SortedDictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in parameters)
        {
            if (parameter.Default is { } defaultElement)
            {
                var value = ParameterTypes.ConvertLiteral(parameter.Type, defaultElement.Value);
                ValidateEnum(parameter, value);
                effective[parameter.Name] = value;
                redacted[parameter.Name] = value;
            }
        }

        ApplyInputs(parameterMap, baseInputs, effectiveEnvironment, nodeId, effective, redacted, secretInputs, templates);
        ApplyInputs(parameterMap, overrideInputs, effectiveEnvironment, nodeId, effective, redacted, secretInputs, templates);

        foreach (var parameter in parameters)
        {
            if (parameter.Required && !effective.ContainsKey(parameter.Name))
            {
                throw new ValidationException("Inputs.Missing.Required", new Dictionary<string, object>
                {
                    ["parameter"] = parameter.Name,
                    ["nodeId"] = nodeId ?? string.Empty
                });
            }
        }

        return new InputResolutionResult(effective, redacted, secretInputs, templates);
    }

    private static void ApplyInputs(
        IReadOnlyDictionary<string, ParameterDefinition> parameters,
        IReadOnlyDictionary<string, JsonElement>? inputs,
        IReadOnlyDictionary<string, string> environment,
        string? nodeId,
        Dictionary<string, object?> effective,
        Dictionary<string, object?> redacted,
        HashSet<string> secretInputs,
        IDictionary<string, JsonElement> templates)
    {
        if (inputs is null)
        {
            return;
        }

        foreach (var (key, value) in inputs)
        {
            var parameter = parameters.TryGetValue(key, out var parameterDef)
                ? parameterDef
                : new ParameterDefinition { Name = key, Type = "string", Required = false };
            templates[key] = value.Clone();

            if (EnvRef.TryParse(value, out var envRef))
            {
                var resolved = ResolveEnvRef(parameter, envRef!, environment, key, nodeId);
                effective[key] = resolved;
                if (envRef!.Secret)
                {
                    redacted[key] = "***";
                    secretInputs.Add(key);
                }
                else
                {
                    redacted[key] = resolved;
                }
            }
            else
            {
                var resolved = ParameterTypes.ConvertLiteral(parameter.Type, value);
                ValidateEnum(parameter, resolved);
                effective[key] = resolved;
                redacted[key] = resolved;
            }
        }
    }

    private static object? ResolveEnvRef(
        ParameterDefinition parameter,
        EnvRef envRef,
        IReadOnlyDictionary<string, string> environment,
        string parameterName,
        string? nodeId)
    {
        environment.TryGetValue(envRef.Env, out var value);
        var isEmpty = string.IsNullOrEmpty(value);

        if (isEmpty)
        {
            if (envRef.Default is { } defaultValue)
            {
                var literal = ParameterTypes.ConvertLiteral(parameter.Type, defaultValue.Value);
                ValidateEnum(parameter, literal);
                return literal;
            }

            if (envRef.Required)
            {
                throw new ValidationException("EnvRef.ResolveFailed", new Dictionary<string, object>
                {
                    ["parameter"] = parameterName,
                    ["nodeId"] = nodeId ?? string.Empty,
                    ["env"] = envRef.Env
                });
            }

            return null;
        }

        var resolved = ParameterTypes.ConvertFromEnv(parameter.Type, value!);
        ValidateEnum(parameter, resolved);
        return resolved;
    }

    private static void ValidateEnum(ParameterDefinition parameter, object? value)
    {
        if (!parameter.Type.StartsWith("enum", StringComparison.OrdinalIgnoreCase) || parameter.EnumValues is null)
        {
            return;
        }

        var allowed = new HashSet<string>(parameter.EnumValues, StringComparer.OrdinalIgnoreCase);
        if (parameter.Type.Equals("enum", StringComparison.OrdinalIgnoreCase))
        {
            if (value is string stringValue && !allowed.Contains(stringValue))
            {
                throw new ValidationException("Inputs.Enum.Invalid", new Dictionary<string, object>
                {
                    ["parameter"] = parameter.Name,
                    ["value"] = stringValue
                });
            }
        }
        else if (value is IEnumerable<object?> list)
        {
            foreach (var item in list)
            {
                if (item is string stringValue && !allowed.Contains(stringValue))
                {
                    throw new ValidationException("Inputs.Enum.Invalid", new Dictionary<string, object>
                    {
                        ["parameter"] = parameter.Name,
                        ["value"] = stringValue
                    });
                }
            }
        }
    }
}
