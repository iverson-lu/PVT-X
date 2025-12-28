using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public static class JsonUtilities
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static T ReadJsonFile<T>(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        return JsonSerializer.Deserialize<T>(data, SerializerOptions) ?? throw new InvalidDataException($"Failed to deserialize {path}.");
    }

    public static void WriteJsonFile<T>(string path, T value)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        File.WriteAllBytes(path, data);
    }

    public static void AppendJsonLine<T>(string path, T value)
    {
        string line = JsonSerializer.Serialize(value, SerializerOptions);
        using var stream = new FileStream(path, File.Exists(path) ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.WriteLine(line);
    }
}
