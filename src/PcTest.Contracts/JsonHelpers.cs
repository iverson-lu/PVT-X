using System.Text;
using System.Text.Json;

namespace PcTest.Contracts;

public static class JsonHelpers
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static T ReadJsonFile<T>(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        var reader = new Utf8JsonReader(data, isFinalBlock: true, state: default);
        return JsonSerializer.Deserialize<T>(ref reader, SerializerOptions) ?? throw new InvalidDataException($"Failed to deserialize {path}.");
    }

    public static async Task<T> ReadJsonFileAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        T? value = await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        return value ?? throw new InvalidDataException($"Failed to deserialize {path}.");
    }

    public static async Task WriteJsonFileAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static void WriteJsonFile<T>(string path, T value)
    {
        string json = JsonSerializer.Serialize(value, SerializerOptions);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
