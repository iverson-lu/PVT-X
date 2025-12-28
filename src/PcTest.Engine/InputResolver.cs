using System.Globalization;
using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine;

public sealed class ResolvedInputs
{
    public required Dictionary<string, JsonElement> ResolvedJson { get; init; }
    public required Dictionary<string, object?> ResolvedValues { get; init; }
    public required HashSet<string> SecretInputs { get; init; }
    public required Dictionary<string, JsonElement> InputTemplates { get; init; }
}

public sealed class InputResolver
{
    public ValidationResult<ResolvedInputs> Resolve(
        ParameterDefinition[]? parameters,
        Dictionary<string, JsonElement>? inputTemplates,
        Dictionary<string, string> effectiveEnvironment,
        string? nodeId)
    {
        List<ValidationError> errors = new();
        Dictionary<string, ParameterDefinition> parameterMap = new(StringComparer.Ordinal);
        if (parameters is not null)
        {
            foreach (ParameterDefinition param in parameters)
            {
                parameterMap[param.Name] = param;
            }
        }

        Dictionary<string, JsonElement> templates = new(StringComparer.Ordinal);
        if (parameters is not null)
        {
            foreach (ParameterDefinition param in parameters)
            {
                if (param.Default.HasValue)
                {
                    templates[param.Name] = param.Default.Value;
                }
            }
        }

        if (inputTemplates is not null)
        {
            foreach (KeyValuePair<string, JsonElement> pair in inputTemplates)
            {
                templates[pair.Key] = pair.Value;
            }
        }

        Dictionary<string, JsonElement> resolvedJson = new(StringComparer.Ordinal);
        Dictionary<string, object?> resolvedValues = new(StringComparer.Ordinal);
        HashSet<string> secretInputs = new(StringComparer.Ordinal);

        foreach (KeyValuePair<string, JsonElement> pair in templates)
        {
            if (!parameterMap.TryGetValue(pair.Key, out ParameterDefinition? parameter))
            {
                errors.Add(new ValidationError("Inputs.Unknown", $"Unknown input {pair.Key}.", BuildInputPayload(nodeId, pair.Key)));
                continue;
            }

            if (!ParameterTypeParser.TryParse(parameter.Type, out ParameterType parameterType))
            {
                errors.Add(new ValidationError("Inputs.TypeInvalid", $"Invalid parameter type {parameter.Type}.", BuildInputPayload(nodeId, pair.Key)));
                continue;
            }

            if (TryResolveEnvRef(pair.Value, parameterType, parameter.EnumValues, effectiveEnvironment, out object? resolved, out JsonElement resolvedElement, out bool isSecret, out string? error))
            {
                if (resolved is not null)
                {
                    resolvedValues[pair.Key] = resolved;
                    resolvedJson[pair.Key] = resolvedElement;
                    if (isSecret)
                    {
                        secretInputs.Add(pair.Key);
                    }
                }
            }
            else
            {
                errors.Add(new ValidationError("EnvRef.ResolveFailed", error ?? "Failed to resolve input.", BuildInputPayload(nodeId, pair.Key)));
            }
        }

        if (parameters is not null)
        {
            foreach (ParameterDefinition param in parameters)
            {
                if (param.Required && !resolvedValues.ContainsKey(param.Name))
                {
                    errors.Add(new ValidationError("Inputs.RequiredMissing", $"Required input {param.Name} is missing.", BuildInputPayload(nodeId, param.Name)));
                }
            }
        }

        if (errors.Count > 0)
        {
            return ValidationResult<ResolvedInputs>.Failure(errors);
        }

        return ValidationResult<ResolvedInputs>.Success(new ResolvedInputs
        {
            ResolvedJson = resolvedJson,
            ResolvedValues = resolvedValues,
            SecretInputs = secretInputs,
            InputTemplates = templates
        });
    }

    private static Dictionary<string, object?> BuildInputPayload(string? nodeId, string name)
    {
        Dictionary<string, object?> payload = new(StringComparer.Ordinal)
        {
            ["name"] = name
        };
        if (!string.IsNullOrEmpty(nodeId))
        {
            payload["nodeId"] = nodeId;
        }

        return payload;
    }

    private static bool TryResolveEnvRef(
        JsonElement element,
        ParameterType parameterType,
        string[]? enumValues,
        Dictionary<string, string> environment,
        out object? resolvedValue,
        out JsonElement resolvedElement,
        out bool secret,
        out string? error)
    {
        secret = false;
        error = null;
        resolvedValue = null;
        resolvedElement = default;

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("$env", out JsonElement _))
        {
            EnvRef envRef = JsonSerializer.Deserialize<EnvRef>(element.GetRawText()) ?? new EnvRef();
            if (string.IsNullOrWhiteSpace(envRef.Env))
            {
                error = "EnvRef $env is empty.";
                return false;
            }

            string? envValue = null;
            if (environment.TryGetValue(envRef.Env, out string? envValueRaw))
            {
                envValue = envValueRaw;
            }

            bool isEmpty = string.IsNullOrEmpty(envValue);
            if (isEmpty)
            {
                if (envRef.Default.HasValue)
                {
                    return TryResolveLiteral(envRef.Default.Value, parameterType, enumValues, out resolvedValue, out resolvedElement, out error);
                }

                bool required = envRef.Required ?? false;
                if (required)
                {
                    error = $"EnvRef {envRef.Env} is required but missing.";
                    return false;
                }

                resolvedValue = null;
                resolvedElement = JsonSerializer.SerializeToElement<string?>(null);
                secret = envRef.Secret ?? false;
                return true;
            }

            if (!TryConvertFromString(envValue, parameterType, enumValues, out resolvedValue, out resolvedElement, out error))
            {
                return false;
            }

            secret = envRef.Secret ?? false;
            return true;
        }

        return TryResolveLiteral(element, parameterType, enumValues, out resolvedValue, out resolvedElement, out error);
    }

    private static bool TryResolveLiteral(
        JsonElement element,
        ParameterType parameterType,
        string[]? enumValues,
        out object? resolvedValue,
        out JsonElement resolvedElement,
        out string? error)
    {
        error = null;
        resolvedValue = null;
        resolvedElement = default;

        switch (parameterType)
        {
            case ParameterType.String:
            case ParameterType.Path:
            case ParameterType.File:
            case ParameterType.Folder:
            case ParameterType.Enum:
                if (element.ValueKind != JsonValueKind.String)
                {
                    error = "Expected string.";
                    return false;
                }

                string? str = element.GetString();
                if (parameterType == ParameterType.Enum && enumValues is not null && str is not null && !enumValues.Contains(str, StringComparer.Ordinal))
                {
                    error = "Enum value is not allowed.";
                    return false;
                }

                resolvedValue = str ?? string.Empty;
                resolvedElement = JsonSerializer.SerializeToElement(resolvedValue);
                return true;
            case ParameterType.Boolean:
                if (element.ValueKind != JsonValueKind.True && element.ValueKind != JsonValueKind.False)
                {
                    error = "Expected boolean.";
                    return false;
                }

                bool boolValue = element.GetBoolean();
                resolvedValue = boolValue;
                resolvedElement = JsonSerializer.SerializeToElement(boolValue);
                return true;
            case ParameterType.Int:
                if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int intValue))
                {
                    error = "Expected int.";
                    return false;
                }

                resolvedValue = intValue;
                resolvedElement = JsonSerializer.SerializeToElement(intValue);
                return true;
            case ParameterType.Double:
                if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out double doubleValue))
                {
                    error = "Expected double.";
                    return false;
                }

                resolvedValue = doubleValue;
                resolvedElement = JsonSerializer.SerializeToElement(doubleValue);
                return true;
            case ParameterType.StringArray:
            case ParameterType.PathArray:
            case ParameterType.FileArray:
            case ParameterType.FolderArray:
            case ParameterType.EnumArray:
                if (element.ValueKind != JsonValueKind.Array)
                {
                    error = "Expected array.";
                    return false;
                }

                List<string> strings = new();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        error = "Expected string array.";
                        return false;
                    }

                    string? value = item.GetString();
                    if (parameterType == ParameterType.EnumArray && enumValues is not null && value is not null && !enumValues.Contains(value, StringComparer.Ordinal))
                    {
                        error = "Enum array contains invalid value.";
                        return false;
                    }

                    strings.Add(value ?? string.Empty);
                }

                resolvedValue = strings;
                resolvedElement = JsonSerializer.SerializeToElement(strings);
                return true;
            case ParameterType.IntArray:
                if (element.ValueKind != JsonValueKind.Array)
                {
                    error = "Expected array.";
                    return false;
                }

                List<int> ints = new();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out int value))
                    {
                        error = "Expected int array.";
                        return false;
                    }

                    ints.Add(value);
                }

                resolvedValue = ints;
                resolvedElement = JsonSerializer.SerializeToElement(ints);
                return true;
            case ParameterType.DoubleArray:
                if (element.ValueKind != JsonValueKind.Array)
                {
                    error = "Expected array.";
                    return false;
                }

                List<double> doubles = new();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out double value))
                    {
                        error = "Expected double array.";
                        return false;
                    }

                    doubles.Add(value);
                }

                resolvedValue = doubles;
                resolvedElement = JsonSerializer.SerializeToElement(doubles);
                return true;
            case ParameterType.BooleanArray:
                if (element.ValueKind != JsonValueKind.Array)
                {
                    error = "Expected array.";
                    return false;
                }

                List<bool> bools = new();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.True && item.ValueKind != JsonValueKind.False)
                    {
                        error = "Expected boolean array.";
                        return false;
                    }

                    bools.Add(item.GetBoolean());
                }

                resolvedValue = bools;
                resolvedElement = JsonSerializer.SerializeToElement(bools);
                return true;
            default:
                error = "Unsupported parameter type.";
                return false;
        }
    }

    private static bool TryConvertFromString(
        string value,
        ParameterType parameterType,
        string[]? enumValues,
        out object? resolvedValue,
        out JsonElement resolvedElement,
        out string? error)
    {
        resolvedValue = null;
        resolvedElement = default;
        error = null;

        switch (parameterType)
        {
            case ParameterType.String:
            case ParameterType.Path:
            case ParameterType.File:
            case ParameterType.Folder:
            case ParameterType.Enum:
                if (parameterType == ParameterType.Enum && enumValues is not null && !enumValues.Contains(value, StringComparer.Ordinal))
                {
                    error = "Enum value is not allowed.";
                    return false;
                }

                resolvedValue = value;
                resolvedElement = JsonSerializer.SerializeToElement(value);
                return true;
            case ParameterType.Int:
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
                {
                    error = "EnvRef value is not an int.";
                    return false;
                }

                resolvedValue = intValue;
                resolvedElement = JsonSerializer.SerializeToElement(intValue);
                return true;
            case ParameterType.Double:
                if (!double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double doubleValue))
                {
                    error = "EnvRef value is not a double.";
                    return false;
                }

                resolvedValue = doubleValue;
                resolvedElement = JsonSerializer.SerializeToElement(doubleValue);
                return true;
            case ParameterType.Boolean:
                if (TryParseBoolean(value, out bool boolValue))
                {
                    resolvedValue = boolValue;
                    resolvedElement = JsonSerializer.SerializeToElement(boolValue);
                    return true;
                }

                error = "EnvRef value is not a boolean.";
                return false;
            case ParameterType.StringArray:
            case ParameterType.PathArray:
            case ParameterType.FileArray:
            case ParameterType.FolderArray:
            case ParameterType.EnumArray:
                if (!TryParseJsonArray(value, item =>
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        error = "EnvRef array contains non-string.";
                        return false;
                    }

                    string? itemValue = item.GetString();
                    if (parameterType == ParameterType.EnumArray && enumValues is not null && itemValue is not null && !enumValues.Contains(itemValue, StringComparer.Ordinal))
                    {
                        error = "Enum array contains invalid value.";
                        return false;
                    }

                    return true;
                }, out List<string> stringValues, out error, out JsonElement parsedElement, out object? parsed))
                {
                    return false;
                }

                resolvedValue = parsed ?? stringValues;
                resolvedElement = parsedElement;
                return true;
            case ParameterType.IntArray:
                if (!TryParseJsonArray(value, item => item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out _), out List<int> intValues, out error, out JsonElement parsedElementInt, out object? parsedInt))
                {
                    return false;
                }

                resolvedValue = parsedInt ?? intValues;
                resolvedElement = parsedElementInt;
                return true;
            case ParameterType.DoubleArray:
                if (!TryParseJsonArray(value, item => item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out _), out List<double> doubleValues, out error, out JsonElement parsedElementDouble, out object? parsedDouble))
                {
                    return false;
                }

                resolvedValue = parsedDouble ?? doubleValues;
                resolvedElement = parsedElementDouble;
                return true;
            case ParameterType.BooleanArray:
                if (!TryParseJsonArray(value, item => item.ValueKind == JsonValueKind.True || item.ValueKind == JsonValueKind.False, out List<bool> boolValues, out error, out JsonElement parsedElementBool, out object? parsedBool))
                {
                    return false;
                }

                resolvedValue = parsedBool ?? boolValues;
                resolvedElement = parsedElementBool;
                return true;
            default:
                error = "Unsupported parameter type.";
                return false;
        }

        bool TryParseJsonArray<T>(
            string raw,
            Func<JsonElement, bool> validator,
            out List<T> values,
            out string? parseError,
            out JsonElement element,
            out object? parsed)
        {
            values = new List<T>();
            parseError = null;
            element = default;
            parsed = null;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    parseError = "EnvRef array must be a JSON array.";
                    return false;
                }

                foreach (JsonElement item in doc.RootElement.EnumerateArray())
                {
                    if (!validator(item))
                    {
                        parseError = "EnvRef array element is invalid.";
                        return false;
                    }
                }

                element = doc.RootElement.Clone();
                parsed = element.Deserialize<List<T>>();
                if (parsed is List<T> typed)
                {
                    values = typed;
                }

                return true;
            }
            catch (Exception ex)
            {
                parseError = ex.Message;
                return false;
            }
        }
    }

    private static bool TryParseBoolean(string value, out bool result)
    {
        if (bool.TryParse(value, out result))
        {
            return true;
        }

        if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }
}
