using System.Text;
using System.Text.Json;

namespace PcTest.Contracts;

public static class JsonUtil
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static T ReadJsonFile<T>(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return JsonSerializer.Deserialize<T>(bytes, SerializerOptions)
               ?? throw new InvalidDataException($"Unable to deserialize JSON from {path}.");
    }

    public static JsonDocument ReadJsonDocument(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow });
    }

    public static void WriteJsonFile<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        WriteUtf8(path, json);
    }

    public static void WriteJsonLines(string path, IEnumerable<string> lines)
    {
        var content = string.Join("\n", lines);
        WriteUtf8(path, content + (lines.Any() ? "\n" : string.Empty));
    }

    public static void AppendJsonLine(string path, object value)
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        File.AppendAllText(path, json + Environment.NewLine, new UTF8Encoding(false));
    }

    private static void WriteUtf8(string path, string content)
    {
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }
}
