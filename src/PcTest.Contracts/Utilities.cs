using System.Globalization;
using System.Text.Json;

namespace PcTest.Contracts;

public static class IdentityParser
{
    private static readonly char[] Separator = ['@'];

    public static Identity Parse(string value)
    {
        if (value is null)
        {
            throw new PcTestException("Identity.ParseFailed", "Identity cannot be null.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new PcTestException("Identity.ParseFailed", "Identity cannot be empty.");
        }

        if (trimmed.Any(char.IsWhiteSpace))
        {
            throw new PcTestException("Identity.ParseFailed", "Identity cannot contain whitespace.");
        }

        var parts = trimmed.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            throw new PcTestException("Identity.ParseFailed", "Identity must contain a single '@'.");
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(parts[0], "^[A-Za-z0-9._-]+$"))
        {
            throw new PcTestException("Identity.ParseFailed", "Identity id contains invalid characters.");
        }

        return new Identity(parts[0], parts[1]);
    }
}

public static class EnvironmentResolver
{
    public static Dictionary<string, string> Resolve(EnvironmentDefinition? baseEnvironment, EnvironmentOverrides? overrides)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                resolved[key] = value;
            }
        }

        ApplyEnv(resolved, baseEnvironment?.Env);
        ApplyEnv(resolved, overrides?.Env);
        return resolved;
    }

    private static void ApplyEnv(Dictionary<string, string> target, Dictionary<string, string>? env)
    {
        if (env is null)
        {
            return;
        }

        foreach (var (key, value) in env)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new PcTestException("Environment.InvalidKey", "Environment key cannot be empty.");
            }

            target[key] = value;
        }
    }
}

public static class InputResolver
{
    public static (Dictionary<string, object?> EffectiveInputs, Dictionary<string, JsonElement> InputTemplates, HashSet<string> SecretInputs)
        ResolveInputs(TestCaseManifest testCase, Dictionary<string, JsonElement>? defaults, Dictionary<string, JsonElement>? overrides, Dictionary<string, string> effectiveEnvironment, string? nodeId)
    {
        var definitions = testCase.Parameters ?? Array.Empty<ParameterDefinition>();
        var mergedTemplates = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        void MergeTemplates(Dictionary<string, JsonElement>? source)
        {
            if (source is null)
            {
                return;
            }

            foreach (var (key, value) in source)
            {
                mergedTemplates[key] = value;
            }
        }

        MergeTemplates(defaults);
        MergeTemplates(overrides);

        var effective = new Dictionary<string, object?>(StringComparer.Ordinal);
        var secretInputs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            JsonElement? templateValue = null;
            if (mergedTemplates.TryGetValue(definition.Name, out var overrideValue))
            {
                templateValue = overrideValue;
            }
            else if (definition.Default is { } defValue)
            {
                templateValue = defValue;
            }

            if (templateValue is null || templateValue.Value.ValueKind == JsonValueKind.Undefined)
            {
                if (definition.Required)
                {
                    throw new PcTestException("Input.Required", $"Missing required input {definition.Name}.");
                }

                continue;
            }

            var resolved = ResolveInputValue(definition, templateValue.Value, effectiveEnvironment, nodeId, secretInputs);
            effective[definition.Name] = resolved;
        }

        foreach (var (key, value) in mergedTemplates)
        {
            if (effective.ContainsKey(key))
            {
                continue;
            }

            var resolved = ResolveInputValue(new ParameterDefinition { Name = key, Type = "string", Required = false }, value, effectiveEnvironment, nodeId, secretInputs);
            effective[key] = resolved;
        }

        return (effective, mergedTemplates, secretInputs);
    }

    private static object? ResolveInputValue(ParameterDefinition definition, JsonElement templateValue, Dictionary<string, string> effectiveEnvironment, string? nodeId, HashSet<string> secretInputs)
    {
        if (templateValue.ValueKind == JsonValueKind.Object && templateValue.TryGetProperty("$env", out _))
        {
            var envRef = templateValue.Deserialize<EnvRef>(JsonUtilities.SerializerOptions)
                ?? throw new PcTestException("EnvRef.Invalid", "Failed to parse EnvRef.");

            if (string.IsNullOrWhiteSpace(envRef.Env))
            {
                throw new PcTestException("EnvRef.Invalid", "EnvRef $env must be non-empty.");
            }

            if (!effectiveEnvironment.TryGetValue(envRef.Env, out var rawValue) || string.IsNullOrEmpty(rawValue))
            {
                if (envRef.Default is { } defValue)
                {
                    templateValue = defValue;
                }
                else if (envRef.Required)
                {
                    throw new PcTestException("EnvRef.ResolveFailed", "EnvRef required value missing.", ErrorPayload.EnvRefResolveFailed(definition.Name, nodeId, "Missing"));
                }
                else
                {
                    return null;
                }
            }
            else
            {
                templateValue = JsonSerializer.SerializeToElement(rawValue);
            }

            if (envRef.Secret)
            {
                secretInputs.Add(definition.Name);
            }
        }

        var parsed = ParseLiteral(definition, templateValue);
        return parsed;
    }

    private static object? ParseLiteral(ParameterDefinition definition, JsonElement value)
    {
        var type = definition.Type;
        var enumValues = definition.EnumValues;
        switch (type)
        {
            case "string":
            case "path":
            case "file":
            case "folder":
                if (value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
                return value.ToString();
            case "int":
                return value.ValueKind switch
                {
                    JsonValueKind.Number => value.GetInt32(),
                    JsonValueKind.String => int.Parse(value.GetString() ?? string.Empty, NumberStyles.Integer, CultureInfo.InvariantCulture),
                    _ => throw new PcTestException("Input.InvalidType", $"Input {definition.Name} must be int.")
                };
            case "double":
                return value.ValueKind switch
                {
                    JsonValueKind.Number => value.GetDouble(),
                    JsonValueKind.String => double.Parse(value.GetString() ?? string.Empty, NumberStyles.Float, CultureInfo.InvariantCulture),
                    _ => throw new PcTestException("Input.InvalidType", $"Input {definition.Name} must be double.")
                };
            case "boolean":
                return ParseBoolean(value, definition.Name);
            case "enum":
                var enumValue = value.GetString() ?? value.ToString();
                if (enumValues is not null && !enumValues.Contains(enumValue, StringComparer.Ordinal))
                {
                    throw new PcTestException("Input.EnumInvalid", $"Input {definition.Name} must be one of enum values.");
                }
                return enumValue;
            case "string[]":
            case "path[]":
            case "file[]":
            case "folder[]":
                return ParseArray(value, element => element.GetString() ?? element.ToString());
            case "int[]":
                return ParseArray(value, element => element.ValueKind == JsonValueKind.Number
                    ? element.GetInt32()
                    : int.Parse(element.GetString() ?? string.Empty, NumberStyles.Integer, CultureInfo.InvariantCulture));
            case "double[]":
                return ParseArray(value, element => element.ValueKind == JsonValueKind.Number
                    ? element.GetDouble()
                    : double.Parse(element.GetString() ?? string.Empty, NumberStyles.Float, CultureInfo.InvariantCulture));
            case "boolean[]":
                return ParseArray(value, element => ParseBoolean(element, definition.Name));
            case "enum[]":
                var values = ParseArray(value, element => element.GetString() ?? element.ToString());
                if (enumValues is not null && values.Any(v => !enumValues.Contains(v, StringComparer.Ordinal)))
                {
                    throw new PcTestException("Input.EnumInvalid", $"Input {definition.Name} must be one of enum values.");
                }
                return values;
            default:
                return value.ToString();
        }
    }

    private static bool ParseBoolean(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
        {
            return intValue switch
            {
                1 => true,
                0 => false,
                _ => throw new PcTestException("Input.InvalidType", $"Input {name} must be boolean.")
            };
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(text, "1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(text, "0", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        throw new PcTestException("Input.InvalidType", $"Input {name} must be boolean.");
    }

    private static T[] ParseArray<T>(JsonElement value, Func<JsonElement, T> parseElement)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            using var doc = JsonDocument.Parse(value.GetString() ?? "[]");
            value = doc.RootElement.Clone();
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new PcTestException("Input.InvalidType", "Array input must be JSON array.");
        }

        var list = new List<T>();
        foreach (var element in value.EnumerateArray())
        {
            list.Add(parseElement(element));
        }

        return list.ToArray();
    }
}

public static class PathUtilities
{
    public static string NormalizePath(string path) => Path.GetFullPath(path);

    public static bool IsContained(string rootPath, string candidatePath)
    {
        var root = EnsureTrailingSeparator(NormalizePath(rootPath));
        var candidate = NormalizePath(candidatePath);
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    public static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    public static string ResolveLinkTarget(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var segments = full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = root;

        foreach (var segment in segments)
        {
            var next = Path.Combine(current, segment);
            if (File.Exists(next))
            {
                var info = new FileInfo(next);
                if (info.LinkTarget is not null)
                {
                    var resolved = info.ResolveLinkTarget(true);
                    current = resolved?.FullName ?? next;
                    continue;
                }
            }
            else if (Directory.Exists(next))
            {
                var info = new DirectoryInfo(next);
                if (info.LinkTarget is not null)
                {
                    var resolved = info.ResolveLinkTarget(true);
                    current = resolved?.FullName ?? next;
                    continue;
                }
            }

            current = next;
        }

        return current;
    }
}
