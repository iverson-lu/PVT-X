using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts;

/// <summary>
/// JSON serialization options for the PC Test System.
/// All JSON read/write MUST be strict UTF-8 and deterministic.
/// </summary>
public static class JsonDefaults
{
    private static readonly JsonSerializerOptions _readOptions;
    private static readonly JsonSerializerOptions _writeOptions;

    static JsonDefaults()
    {
        _readOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        _writeOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
    }

    public static JsonSerializerOptions ReadOptions => _readOptions;
    public static JsonSerializerOptions WriteOptions => _writeOptions;

    /// <summary>
    /// Deserialize from UTF-8 JSON bytes.
    /// </summary>
    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Json)
    {
        return JsonSerializer.Deserialize<T>(utf8Json, _readOptions);
    }

    /// <summary>
    /// Deserialize from string.
    /// </summary>
    public static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, _readOptions);
    }

    /// <summary>
    /// Serialize to UTF-8 JSON bytes.
    /// </summary>
    public static byte[] SerializeToUtf8Bytes<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, _writeOptions);
    }

    /// <summary>
    /// Serialize to string.
    /// </summary>
    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, _writeOptions);
    }
}
