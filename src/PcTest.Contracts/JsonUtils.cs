using System.Text.Json;

namespace PcTest.Contracts;

public static class JsonUtils
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true
    };

    public static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static JsonElement ToJsonElement(object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    public static T ReadFile<T>(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return JsonSerializer.Deserialize<T>(bytes, SerializerOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize {path}.");
    }

    public static JsonElement ReadJsonElementFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    public static void WriteFile(string path, object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        File.WriteAllBytes(path, bytes);
    }

    public static void WriteJsonElementFile(string path, JsonElement value)
    {
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions));
    }
}
