using System.Globalization;
using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine;

public sealed record ResolvedInputs(
    Dictionary<string, object?> EffectiveInputs,
    Dictionary<string, object?> RedactedInputs,
    HashSet<string> SecretInputs,
    List<RunEvent> Events);

public sealed class InputResolver
{
    public ResolvedInputs ResolveInputs(
        TestCaseManifest testCase,
        Dictionary<string, object?> baseInputs,
        Dictionary<string, object?> overrideInputs,
        Dictionary<string, string> effectiveEnvironment,
        string? nodeId)
    {
        var parameters = testCase.Parameters ?? Array.Empty<ParameterDefinition>();
        var parameterMap = parameters.ToDictionary(p => p.Name, p => p, StringComparer.Ordinal);
        var merged = new Dictionary<string, object?>();

        foreach (var parameter in parameters)
        {
            if (parameter.Default is not null)
            {
                merged[parameter.Name] = parameter.Default;
            }
        }

        MergeInputs(merged, baseInputs);
        MergeInputs(merged, overrideInputs);

        foreach (var key in merged.Keys.ToList())
        {
            if (!parameterMap.ContainsKey(key))
            {
                throw new EngineException("Inputs.Unknown", $"Unknown input '{key}'.", new Dictionary<string, object?>
                {
                    ["parameter"] = key,
                    ["nodeId"] = nodeId
                });
            }
        }

        foreach (var parameter in parameters)
        {
            if (parameter.Required && !merged.ContainsKey(parameter.Name))
            {
                throw new EngineException("Inputs.RequiredMissing", $"Required input '{parameter.Name}' missing.", new Dictionary<string, object?>
                {
                    ["parameter"] = parameter.Name,
                    ["nodeId"] = nodeId
                });
            }
        }

        var resolved = new Dictionary<string, object?>();
        var redacted = new Dictionary<string, object?>();
        var secretInputs = new HashSet<string>(StringComparer.Ordinal);
        var events = new List<RunEvent>();

        foreach (var (name, value) in merged)
        {
            var parameter = parameterMap[name];
            var resolvedValue = ResolveValue(parameter, value, effectiveEnvironment, nodeId, name, secretInputs, events, out var redactedValue);
            resolved[name] = resolvedValue;
            redacted[name] = redactedValue;
        }

        return new ResolvedInputs(resolved, redacted, secretInputs, events);
    }

    private static void MergeInputs(Dictionary<string, object?> target, Dictionary<string, object?> source)
    {
        foreach (var entry in source)
        {
            target[entry.Key] = entry.Value;
        }
    }

    private static object? ResolveValue(
        ParameterDefinition parameter,
        object? value,
        Dictionary<string, string> environment,
        string? nodeId,
        string name,
        HashSet<string> secretInputs,
        List<RunEvent> events,
        out object? redactedValue)
    {
        var parsedType = parameter.ParsedType;
        if (value is JsonElement element)
        {
            value = ConvertElement(element);
        }

        if (value is Dictionary<string, object?> envRef && envRef.ContainsKey("$env"))
        {
            return ResolveEnvRef(parameter, envRef, environment, nodeId, name, secretInputs, events, out redactedValue);
        }

        var validated = ValidateLiteral(parameter, value, nodeId, name);
        redactedValue = validated;
        return validated;
    }

    private static object? ResolveEnvRef(
        ParameterDefinition parameter,
        Dictionary<string, object?> envRef,
        Dictionary<string, string> environment,
        string? nodeId,
        string name,
        HashSet<string> secretInputs,
        List<RunEvent> events,
        out object? redactedValue)
    {
        if (!envRef.TryGetValue("$env", out var envNameObj) || envNameObj is not string envName || string.IsNullOrWhiteSpace(envName))
        {
            throw new EngineException(SchemaConstants.EnvRefResolveFailed, "EnvRef requires $env.", new Dictionary<string, object?>
            {
                ["parameter"] = name,
                ["nodeId"] = nodeId
            });
        }

        var required = envRef.TryGetValue("required", out var requiredObj) && requiredObj is bool requiredValue && requiredValue;
        var secret = envRef.TryGetValue("secret", out var secretObj) && secretObj is bool secretValue && secretValue;

        var hasDefault = envRef.TryGetValue("default", out var defaultObj);

        environment.TryGetValue(envName, out var envValue);
        if (string.IsNullOrEmpty(envValue))
        {
            if (hasDefault)
            {
                var literal = ValidateLiteral(parameter, defaultObj, nodeId, name);
                redactedValue = secret ? "***" : literal;
                if (secret)
                {
                    secretInputs.Add(name);
                    events.Add(new RunEvent(DateTime.UtcNow, SchemaConstants.EnvRefSecretOnCommandLine, new Dictionary<string, object?>
                    {
                        ["parameter"] = name,
                        ["nodeId"] = nodeId
                    }));
                }
                return literal;
            }

            if (required)
            {
                throw new EngineException(SchemaConstants.EnvRefResolveFailed, "EnvRef required value missing.", new Dictionary<string, object?>
                {
                    ["parameter"] = name,
                    ["nodeId"] = nodeId
                });
            }

            redactedValue = secret ? "***" : null;
            if (secret)
            {
                secretInputs.Add(name);
            }

            return null;
        }

        var resolved = ConvertFromString(parameter, envValue, nodeId, name);
        if (secret)
        {
            secretInputs.Add(name);
            events.Add(new RunEvent(DateTime.UtcNow, SchemaConstants.EnvRefSecretOnCommandLine, new Dictionary<string, object?>
            {
                ["parameter"] = name,
                ["nodeId"] = nodeId
            }));
        }

        redactedValue = secret ? "***" : resolved;
        return resolved;
    }

    private static object? ValidateLiteral(ParameterDefinition parameter, object? value, string? nodeId, string name)
    {
        if (value is JsonElement element)
        {
            value = ConvertElement(element);
        }

        var parsed = parameter.ParsedType;
        if (value == null)
        {
            return null;
        }

        if (parsed.IsArray())
        {
            if (value is not IEnumerable<object?> enumerable)
            {
                throw new EngineException("Inputs.InvalidType", "Input must be array.", new Dictionary<string, object?>
                {
                    ["parameter"] = name,
                    ["nodeId"] = nodeId
                });
            }

            var list = enumerable.ToList();
            var converted = list.Select(item => ConvertScalar(parameter, item, nodeId, name)).ToList();
            ValidateEnum(parameter, converted, nodeId, name);
            return converted.ToArray();
        }

        var scalar = ConvertScalar(parameter, value, nodeId, name);
        ValidateEnum(parameter, scalar, nodeId, name);
        return scalar;
    }

    private static object? ConvertScalar(ParameterDefinition parameter, object? value, string? nodeId, string name)
    {
        if (value is JsonElement element)
        {
            value = ConvertElement(element);
        }

        var parsed = parameter.ParsedType;
        return parsed switch
        {
            ParameterType.Int => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            ParameterType.Double => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            ParameterType.Boolean => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
            ParameterType.String or ParameterType.Path or ParameterType.File or ParameterType.Folder or ParameterType.Enum => value?.ToString(),
            _ when parsed.IsArray() => value,
            _ => value
        };
    }

    private static object? ConvertFromString(ParameterDefinition parameter, string value, string? nodeId, string name)
    {
        var parsed = parameter.ParsedType;
        if (parsed.IsArray())
        {
            try
            {
                var array = JsonSerializer.Deserialize<List<JsonElement>>(value, JsonUtilities.SerializerOptions)
                           ?? throw new InvalidOperationException();
                var converted = array.Select(item => ConvertScalar(parameter, item, nodeId, name)).ToList();
                ValidateEnum(parameter, converted, nodeId, name);
                return converted.ToArray();
            }
            catch (Exception ex)
            {
                throw new EngineException(SchemaConstants.EnvRefResolveFailed, "EnvRef array parse failed.", new Dictionary<string, object?>
                {
                    ["parameter"] = name,
                    ["nodeId"] = nodeId,
                    ["message"] = ex.Message
                });
            }
        }

        try
        {
            object? converted = parsed switch
            {
                ParameterType.Int => int.Parse(value, CultureInfo.InvariantCulture),
                ParameterType.Double => double.Parse(value, CultureInfo.InvariantCulture),
                ParameterType.Boolean => ParseBool(value),
                _ => value
            };

            ValidateEnum(parameter, converted, nodeId, name);
            return converted;
        }
        catch (EngineException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EngineException(SchemaConstants.EnvRefResolveFailed, "EnvRef parse failed.", new Dictionary<string, object?>
            {
                ["parameter"] = name,
                ["nodeId"] = nodeId,
                ["message"] = ex.Message
            });
        }
    }

    private static bool ParseBool(string value)
    {
        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue;
        }

        if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new InvalidOperationException("Boolean parse failed.");
    }

    private static void ValidateEnum(ParameterDefinition parameter, object? value, string? nodeId, string name)
    {
        if (parameter.EnumValues is null)
        {
            return;
        }

        if (value is string stringValue)
        {
            if (!parameter.EnumValues.Contains(stringValue))
            {
                throw new EngineException("Inputs.Enum.Invalid", "Enum value invalid.", new Dictionary<string, object?>
                {
                    ["parameter"] = name,
                    ["nodeId"] = nodeId
                });
            }

            return;
        }

        if (value is IEnumerable<object?> arrayValue)
        {
            foreach (var item in arrayValue)
            {
                var itemString = item?.ToString();
                if (itemString is null || !parameter.EnumValues.Contains(itemString))
                {
                    throw new EngineException("Inputs.Enum.Invalid", "Enum array contains invalid value.", new Dictionary<string, object?>
                    {
                        ["parameter"] = name,
                        ["nodeId"] = nodeId
                    });
                }
            }
        }
    }

    private static object? ConvertElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var longValue) ? longValue : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText(), JsonUtilities.SerializerOptions),
            JsonValueKind.Array => JsonSerializer.Deserialize<List<object?>>(element.GetRawText(), JsonUtilities.SerializerOptions),
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }
}
