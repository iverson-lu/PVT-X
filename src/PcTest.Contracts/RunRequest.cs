using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public sealed class EnvironmentOverrides
{
    [JsonPropertyName("env")]
    public IReadOnlyDictionary<string, string>? Env { get; init; }
}

public sealed class NodeOverride
{
    [JsonPropertyName("inputs")]
    public IReadOnlyDictionary<string, JsonElement>? Inputs { get; init; }
}

public sealed class RunRequest
{
    [JsonPropertyName("suite")]
    public string? Suite { get; init; }

    [JsonPropertyName("testCase")]
    public string? TestCase { get; init; }

    [JsonPropertyName("plan")]
    public string? Plan { get; init; }

    [JsonPropertyName("caseInputs")]
    public IReadOnlyDictionary<string, JsonElement>? CaseInputs { get; init; }

    [JsonPropertyName("nodeOverrides")]
    public IReadOnlyDictionary<string, NodeOverride>? NodeOverrides { get; init; }

    [JsonPropertyName("environmentOverrides")]
    public EnvironmentOverrides? EnvironmentOverrides { get; init; }
}
