using System.Globalization;
using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine;

public sealed class InputResolutionResult
{
    public InputResolutionResult(
        IReadOnlyDictionary<string, object> effectiveInputs,
        IReadOnlyDictionary<string, object> redactedInputs,
        IReadOnlyDictionary<string, bool> secretInputs,
        IReadOnlyDictionary<string, JsonElement> inputTemplates)
    {
        EffectiveInputs = effectiveInputs;
        RedactedInputs = redactedInputs;
        SecretInputs = secretInputs;
        InputTemplates = inputTemplates;
    }

    public IReadOnlyDictionary<string, object> EffectiveInputs { get; }
    public IReadOnlyDictionary<string, object> RedactedInputs { get; }
    public IReadOnlyDictionary<string, bool> SecretInputs { get; }
    public IReadOnlyDictionary<string, JsonElement> InputTemplates { get; }
}

public static class InputResolver
{
    public static InputResolutionResult ResolveInputs(
        TestCaseManifest testCase,
        IReadOnlyDictionary<string, JsonElement>? baseInputs,
        IReadOnlyDictionary<string, JsonElement>? overrideInputs,
        IReadOnlyDictionary<string, string> effectiveEnvironment)
    {
        var templates = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (baseInputs is not null)
        {
            foreach (var kvp in baseInputs)
            {
                templates[kvp.Key] = kvp.Value;
            }
        }

        if (overrideInputs is not null)
        {
            foreach (var kvp in overrideInputs)
            {
                templates[kvp.Key] = kvp.Value;
            }
        }

        var effectiveInputs = new SortedDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var redactedInputs = new SortedDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var secretInputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var templateSnapshot = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        var parameters = testCase.Parameters ?? Array.Empty<ParameterDefinition>();
        var definedNames = new HashSet<string>(parameters.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        foreach (string key in templates.Keys)
        {
            if (!definedNames.Contains(key))
            {
                throw new PcTestException("Inputs.Unknown", $"Unknown input '{key}'.");
            }
        }

        foreach (var parameter in parameters)
        {
            templateSnapshot[parameter.Name] = templates.TryGetValue(parameter.Name, out var value)
                ? value
                : parameter.Default ?? default;

            if (!templates.TryGetValue(parameter.Name, out var templateValue))
            {
                if (parameter.Default is null)
                {
                    if (parameter.Required)
                    {
                        throw new PcTestException("Inputs.Required", $"Required input '{parameter.Name}' missing.");
                    }
                    continue;
                }

                templateValue = parameter.Default.Value;
            }

            bool isEnvRef = TryParseEnvRef(templateValue, out var envRef);
            object resolved = isEnvRef
                ? ResolveEnvRef(parameter, envRef, effectiveEnvironment)
                : ConvertLiteral(parameter, templateValue);

            effectiveInputs[parameter.Name] = resolved;
            if (isEnvRef && envRef.Secret == true)
            {
                secretInputs[parameter.Name] = true;
                redactedInputs[parameter.Name] = "***";
            }
            else
            {
                redactedInputs[parameter.Name] = resolved;
            }
        }

        return new InputResolutionResult(effectiveInputs, redactedInputs, secretInputs, templateSnapshot);
    }

    private static bool TryParseEnvRef(JsonElement element, out EnvRef envRef)
    {
        envRef = new EnvRef();
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!element.TryGetProperty("env", out var envProp) || envProp.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        if (!element.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        bool? secret = null;
        if (element.TryGetProperty("secret", out var secretProp) && secretProp.ValueKind == JsonValueKind.True)
        {
            secret = true;
        }
        else if (element.TryGetProperty("secret", out secretProp) && secretProp.ValueKind == JsonValueKind.False)
        {
            secret = false;
        }

        envRef = new EnvRef
        {
            Env = envProp.GetString() ?? string.Empty,
            Type = typeProp.GetString() ?? string.Empty,
            Secret = secret
        };

        return true;
    }

    private static object ResolveEnvRef(ParameterDefinition parameter, EnvRef envRef, IReadOnlyDictionary<string, string> environment)
    {
        if (!environment.TryGetValue(envRef.Env, out var value))
        {
            if (parameter.Required)
            {
                throw new PcTestException("EnvRef.Missing", $"EnvRef '{envRef.Env}' not found.");
            }

            return string.Empty;
        }

        return ConvertString(parameter, envRef.Type, value);
    }

    private static object ConvertLiteral(ParameterDefinition parameter, JsonElement element)
    {
        return parameter.Type switch
        {
            ParameterType.Int => element.GetInt32(),
            ParameterType.Double => element.GetDouble(),
            ParameterType.String => element.GetString() ?? string.Empty,
            ParameterType.Boolean => element.GetBoolean(),
            ParameterType.Path or ParameterType.File or ParameterType.Folder => element.GetString() ?? string.Empty,
            ParameterType.Enum => ConvertEnum(parameter, element.GetString() ?? string.Empty),
            ParameterType.IntArray => element.EnumerateArray().Select(x => x.GetInt32()).ToArray(),
            ParameterType.DoubleArray => element.EnumerateArray().Select(x => x.GetDouble()).ToArray(),
            ParameterType.StringArray => element.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray(),
            ParameterType.BooleanArray => element.EnumerateArray().Select(x => x.GetBoolean()).ToArray(),
            ParameterType.PathArray or ParameterType.FileArray or ParameterType.FolderArray => element.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray(),
            ParameterType.EnumArray => element.EnumerateArray().Select(x => ConvertEnum(parameter, x.GetString() ?? string.Empty)).ToArray(),
            _ => throw new PcTestException("Inputs.Type", $"Unsupported parameter type '{parameter.Type}'.")
        };
    }

    private static object ConvertString(ParameterDefinition parameter, string type, string value)
    {
        string normalizedType = string.IsNullOrWhiteSpace(type) ? parameter.Type : type;
        return normalizedType switch
        {
            ParameterType.Int => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            ParameterType.Double => double.Parse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture),
            ParameterType.String => value,
            ParameterType.Boolean => ParseBool(value),
            ParameterType.Path or ParameterType.File or ParameterType.Folder => value,
            ParameterType.Enum => ConvertEnum(parameter, value),
            ParameterType.IntArray => ParseArray(value, el => int.Parse(el.GetString() ?? string.Empty, NumberStyles.Integer, CultureInfo.InvariantCulture)),
            ParameterType.DoubleArray => ParseArray(value, el => double.Parse(el.GetString() ?? string.Empty, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture)),
            ParameterType.StringArray => ParseArray(value, el => el.GetString() ?? string.Empty),
            ParameterType.BooleanArray => ParseArray(value, el => ParseBool(el.GetString() ?? string.Empty)),
            ParameterType.PathArray or ParameterType.FileArray or ParameterType.FolderArray => ParseArray(value, el => el.GetString() ?? string.Empty),
            ParameterType.EnumArray => ParseArray(value, el => ConvertEnum(parameter, el.GetString() ?? string.Empty)),
            _ => throw new PcTestException("EnvRef.Type", $"Unsupported EnvRef type '{normalizedType}'.")
        };
    }

    private static bool ParseBool(string value)
    {
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new PcTestException("EnvRef.Type", $"Invalid boolean '{value}'.");
    }

    private static T[] ParseArray<T>(string value, Func<JsonElement, T> converter)
    {
        using var doc = JsonDocument.Parse(value);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new PcTestException("EnvRef.Type", "Array EnvRef value must be JSON array.");
        }

        return doc.RootElement.EnumerateArray().Select(converter).ToArray();
    }

    private static string ConvertEnum(ParameterDefinition parameter, string value)
    {
        if (parameter.EnumValues is null || parameter.EnumValues.Count == 0)
        {
            throw new PcTestException("Inputs.Enum", $"Enum values missing for '{parameter.Name}'.");
        }

        if (!parameter.EnumValues.Contains(value, StringComparer.Ordinal))
        {
            throw new PcTestException("Inputs.Enum", $"Enum value '{value}' invalid for '{parameter.Name}'.");
        }

        return value;
    }
}
