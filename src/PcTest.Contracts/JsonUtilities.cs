using System.Text;
using System.Text.Json;

namespace PcTest.Contracts;

public static class JsonUtilities
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static T ReadJson<T>(string path)
    {
        using var stream = File.OpenRead(path);
        var value = JsonSerializer.Deserialize<T>(stream, SerializerOptions);
        if (value is null)
        {
            throw new InvalidOperationException($"Failed to deserialize {path}.");
        }
        return value;
    }

    public static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
        JsonSerializer.Serialize(writer, value, SerializerOptions);
        writer.Flush();
    }

    public static void WriteJsonLine<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        writer.WriteLine(json);
    }
}
