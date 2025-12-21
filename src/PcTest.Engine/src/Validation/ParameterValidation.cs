using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PcTest.Contracts.Manifest;

namespace PcTest.Engine.Validation;

/// <summary>
/// Provides reusable validation logic for manifest parameter values.
/// </summary>
public static class ParameterValidation
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Converts raw input into a strongly-typed value according to the parameter definition.
    /// </summary>
    /// <param name="definition">Parameter definition.</param>
    /// <param name="raw">Raw string input.</param>
    /// <returns>Typed value.</returns>
    public static object? ConvertFromInput(ParameterDefinition definition, string raw)
    {
        return ConvertValue(definition, raw);
    }

    /// <summary>
    /// Converts a default value to its strongly-typed representation.
    /// </summary>
    /// <param name="definition">Parameter definition.</param>
    /// <returns>Typed default value.</returns>
    public static object? ConvertDefault(ParameterDefinition definition)
    {
        if (definition.Default is null)
        {
            return null;
        }

        if (definition.Default is JsonElement element)
        {
            return ConvertJsonElement(definition, element);
        }

        return ConvertValue(definition, definition.Default.ToString() ?? string.Empty);
    }

    private static object? ConvertJsonElement(ParameterDefinition definition, JsonElement element)
    {
        var type = NormalizeType(definition);
        return type switch
        {
            "string" or "path" or "file" or "folder" => ValidateString(definition, element.GetString() ?? string.Empty),
            "int" => (int)ValidateRange(definition, element.GetInt32()),
            "double" => ValidateRange(definition, element.GetDouble()),
            "bool" => element.GetBoolean(),
            "enum" => ValidateEnum(definition, element.GetString() ?? string.Empty),
            "string[]" => element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().Select(e => ValidateString(definition, e.GetString() ?? string.Empty)).ToArray() : Array.Empty<string>(),
            "int[]" => element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().Select(e => (int)ValidateRange(definition, e.GetInt32())).ToArray() : Array.Empty<int>(),
            "double[]" => element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().Select(e => ValidateRange(definition, e.GetDouble())).ToArray() : Array.Empty<double>(),
            "enum[]" => element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().Select(e => ValidateEnum(definition, e.GetString() ?? string.Empty)).ToArray() : Array.Empty<string>(),
            _ => element.ToString()
        };
    }

    private static object? ConvertValue(ParameterDefinition definition, string raw)
    {
        var type = NormalizeType(definition);
        return type switch
        {
            "string" or "path" or "file" or "folder" => ValidateString(definition, raw),
            "int" => ParseInt(definition, raw),
            "double" => ParseDouble(definition, raw),
            "bool" => ParseBool(definition, raw),
            "enum" => ValidateEnum(definition, raw),
            "string[]" => ParseArray(definition, raw, ValidateString),
            "int[]" => ParseArray(definition, raw, ParseInt),
            "double[]" => ParseArray(definition, raw, ParseDouble),
            "enum[]" => ParseArray(definition, raw, ValidateEnum),
            _ => raw
        };
    }

    private static string NormalizeType(ParameterDefinition definition) => definition.Type.ToLowerInvariant();

    private static object ParseArray(ParameterDefinition definition, string raw, Func<ParameterDefinition, string, object> converter)
    {
        try
        {
            return Split(raw).Select(value => converter(definition, value)).ToArray();
        }
        catch (ValidationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            throw new ValidationException(BuildTypeMessage(definition));
        }
    }

    private static IEnumerable<string> Split(string raw)
    {
        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => v.Trim());
    }

    private static object ValidateEnum(ParameterDefinition definition, string raw)
    {
        if (definition.EnumValues is not null && !definition.EnumValues.Contains(raw, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationException($"Parameter '{definition.Name}' must be one of: {string.Join(", ", definition.EnumValues)}");
        }

        return raw;
    }

    private static string ValidateString(ParameterDefinition definition, string value)
    {
        if (!string.IsNullOrWhiteSpace(definition.Pattern))
        {
            EnsurePattern(definition, value);
        }

        return value;
    }

    private static int ParseInt(ParameterDefinition definition, string raw)
    {
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            throw new ValidationException(BuildTypeMessage(definition));
        }

        return (int)ValidateRange(definition, intValue);
    }

    private static double ParseDouble(ParameterDefinition definition, string raw)
    {
        if (!double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue))
        {
            throw new ValidationException(BuildTypeMessage(definition));
        }

        return ValidateRange(definition, doubleValue);
    }

    private static bool ParseBool(ParameterDefinition definition, string raw)
    {
        if (!bool.TryParse(raw, out var value))
        {
            throw new ValidationException(BuildTypeMessage(definition));
        }

        return value;
    }

    private static double ValidateRange(ParameterDefinition definition, double value)
    {
        if (definition.Min.HasValue && value < definition.Min.Value)
        {
            throw new ValidationException($"Parameter '{definition.Name}' must be >= {definition.Min.Value}.");
        }

        if (definition.Max.HasValue && value > definition.Max.Value)
        {
            throw new ValidationException($"Parameter '{definition.Name}' must be <= {definition.Max.Value}.");
        }

        return value;
    }

    private static void EnsurePattern(ParameterDefinition definition, string value)
    {
        try
        {
            var regex = new Regex(definition.Pattern!, RegexOptions.CultureInvariant, PatternTimeout);
            if (!regex.IsMatch(value))
            {
                throw new ValidationException($"Parameter '{definition.Name}' does not match the required pattern.");
            }
        }
        catch (RegexMatchTimeoutException)
        {
            throw new ValidationException($"Parameter '{definition.Name}' pattern evaluation timed out.");
        }
    }

    private static string BuildTypeMessage(ParameterDefinition definition)
    {
        return $"Parameter '{definition.Name}' expects a value of type '{definition.Type}'.";
    }
}
