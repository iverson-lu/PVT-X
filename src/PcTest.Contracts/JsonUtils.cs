using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public static class JsonUtils
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static T ReadJsonFile<T>(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return JsonSerializer.Deserialize<T>(bytes, SerializerOptions)
            ?? throw new InvalidDataException($"Failed to deserialize JSON file: {path}");
    }

    public static JsonDocument ReadJsonDocument(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = false
        });
    }

    public static void WriteJsonFile<T>(string path, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        File.WriteAllBytes(path, bytes);
    }

    public static void WriteJsonLines(string path, IEnumerable<object> entries)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        foreach (var entry in entries)
        {
            var json = JsonSerializer.Serialize(entry, SerializerOptions);
            writer.WriteLine(json);
        }
    }

    public static void AppendJsonLine(string path, object entry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var json = JsonSerializer.Serialize(entry, SerializerOptions);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.WriteLine(json);
    }
}
