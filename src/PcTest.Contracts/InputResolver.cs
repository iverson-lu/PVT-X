using System.Globalization;
using System.Text.Json;

namespace PcTest.Contracts;

public static class InputResolver
{
    public static EffectiveInputsResult ResolveEffectiveInputs(
        IReadOnlyList<ParameterDefinition> parameters,
        IReadOnlyDictionary<string, JsonElement> defaults,
        IReadOnlyDictionary<string, JsonElement>? suiteInputs,
        IReadOnlyDictionary<string, JsonElement>? overrides,
        IReadOnlyDictionary<string, string> environment,
        out ValidationResult validation)
    {
        validation = new ValidationResult();
        Dictionary<string, ParameterDefinition> parameterMap = parameters.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, JsonElement> combined = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, JsonElement> pair in defaults)
        {
            combined[pair.Key] = pair.Value;
        }

        if (suiteInputs is not null)
        {
            foreach (KeyValuePair<string, JsonElement> pair in suiteInputs)
            {
                combined[pair.Key] = pair.Value;
            }
        }

        if (overrides is not null)
        {
            foreach (KeyValuePair<string, JsonElement> pair in overrides)
            {
                combined[pair.Key] = pair.Value;
            }
        }

        foreach (string key in combined.Keys)
        {
            if (!parameterMap.ContainsKey(key))
            {
                validation.Add("Inputs.Unknown", $"Unknown input '{key}'.", new Dictionary<string, object?>
                {
                    ["name"] = key
                });
            }
        }

        Dictionary<string, object> resolvedInputs = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, object> redactedInputs = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> secretKeys = new(StringComparer.OrdinalIgnoreCase);

        foreach (ParameterDefinition parameter in parameters)
        {
            if (!combined.TryGetValue(parameter.Name, out JsonElement value))
            {
                if (parameter.Required)
                {
                    validation.Add("Inputs.Missing", $"Required input '{parameter.Name}' is missing.", new Dictionary<string, object?>
                    {
                        ["name"] = parameter.Name
                    });
                }

                continue;
            }

            if (!TryResolveValue(parameter, value, environment, out object? resolved, out bool secret, out string? error))
            {
                validation.Add("Inputs.Invalid", error ?? "Invalid input.", new Dictionary<string, object?>
                {
                    ["name"] = parameter.Name
                });
                continue;
            }

            if (resolved is not null)
            {
                resolvedInputs[parameter.Name] = resolved;
                if (secret)
                {
                    secretKeys.Add(parameter.Name);
                    redactedInputs[parameter.Name] = "***";
                }
                else
                {
                    redactedInputs[parameter.Name] = resolved;
                }
            }
        }

        return new EffectiveInputsResult
        {
            Inputs = resolvedInputs,
            RedactedInputs = redactedInputs,
            SecretKeys = secretKeys
        };
    }

    public static Dictionary<string, JsonElement> ExtractDefaults(IReadOnlyList<ParameterDefinition>? parameters)
    {
        Dictionary<string, JsonElement> defaults = new(StringComparer.OrdinalIgnoreCase);
        if (parameters is null)
        {
            return defaults;
        }

        foreach (ParameterDefinition parameter in parameters)
        {
            if (parameter.Default is JsonElement element)
            {
                defaults[parameter.Name] = element;
            }
        }

        return defaults;
    }

    private static bool TryResolveValue(
        ParameterDefinition parameter,
        JsonElement value,
        IReadOnlyDictionary<string, string> environment,
        out object? resolved,
        out bool secret,
        out string? error)
    {
        resolved = null;
        secret = false;
        error = null;

        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("envRef", out JsonElement envRefElement))
        {
            string? envKey = envRefElement.GetString();
            if (string.IsNullOrWhiteSpace(envKey))
            {
                error = "envRef must be a non-empty string.";
                return false;
            }

            if (!environment.TryGetValue(envKey, out string? envValue))
            {
                error = $"Environment variable '{envKey}' was not found.";
                return false;
            }

            if (value.TryGetProperty("secret", out JsonElement secretElement) && secretElement.ValueKind == JsonValueKind.True)
            {
                secret = true;
            }

            return TryConvertFromString(parameter, envValue, out resolved, out error);
        }

        return TryConvertFromJson(parameter, value, out resolved, out error);
    }

    private static bool TryConvertFromString(ParameterDefinition parameter, string envValue, out object? resolved, out string? error)
    {
        resolved = null;
        error = null;
        string type = parameter.Type;

        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(envValue);
                return TryConvertFromJson(parameter, document.RootElement, out resolved, out error);
            }
            catch (JsonException)
            {
                error = $"EnvRef for array parameter '{parameter.Name}' must be a JSON array.";
                return false;
            }
        }

        return type switch
        {
            "string" or "path" or "file" or "folder" or "enum" => ValidateEnum(parameter, envValue, out resolved, out error),
            "int" => TryParseInt(envValue, out resolved, out error),
            "double" => TryParseDouble(envValue, out resolved, out error),
            "boolean" => TryParseBool(envValue, out resolved, out error),
            _ => FailUnknownType(parameter.Type, out error)
        };
    }

    private static bool TryConvertFromJson(ParameterDefinition parameter, JsonElement value, out object? resolved, out string? error)
    {
        resolved = null;
        error = null;

        switch (parameter.Type)
        {
            case "string":
            case "path":
            case "file":
            case "folder":
                if (value.ValueKind != JsonValueKind.String)
                {
                    error = "Expected string value.";
                    return false;
                }

                resolved = value.GetString() ?? string.Empty;
                return true;
            case "enum":
                if (value.ValueKind != JsonValueKind.String)
                {
                    error = "Expected enum string value.";
                    return false;
                }

                return ValidateEnum(parameter, value.GetString() ?? string.Empty, out resolved, out error);
            case "int":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int intValue))
                {
                    error = "Expected int value.";
                    return false;
                }

                resolved = intValue;
                return true;
            case "double":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double doubleValue))
                {
                    error = "Expected double value.";
                    return false;
                }

                resolved = doubleValue;
                return true;
            case "boolean":
                if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                {
                    error = "Expected boolean value.";
                    return false;
                }

                resolved = value.GetBoolean();
                return true;
            case "string[]":
            case "path[]":
            case "file[]":
            case "folder[]":
                return ConvertArray(parameter, value, element =>
                {
                    if (element.ValueKind != JsonValueKind.String)
                    {
                        return null;
                    }

                    return element.GetString();
                }, out resolved, out error);
            case "enum[]":
                return ConvertArray(parameter, value, element =>
                {
                    if (element.ValueKind != JsonValueKind.String)
                    {
                        return null;
                    }

                    string item = element.GetString() ?? string.Empty;
                    if (!IsEnumValueAllowed(parameter, item))
                    {
                        return null;
                    }

                    return item;
                }, out resolved, out error);
            case "int[]":
                return ConvertArray(parameter, value, element =>
                {
                    if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int item))
                    {
                        return null;
                    }

                    return item;
                }, out resolved, out error);
            case "double[]":
                return ConvertArray(parameter, value, element =>
                {
                    if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out double item))
                    {
                        return null;
                    }

                    return item;
                }, out resolved, out error);
            case "boolean[]":
                return ConvertArray(parameter, value, element =>
                {
                    if (element.ValueKind != JsonValueKind.True && element.ValueKind != JsonValueKind.False)
                    {
                        return null;
                    }

                    return element.GetBoolean();
                }, out resolved, out error);
            default:
                error = $"Unknown parameter type '{parameter.Type}'.";
                return false;
        }
    }

    private static bool ConvertArray<T>(ParameterDefinition parameter, JsonElement value, Func<JsonElement, T?> converter, out object? resolved, out string? error)
    {
        resolved = null;
        error = null;

        if (value.ValueKind != JsonValueKind.Array)
        {
            error = "Expected JSON array value.";
            return false;
        }

        List<T> items = new();
        foreach (JsonElement element in value.EnumerateArray())
        {
            T? converted = converter(element);
            if (converted is null)
            {
                error = "Invalid array value.";
                return false;
            }

            items.Add(converted);
        }

        resolved = items;
        return true;
    }

    private static bool TryParseInt(string value, out object? resolved, out string? error)
    {
        resolved = null;
        error = null;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            error = "Expected int value.";
            return false;
        }

        resolved = parsed;
        return true;
    }

    private static bool TryParseDouble(string value, out object? resolved, out string? error)
    {
        resolved = null;
        error = null;
        if (!double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double parsed))
        {
            error = "Expected double value.";
            return false;
        }

        resolved = parsed;
        return true;
    }

    private static bool TryParseBool(string value, out object? resolved, out string? error)
    {
        resolved = null;
        error = null;
        if (bool.TryParse(value, out bool parsed))
        {
            resolved = parsed;
            return true;
        }

        if (value == "1")
        {
            resolved = true;
            return true;
        }

        if (value == "0")
        {
            resolved = false;
            return true;
        }

        error = "Expected boolean value.";
        return false;
    }

    private static bool ValidateEnum(ParameterDefinition parameter, string value, out object? resolved, out string? error)
    {
        resolved = null;
        error = null;
        if (!IsEnumValueAllowed(parameter, value))
        {
            error = "Enum value is not allowed.";
            return false;
        }

        resolved = value;
        return true;
    }

    private static bool IsEnumValueAllowed(ParameterDefinition parameter, string value)
    {
        if (parameter.EnumValues is null || parameter.EnumValues.Length == 0)
        {
            return true;
        }

        return parameter.EnumValues.Contains(value, StringComparer.Ordinal);
    }

    private static bool FailUnknownType(string type, out string? error)
    {
        error = $"Unknown parameter type '{type}'.";
        return false;
    }
}
