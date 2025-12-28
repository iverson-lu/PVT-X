using System.Globalization;
using System.Text.Json;

namespace PcTest.Contracts;

public static class ParameterTypes
{
    public static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "int", "double", "string", "boolean", "path", "file", "folder", "enum",
        "int[]", "double[]", "string[]", "boolean[]", "path[]", "file[]", "folder[]", "enum[]"
    };

    public static object? ConvertLiteral(string type, JsonElement element)
    {
        return type.ToLowerInvariant() switch
        {
            "int" => element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var intVal) ? intVal : ParseInt(element),
            "double" => element.ValueKind == JsonValueKind.Number ? element.GetDouble() : ParseDouble(element),
            "boolean" => ParseBool(element),
            "string" or "enum" or "path" or "file" or "folder" => element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString(),
            "int[]" => ParseArray(element, ParseInt),
            "double[]" => ParseArray(element, ParseDouble),
            "boolean[]" => ParseArray(element, ParseBool),
            "string[]" or "enum[]" or "path[]" or "file[]" or "folder[]" => ParseArray(element, e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.ToString()),
            _ => element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString()
        };
    }

    public static object? ConvertFromEnv(string type, string value)
    {
        return type.ToLowerInvariant() switch
        {
            "int" => int.Parse(value, CultureInfo.InvariantCulture),
            "double" => double.Parse(value, CultureInfo.InvariantCulture),
            "boolean" => ParseBool(value),
            "string" or "enum" or "path" or "file" or "folder" => value,
            "int[]" => ParseEnvArray(value, e => int.Parse(e.GetRawText(), CultureInfo.InvariantCulture)),
            "double[]" => ParseEnvArray(value, e => double.Parse(e.GetRawText(), CultureInfo.InvariantCulture)),
            "boolean[]" => ParseEnvArray(value, e => ParseBool(e.GetString() ?? e.GetRawText())),
            "string[]" or "enum[]" or "path[]" or "file[]" or "folder[]" => ParseEnvArray(value, e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.GetRawText()),
            _ => value
        };
    }

    private static int ParseInt(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new ValidationException("Parameter.Type.Invalid", new Dictionary<string, object>
        {
            ["expected"] = "int",
            ["actual"] = element.ValueKind.ToString()
        });
    }

    private static double ParseDouble(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String && double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new ValidationException("Parameter.Type.Invalid", new Dictionary<string, object>
        {
            ["expected"] = "double",
            ["actual"] = element.ValueKind.ToString()
        });
    }

    private static bool ParseBool(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => ParseBool(element.GetString() ?? string.Empty),
            _ => throw new ValidationException("Parameter.Type.Invalid", new Dictionary<string, object>
            {
                ["expected"] = "boolean",
                ["actual"] = element.ValueKind.ToString()
            })
        };
    }

    private static bool ParseBool(string value)
    {
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new ValidationException("Parameter.Type.Invalid", new Dictionary<string, object>
        {
            ["expected"] = "boolean",
            ["actual"] = value
        });
    }

    private static List<object?> ParseArray(JsonElement element, Func<JsonElement, object?> parser)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new ValidationException("Parameter.Type.Invalid", new Dictionary<string, object>
            {
                ["expected"] = "array",
                ["actual"] = element.ValueKind.ToString()
            });
        }

        var list = new List<object?>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(parser(item));
        }

        return list;
    }

    private static List<object?> ParseEnvArray(string value, Func<JsonElement, object?> parser)
    {
        using var doc = JsonDocument.Parse(value);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new ValidationException("Parameter.Type.Invalid", new Dictionary<string, object>
            {
                ["expected"] = "array",
                ["actual"] = doc.RootElement.ValueKind.ToString()
            });
        }

        var list = new List<object?>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            list.Add(parser(item));
        }

        return list;
    }
}
