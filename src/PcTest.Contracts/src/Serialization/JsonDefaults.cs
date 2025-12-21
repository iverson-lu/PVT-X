using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts.Serialization;

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
