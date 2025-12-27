using System.Text.Json;

namespace PcTest.Contracts;

public static class JsonUtilities
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static Dictionary<string, object?> DeserializeInputs(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind == JsonValueKind.Undefined || element.Value.ValueKind == JsonValueKind.Null)
        {
            return new Dictionary<string, object?>();
        }

        if (element.Value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Inputs must be an object.");
        }

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(element.Value.GetRawText(), SerializerOptions)
               ?? new Dictionary<string, object?>();
    }
}
