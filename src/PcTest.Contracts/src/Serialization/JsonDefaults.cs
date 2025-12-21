using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts.Serialization;

/// <summary>
/// Provides shared JSON serialization settings used across the solution.
/// </summary>
public static class JsonDefaults
{
    /// <summary>
    /// Default serializer options for manifest and result payloads.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
