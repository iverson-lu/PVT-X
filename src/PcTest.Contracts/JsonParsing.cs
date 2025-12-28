using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public static class JsonParsing
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new InputValueConverter() }
    };

    public static T ReadFile<T>(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var value = JsonSerializer.Deserialize<T>(bytes, SerializerOptions);
        if (value is null)
        {
            throw new InvalidOperationException($"Failed to deserialize {path}.");
        }

        return value;
    }

    public static void WriteDeterministic<T>(string path, T value)
    {
        var options = new JsonSerializerOptions(SerializerOptions)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, options);
        File.WriteAllBytes(path, bytes);
    }

    public static Dictionary<string, InputValue>? ReadInputMap(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.Value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Inputs must be an object.");
        }

        var result = new Dictionary<string, InputValue>(StringComparer.Ordinal);
        foreach (var property in element.Value.EnumerateObject())
        {
            result[property.Name] = ParseInputValue(property.Value);
        }

        return result;
    }

    public static InputValue ParseInputValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("$env", out _))
        {
            var envRef = JsonSerializer.Deserialize<EnvRef>(element, SerializerOptions);
            if (envRef is null)
            {
                throw new JsonException("Invalid EnvRef.");
            }

            return new InputValue(envRef);
        }

        return new InputValue(element);
    }
}
