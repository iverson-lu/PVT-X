using System.Globalization;
using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine;

public sealed record InputResolution(
    IReadOnlyDictionary<string, object?> EffectiveInputs,
    IReadOnlyDictionary<string, object?> RedactedInputs,
    IReadOnlyDictionary<string, JsonElement> InputTemplates,
    IReadOnlyCollection<string> SecretInputs
);

public static class InputResolver
{
    private static readonly HashSet<string> ArrayTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "int[]", "double[]", "string[]", "boolean[]", "path[]", "file[]", "folder[]", "enum[]"
    };

    public static InputResolution ResolveInputs(
        IReadOnlyDictionary<string, JsonElement>? inputs,
        IReadOnlyDictionary<string, JsonElement>? overrides,
        ParameterDefinition[]? parameters,
        IReadOnlyDictionary<string, string> effectiveEnvironment,
        string? nodeId)
    {
        var templateInputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var effectiveInputs = new Dictionary<string, object?>(StringComparer.Ordinal);
        var redactedInputs = new Dictionary<string, object?>(StringComparer.Ordinal);
        var secretInputs = new HashSet<string>(StringComparer.Ordinal);

        var paramMap = parameters?.ToDictionary(p => p.Name, StringComparer.Ordinal)
                       ?? new Dictionary<string, ParameterDefinition>(StringComparer.Ordinal);

        foreach (var param in paramMap.Values)
        {
            if (param.Default.HasValue)
            {
                var resolved = ResolveLiteral(param, param.Default.Value);
                templateInputs[param.Name] = param.Default.Value;
                effectiveInputs[param.Name] = resolved;
                redactedInputs[param.Name] = resolved;
            }
        }

        ApplyInputs(inputs, paramMap, effectiveEnvironment, nodeId, templateInputs, effectiveInputs, redactedInputs, secretInputs);
        ApplyInputs(overrides, paramMap, effectiveEnvironment, nodeId, templateInputs, effectiveInputs, redactedInputs, secretInputs);

        foreach (var param in paramMap.Values)
        {
            if (param.Required && (!effectiveInputs.TryGetValue(param.Name, out var value) || value == null))
            {
                throw CreateResolveFailed(param.Name, nodeId, $"Required input {param.Name} is missing.");
            }
        }

        return new InputResolution(effectiveInputs, redactedInputs, templateInputs, secretInputs);
    }

    public static InputResolution ResolveStandaloneInputs(
        IReadOnlyDictionary<string, JsonElement>? caseInputs,
        ParameterDefinition[]? parameters,
        IReadOnlyDictionary<string, string> effectiveEnvironment)
    {
        return ResolveInputs(caseInputs, null, parameters, effectiveEnvironment, null);
    }

    private static void ApplyInputs(
        IReadOnlyDictionary<string, JsonElement>? inputs,
        IReadOnlyDictionary<string, ParameterDefinition> paramMap,
        IReadOnlyDictionary<string, string> effectiveEnvironment,
        string? nodeId,
        IDictionary<string, JsonElement> templates,
        IDictionary<string, object?> effective,
        IDictionary<string, object?> redacted,
        ISet<string> secrets)
    {
        if (inputs == null)
        {
            return;
        }

        foreach (var (name, value) in inputs)
        {
            if (!paramMap.TryGetValue(name, out var param))
            {
                throw CreateResolveFailed(name, nodeId, "Unknown input name.");
            }

            if (TryParseEnvRef(value, out var envRef))
            {
                templates[name] = value.Clone();
                var resolved = ResolveEnvRef(envRef, param, effectiveEnvironment, name, nodeId, out var isSecret);
                if (resolved != null)
                {
                    effective[name] = resolved;
                    if (isSecret)
                    {
                        redacted[name] = "***";
                        secrets.Add(name);
                    }
                    else
                    {
                        redacted[name] = resolved;
                    }
                }
            }
            else
            {
                var literal = ResolveLiteral(param, value);
                templates[name] = value.Clone();
                if (literal != null)
                {
                    effective[name] = literal;
                    redacted[name] = literal;
                }
            }
        }
    }

    private static bool TryParseEnvRef(JsonElement value, out EnvRef envRef)
    {
        envRef = default;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!value.TryGetProperty("$env", out var envNameElement) || envNameElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        envRef = new EnvRef
        {
            Env = envNameElement.GetString() ?? string.Empty,
            Default = value.TryGetProperty("default", out var defaultElement) ? defaultElement.Clone() : null,
            Required = value.TryGetProperty("required", out var requiredElement) && requiredElement.ValueKind == JsonValueKind.True,
            Secret = value.TryGetProperty("secret", out var secretElement) && secretElement.ValueKind == JsonValueKind.True
        };

        return true;
    }

    private static object? ResolveEnvRef(
        EnvRef envRef,
        ParameterDefinition param,
        IReadOnlyDictionary<string, string> env,
        string paramName,
        string? nodeId,
        out bool isSecret)
    {
        isSecret = envRef.Secret;
        if (string.IsNullOrWhiteSpace(envRef.Env))
        {
            throw CreateResolveFailed(paramName, nodeId, "EnvRef $env is empty.");
        }

        env.TryGetValue(envRef.Env, out var envValue);
        if (string.IsNullOrEmpty(envValue))
        {
            if (envRef.Default.HasValue)
            {
                return ResolveLiteral(param, envRef.Default.Value);
            }

            if (envRef.Required)
            {
                throw CreateResolveFailed(paramName, nodeId, $"Env {envRef.Env} is required.");
            }

            return null;
        }

        return ResolveFromString(param, envValue, paramName, nodeId);
    }

    private static object? ResolveLiteral(ParameterDefinition param, JsonElement value)
    {
        var type = NormalizeType(param.Type);
        if (ArrayTypes.Contains(type))
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new PcTestException(new[]
                {
                    new PcTestError(SpecConstants.EnvRefResolveFailed, $"Expected array for {param.Name}.")
                });
            }

            return ResolveArrayLiteral(param, value);
        }

        return type switch
        {
            "int" => value.ValueKind == JsonValueKind.Number ? value.GetInt32() : throw CreateLiteralError(param.Name),
            "double" => value.ValueKind == JsonValueKind.Number ? value.GetDouble() : throw CreateLiteralError(param.Name),
            "boolean" => value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False ? value.GetBoolean() : throw CreateLiteralError(param.Name),
            "string" or "path" or "file" or "folder" => value.ValueKind == JsonValueKind.String ? value.GetString() : throw CreateLiteralError(param.Name),
            "enum" => ResolveEnumLiteral(param, value),
            _ => value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
        };
    }

    private static object? ResolveEnumLiteral(ParameterDefinition param, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw CreateLiteralError(param.Name);
        }

        var literal = value.GetString();
        if (param.EnumValues == null || param.EnumValues.Length == 0)
        {
            throw CreateLiteralError(param.Name);
        }

        if (!param.EnumValues.Contains(literal, StringComparer.Ordinal))
        {
            throw CreateLiteralError(param.Name);
        }

        return literal;
    }

    private static object? ResolveFromString(ParameterDefinition param, string envValue, string paramName, string? nodeId)
    {
        var type = NormalizeType(param.Type);
        if (ArrayTypes.Contains(type))
        {
            try
            {
                using var doc = JsonDocument.Parse(envValue);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    throw CreateResolveFailed(paramName, nodeId, "EnvRef array must be JSON array.");
                }

                return ResolveArrayLiteral(param, doc.RootElement);
            }
            catch (JsonException)
            {
                throw CreateResolveFailed(paramName, nodeId, "EnvRef array must be JSON array.");
            }
        }

        try
        {
            return type switch
            {
                "int" => int.Parse(envValue, NumberStyles.Integer, CultureInfo.InvariantCulture),
                "double" => double.Parse(envValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture),
                "boolean" => ParseBool(envValue, paramName, nodeId),
                "enum" => ValidateEnum(param, envValue, paramName, nodeId),
                "string" or "path" or "file" or "folder" => envValue,
                _ => envValue
            };
        }
        catch (FormatException)
        {
            throw CreateResolveFailed(paramName, nodeId, $"EnvRef parse failed for {paramName}.");
        }
    }

    private static object? ResolveArrayLiteral(ParameterDefinition param, JsonElement value)
    {
        var type = NormalizeType(param.Type);
        var elementType = type[..^2];
        var list = new List<object?>();
        foreach (var item in value.EnumerateArray())
        {
            var resolved = elementType switch
            {
                "int" => item.ValueKind == JsonValueKind.Number ? item.GetInt32() : throw CreateLiteralError(param.Name),
                "double" => item.ValueKind == JsonValueKind.Number ? item.GetDouble() : throw CreateLiteralError(param.Name),
                "boolean" => item.ValueKind == JsonValueKind.True || item.ValueKind == JsonValueKind.False ? item.GetBoolean() : throw CreateLiteralError(param.Name),
                "enum" => ResolveEnumLiteral(param, item),
                "string" or "path" or "file" or "folder" => item.ValueKind == JsonValueKind.String ? item.GetString() : throw CreateLiteralError(param.Name),
                _ => item.ToString()
            };

            list.Add(resolved);
        }

        return list;
    }

    private static bool ParseBool(string value, string paramName, string? nodeId)
    {
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1")
        {
            return true;
        }

        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) || value == "0")
        {
            return false;
        }

        throw CreateResolveFailed(paramName, nodeId, "EnvRef boolean parse failed.");
    }

    private static object ValidateEnum(ParameterDefinition param, string value, string paramName, string? nodeId)
    {
        if (param.EnumValues == null || param.EnumValues.Length == 0)
        {
            throw CreateResolveFailed(paramName, nodeId, "Enum values are missing.");
        }

        if (!param.EnumValues.Contains(value, StringComparer.Ordinal))
        {
            throw CreateResolveFailed(paramName, nodeId, "Enum value is invalid.");
        }

        return value;
    }

    private static string NormalizeType(string type)
    {
        return string.IsNullOrWhiteSpace(type) ? "string" : type.Trim().ToLowerInvariant();
    }

    private static PcTestException CreateResolveFailed(string paramName, string? nodeId, string message)
    {
        var payload = new Dictionary<string, object?>
        {
            ["parameter"] = paramName,
            ["nodeId"] = nodeId
        };

        return new PcTestException(new[]
        {
            new PcTestError(SpecConstants.EnvRefResolveFailed, message, JsonUtils.ToJsonElement(payload))
        });
    }

    private static PcTestException CreateLiteralError(string paramName)
    {
        return CreateResolveFailed(paramName, null, $"Literal value for {paramName} has invalid type.");
    }

    private readonly struct EnvRef
    {
        public string Env { get; init; }
        public JsonElement? Default { get; init; }
        public bool Required { get; init; }
        public bool Secret { get; init; }
    }
}
