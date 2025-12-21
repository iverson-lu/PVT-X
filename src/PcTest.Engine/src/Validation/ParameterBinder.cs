using System.Text.Json;
using PcTest.Contracts.Manifest;

namespace PcTest.Engine.Validation;

public static class ParameterBinder
{
    public static IReadOnlyDictionary<string, BoundParameterValue> Bind(TestManifest manifest, IDictionary<string, string> provided)
    {
        var result = new Dictionary<string, BoundParameterValue>(StringComparer.OrdinalIgnoreCase);
        var definitions = manifest.Parameters ?? Array.Empty<ParameterDefinition>();
        var providedKeys = new HashSet<string>(provided.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var definition in definitions)
        {
            var hasInput = TryGetProvided(provided, definition.Name, out var raw);
            if (!hasInput && definition.Required && definition.Default is null)
            {
                throw new InvalidDataException($"Missing required parameter '{definition.Name}'.");
            }

            if (!hasInput && definition.Default is null)
            {
                continue; // optional and not provided
            }

            var value = hasInput
                ? ConvertValue(raw!, definition)
                : ConvertDefault(definition);

            result[definition.Name] = new BoundParameterValue(definition, value, hasInput || definition.Default is not null);
            providedKeys.RemoveWhere(k => string.Equals(k, definition.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (providedKeys.Count > 0)
        {
            throw new InvalidDataException($"Unknown parameters: {string.Join(", ", providedKeys)}");
        }

        return result;
    }

    private static bool TryGetProvided(IDictionary<string, string> provided, string name, out string? value)
    {
        foreach (var kvp in provided)
        {
            if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static object? ConvertDefault(ParameterDefinition definition)
    {
        if (definition.Default is null)
        {
            return null;
        }

        if (definition.Default is JsonElement element)
        {
            return ConvertJsonElement(element, definition.Type);
        }

        return ConvertValue(definition.Default.ToString() ?? string.Empty, definition);
    }

    private static object ConvertValue(string raw, ParameterDefinition definition)
    {
        var type = definition.Type.ToLowerInvariant();
        return type switch
        {
            "string" or "path" or "file" or "folder" => raw,
            "int" => int.TryParse(raw, out var intValue)
                ? intValue
                : throw new InvalidDataException($"Parameter '{definition.Name}' expects an integer."),
            "double" => double.TryParse(raw, out var doubleValue)
                ? doubleValue
                : throw new InvalidDataException($"Parameter '{definition.Name}' expects a number."),
            "bool" => bool.TryParse(raw, out var boolValue)
                ? boolValue
                : throw new InvalidDataException($"Parameter '{definition.Name}' expects a boolean."),
            "enum" => ConvertEnum(raw, definition),
            "string[]" => Split(raw).ToArray(),
            "int[]" => Split(raw).Select(s => int.Parse(s)).ToArray(),
            "enum[]" => Split(raw).Select(v => ConvertEnum(v, definition)).ToArray(),
            _ => raw
        };
    }

    private static object ConvertJsonElement(JsonElement element, string type)
    {
        return type.ToLowerInvariant() switch
        {
            "string" or "path" or "file" or "folder" => element.GetString() ?? string.Empty,
            "int" => element.GetInt32(),
            "double" => element.GetDouble(),
            "bool" => element.GetBoolean(),
            "enum" => element.GetString() ?? string.Empty,
            "string[]" => element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray() : Array.Empty<string>(),
            "int[]" => element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().Select(e => e.GetInt32()).ToArray() : Array.Empty<int>(),
            "enum[]" => element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray() : Array.Empty<string>(),
            _ => element.ToString()
        };
    }

    private static object ConvertEnum(string raw, ParameterDefinition definition)
    {
        if (definition.EnumValues is not null && !definition.EnumValues.Contains(raw, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Parameter '{definition.Name}' must be one of: {string.Join(", ", definition.EnumValues)}");
        }

        return raw;
    }

    private static IEnumerable<string> Split(string raw)
    {
        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => v.Trim());
    }
}
