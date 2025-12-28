using System.Text;
using System.Text.Json;

namespace PcTest.Contracts;

public static class JsonFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static T Read<T>(string path)
    {
        string json = File.ReadAllText(path, Utf8NoBom);
        T? value = JsonSerializer.Deserialize<T>(json, JsonDefaults.Options);
        if (value is null)
        {
            throw new InvalidDataException($"Failed to deserialize {path}.");
        }

        return value;
    }

    public static JsonDocument ReadDocument(string path)
    {
        string json = File.ReadAllText(path, Utf8NoBom);
        return JsonDocument.Parse(json);
    }

    public static void Write<T>(string path, T value)
    {
        string json = JsonSerializer.Serialize(value, JsonDefaults.Options);
        File.WriteAllText(path, json, Utf8NoBom);
    }

    public static void WriteUtf8(string path, ReadOnlySpan<byte> utf8)
    {
        File.WriteAllBytes(path, utf8.ToArray());
    }
}
