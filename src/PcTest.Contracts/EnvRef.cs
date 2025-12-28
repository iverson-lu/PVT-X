using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public sealed class EnvRef
{
    [JsonPropertyName("env")]
    public string Env { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("secret")]
    public bool? Secret { get; init; }
}
