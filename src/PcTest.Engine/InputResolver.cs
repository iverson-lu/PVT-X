using System.Globalization;
using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Models;

namespace PcTest.Engine;

public sealed class ResolvedInputs
{
    public Dictionary<string, object?> Values { get; } = new();
    public Dictionary<string, object?> RedactedValues { get; } = new();
    public HashSet<string> SecretInputs { get; } = new();
}

public static class InputResolver
{
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "int", "double", "string", "boolean", "path", "file", "folder", "enum",
        "int[]", "double[]", "string[]", "boolean[]", "path[]", "file[]", "folder[]", "enum[]"
    };

    public static ResolvedInputs Resolve(TestCaseManifest manifest, Dictionary<string, JsonElement> inputs, Dictionary<string, string> environment, string? nodeId)
    {
        var resolved = new ResolvedInputs();
        var parameters = manifest.Parameters ?? new List<ParameterDefinition>();
        var parameterMap = parameters.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in inputs)
        {
            if (!parameterMap.ContainsKey(entry.Key))
            {
                throw new EngineException("Inputs.Unknown", new { parameter = entry.Key, nodeId });
            }
        }

        foreach (var parameter in parameters)
        {
            if (!SupportedTypes.Contains(parameter.Type))
            {
                throw new EngineException("Inputs.Type.Invalid", new { parameter = parameter.Name, type = parameter.Type });
            }

            if (!inputs.TryGetValue(parameter.Name, out var value))
            {
                if (parameter.Required)
                {
                    throw new EngineException("Inputs.RequiredMissing", new { parameter = parameter.Name, nodeId });
                }

                continue;
            }

            var resolvedValue = ResolveValue(parameter, value, environment, nodeId, resolved.SecretInputs);
            if (resolvedValue.value is null && parameter.Required)
            {
                throw new EngineException("Inputs.RequiredMissing", new { parameter = parameter.Name, nodeId });
            }
            resolved.Values[parameter.Name] = resolvedValue.value;
            resolved.RedactedValues[parameter.Name] = resolvedValue.redacted;
        }

        return resolved;
    }

    private static (object? value, object? redacted) ResolveValue(ParameterDefinition parameter, JsonElement input, Dictionary<string, string> environment, string? nodeId, HashSet<string> secretInputs)
    {
        if (EnvRef.TryParse(input, out var envRef))
        {
            if (string.IsNullOrWhiteSpace(envRef?.Env))
            {
                throw new EngineException("EnvRef.Invalid", new { parameter = parameter.Name, nodeId });
            }

            environment.TryGetValue(envRef.Env, out var envValue);
            var isEmpty = string.IsNullOrEmpty(envValue);

            if (isEmpty)
            {
                if (envRef.Default is { } defaultValue)
                {
                    var parsedDefault = ResolveLiteral(parameter, defaultValue);
                    return (parsedDefault, envRef.Secret ? "***" : parsedDefault);
                }

                if (envRef.Required)
                {
                    throw new EngineException("EnvRef.ResolveFailed", new { parameter = parameter.Name, nodeId, reason = "Missing" });
                }

                return (null, null);
            }

            var converted = ConvertFromString(parameter, envValue!);
            if (parameter.Type.StartsWith("enum", StringComparison.OrdinalIgnoreCase))
            {
                ValidateEnum(parameter, converted);
            }

            if (envRef.Secret)
            {
                secretInputs.Add(parameter.Name);
                return (converted, "***");
            }

            return (converted, converted);
        }

        var literal = ResolveLiteral(parameter, input);
        return (literal, literal);
    }

    private static object? ResolveLiteral(ParameterDefinition parameter, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        object? value = parameter.Type switch
        {
            "int" => element.GetInt32(),
            "double" => element.GetDouble(),
            "string" => element.GetString(),
            "boolean" => element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False ? element.GetBoolean() : throw new EngineException("Inputs.TypeMismatch", new { parameter = parameter.Name }),
            "path" or "file" or "folder" or "enum" => element.GetString(),
            "int[]" => element.EnumerateArray().Select(x => x.GetInt32()).ToArray(),
            "double[]" => element.EnumerateArray().Select(x => x.GetDouble()).ToArray(),
            "string[]" or "path[]" or "file[]" or "folder[]" or "enum[]" => element.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray(),
            "boolean[]" => element.EnumerateArray().Select(x => x.GetBoolean()).ToArray(),
            _ => element.ToString()
        };

        if (parameter.Type.StartsWith("enum", StringComparison.OrdinalIgnoreCase))
        {
            ValidateEnum(parameter, value);
        }

        return value;
    }

    private static object ConvertFromString(ParameterDefinition parameter, string value)
    {
        var type = parameter.Type.ToLowerInvariant();
        if (type is "string" or "path" or "file" or "folder" or "enum")
        {
            return value;
        }

        if (type == "int")
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            {
                throw new EngineException("EnvRef.ResolveFailed", new { parameter = parameter.Name, reason = "Parse" });
            }

            return result;
        }

        if (type == "double")
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            {
                throw new EngineException("EnvRef.ResolveFailed", new { parameter = parameter.Name, reason = "Parse" });
            }

            return result;
        }

        if (type == "boolean")
        {
            if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (value == "1")
            {
                return true;
            }

            if (value == "0")
            {
                return false;
            }

            throw new EngineException("EnvRef.ResolveFailed", new { parameter = parameter.Name, reason = "Parse" });
        }

        if (type.EndsWith("[]", StringComparison.OrdinalIgnoreCase))
        {
            var doc = JsonDocument.Parse(value);
            var element = doc.RootElement;
            if (element.ValueKind != JsonValueKind.Array)
            {
                throw new EngineException("EnvRef.ResolveFailed", new { parameter = parameter.Name, reason = "Parse" });
            }

            var inner = type[..^2];
            return inner switch
            {
                "int" => element.EnumerateArray().Select(x => x.GetInt32()).ToArray(),
                "double" => element.EnumerateArray().Select(x => x.GetDouble()).ToArray(),
                "boolean" => element.EnumerateArray().Select(x => x.GetBoolean()).ToArray(),
                _ => element.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray()
            };
        }

        return value;
    }

    private static void ValidateEnum(ParameterDefinition parameter, object? value)
    {
        var allowed = parameter.EnumValues ?? new List<string>();
        if (value is string s)
        {
            if (!allowed.Contains(s))
            {
                throw new EngineException("Inputs.Enum.Invalid", new { parameter = parameter.Name, value = s });
            }
        }
        else if (value is IEnumerable<string> list)
        {
            foreach (var item in list)
            {
                if (!allowed.Contains(item))
                {
                    throw new EngineException("Inputs.Enum.Invalid", new { parameter = parameter.Name, value = item });
                }
            }
        }
    }
}
