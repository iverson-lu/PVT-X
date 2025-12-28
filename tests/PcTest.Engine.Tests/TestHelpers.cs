using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine.Tests;

public static class TestHelpers
{
    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "pctest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        JsonUtils.WriteJsonFile(path, value);
    }

    public static Dictionary<string, JsonElement> InputsFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.Clone();
        }

        return dict;
    }
}
