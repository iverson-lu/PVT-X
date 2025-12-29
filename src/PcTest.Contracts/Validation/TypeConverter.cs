using System.Globalization;
using System.Text.Json;

namespace PcTest.Contracts.Validation;

/// <summary>
/// Type conversion utilities for parameter values per spec section 6.2 and 7.4.
/// </summary>
public static class TypeConverter
{
    private static readonly StringComparer BoolComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Converts a JsonElement to the specified type.
    /// </summary>
    public static (bool Success, object? Value, string? Error) ConvertJsonElement(
        JsonElement element,
        string targetType,
        IReadOnlyList<string>? enumValues = null)
    {
        try
        {
            return targetType switch
            {
                ParameterTypes.Int => ConvertToInt(element),
                ParameterTypes.Double => ConvertToDouble(element),
                ParameterTypes.String => ConvertToString(element),
                ParameterTypes.Boolean => ConvertToBoolean(element),
                ParameterTypes.Path or ParameterTypes.File or ParameterTypes.Folder => ConvertToString(element),
                ParameterTypes.Enum => ConvertToEnum(element, enumValues),
                ParameterTypes.IntArray => ConvertToIntArray(element),
                ParameterTypes.DoubleArray => ConvertToDoubleArray(element),
                ParameterTypes.StringArray => ConvertToStringArray(element),
                ParameterTypes.BooleanArray => ConvertToBooleanArray(element),
                ParameterTypes.PathArray or ParameterTypes.FileArray or ParameterTypes.FolderArray => ConvertToStringArray(element),
                ParameterTypes.EnumArray => ConvertToEnumArray(element, enumValues),
                _ => (false, null, $"Unsupported type: {targetType}")
            };
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    /// <summary>
    /// Converts a string (from environment variable) to the specified type.
    /// Per spec section 7.4:
    /// - int/double: invariant culture
    /// - boolean: true/false/1/0 case-insensitive
    /// - arrays: JSON array format
    /// </summary>
    public static (bool Success, object? Value, string? Error) ConvertString(
        string value,
        string targetType,
        IReadOnlyList<string>? enumValues = null)
    {
        try
        {
            return targetType switch
            {
                ParameterTypes.Int => ParseInt(value),
                ParameterTypes.Double => ParseDouble(value),
                ParameterTypes.String => (true, value, null),
                ParameterTypes.Boolean => ParseBoolean(value),
                ParameterTypes.Path or ParameterTypes.File or ParameterTypes.Folder => (true, value, null),
                ParameterTypes.Enum => ValidateEnum(value, enumValues),
                ParameterTypes.IntArray => ParseIntArray(value),
                ParameterTypes.DoubleArray => ParseDoubleArray(value),
                ParameterTypes.StringArray => ParseStringArray(value),
                ParameterTypes.BooleanArray => ParseBooleanArray(value),
                ParameterTypes.PathArray or ParameterTypes.FileArray or ParameterTypes.FolderArray => ParseStringArray(value),
                ParameterTypes.EnumArray => ParseEnumArray(value, enumValues),
                _ => (true, value, null) // Treat unknown as string
            };
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private static (bool, object?, string?) ConvertToInt(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var intVal))
            return (true, intVal, null);
        if (element.ValueKind == JsonValueKind.String)
            return ParseInt(element.GetString() ?? "");
        return (false, null, $"Cannot convert {element.ValueKind} to int");
    }

    private static (bool, object?, string?) ConvertToDouble(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var doubleVal))
            return (true, doubleVal, null);
        if (element.ValueKind == JsonValueKind.String)
            return ParseDouble(element.GetString() ?? "");
        return (false, null, $"Cannot convert {element.ValueKind} to double");
    }

    private static (bool, object?, string?) ConvertToString(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return (true, element.GetString(), null);
        // Accept other types as their string representation
        return (true, element.ToString(), null);
    }

    private static (bool, object?, string?) ConvertToBoolean(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.True)
            return (true, true, null);
        if (element.ValueKind == JsonValueKind.False)
            return (true, false, null);
        if (element.ValueKind == JsonValueKind.String)
            return ParseBoolean(element.GetString() ?? "");
        if (element.ValueKind == JsonValueKind.Number)
        {
            if (element.TryGetInt32(out var intVal))
            {
                if (intVal == 1) return (true, true, null);
                if (intVal == 0) return (true, false, null);
            }
        }
        return (false, null, $"Cannot convert {element.ValueKind} to boolean");
    }

    private static (bool, object?, string?) ConvertToEnum(JsonElement element, IReadOnlyList<string>? enumValues)
    {
        if (element.ValueKind != JsonValueKind.String)
            return (false, null, $"Enum value must be a string, got {element.ValueKind}");
        return ValidateEnum(element.GetString() ?? "", enumValues);
    }

    private static (bool, object?, string?) ConvertToIntArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return (false, null, $"Expected array, got {element.ValueKind}");

        var list = new List<int>();
        foreach (var item in element.EnumerateArray())
        {
            var (success, value, error) = ConvertToInt(item);
            if (!success)
                return (false, null, error);
            list.Add((int)value!);
        }
        return (true, list.ToArray(), null);
    }

    private static (bool, object?, string?) ConvertToDoubleArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return (false, null, $"Expected array, got {element.ValueKind}");

        var list = new List<double>();
        foreach (var item in element.EnumerateArray())
        {
            var (success, value, error) = ConvertToDouble(item);
            if (!success)
                return (false, null, error);
            list.Add((double)value!);
        }
        return (true, list.ToArray(), null);
    }

    private static (bool, object?, string?) ConvertToStringArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return (false, null, $"Expected array, got {element.ValueKind}");

        var list = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            var (success, value, error) = ConvertToString(item);
            if (!success)
                return (false, null, error);
            list.Add((string?)value ?? "");
        }
        return (true, list.ToArray(), null);
    }

    private static (bool, object?, string?) ConvertToBooleanArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return (false, null, $"Expected array, got {element.ValueKind}");

        var list = new List<bool>();
        foreach (var item in element.EnumerateArray())
        {
            var (success, value, error) = ConvertToBoolean(item);
            if (!success)
                return (false, null, error);
            list.Add((bool)value!);
        }
        return (true, list.ToArray(), null);
    }

    private static (bool, object?, string?) ConvertToEnumArray(JsonElement element, IReadOnlyList<string>? enumValues)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return (false, null, $"Expected array, got {element.ValueKind}");

        var list = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            var (success, value, error) = ConvertToEnum(item, enumValues);
            if (!success)
                return (false, null, error);
            list.Add((string?)value ?? "");
        }
        return (true, list.ToArray(), null);
    }

    private static (bool, object?, string?) ParseInt(string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            return (true, result, null);
        return (false, null, $"Cannot parse '{value}' as int");
    }

    private static (bool, object?, string?) ParseDouble(string value)
    {
        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var result))
            return (true, result, null);
        return (false, null, $"Cannot parse '{value}' as double");
    }

    private static (bool, object?, string?) ParseBoolean(string value)
    {
        if (BoolComparer.Equals(value, "true") || value == "1")
            return (true, true, null);
        if (BoolComparer.Equals(value, "false") || value == "0")
            return (true, false, null);
        return (false, null, $"Cannot parse '{value}' as boolean. Expected: true/false/1/0");
    }

    private static (bool, object?, string?) ValidateEnum(string value, IReadOnlyList<string>? enumValues)
    {
        if (enumValues is null || enumValues.Count == 0)
            return (true, value, null); // No validation if enumValues not specified
        if (enumValues.Contains(value))
            return (true, value, null);
        return (false, null, $"Value '{value}' is not in allowed values: [{string.Join(", ", enumValues)}]");
    }

    private static (bool, object?, string?) ParseIntArray(string value)
    {
        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(value);
            return ConvertToIntArray(element);
        }
        catch (JsonException ex)
        {
            return (false, null, $"Cannot parse as JSON int array: {ex.Message}");
        }
    }

    private static (bool, object?, string?) ParseDoubleArray(string value)
    {
        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(value);
            return ConvertToDoubleArray(element);
        }
        catch (JsonException ex)
        {
            return (false, null, $"Cannot parse as JSON double array: {ex.Message}");
        }
    }

    private static (bool, object?, string?) ParseStringArray(string value)
    {
        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(value);
            return ConvertToStringArray(element);
        }
        catch (JsonException ex)
        {
            return (false, null, $"Cannot parse as JSON string array: {ex.Message}");
        }
    }

    private static (bool, object?, string?) ParseBooleanArray(string value)
    {
        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(value);
            return ConvertToBooleanArray(element);
        }
        catch (JsonException ex)
        {
            return (false, null, $"Cannot parse as JSON boolean array: {ex.Message}");
        }
    }

    private static (bool, object?, string?) ParseEnumArray(string value, IReadOnlyList<string>? enumValues)
    {
        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(value);
            return ConvertToEnumArray(element, enumValues);
        }
        catch (JsonException ex)
        {
            return (false, null, $"Cannot parse as JSON enum array: {ex.Message}");
        }
    }
}
