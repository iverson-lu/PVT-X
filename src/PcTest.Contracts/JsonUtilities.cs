using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public static class JsonUtilities
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static T ReadFile<T>(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, SerializerOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize {path}.");
    }

    public static void WriteFile<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        File.WriteAllText(path, json, Utf8NoBom);
    }

    public static void AppendJsonLine(string path, object value)
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        File.AppendAllText(path, json + Environment.NewLine, Utf8NoBom);
    }
}
