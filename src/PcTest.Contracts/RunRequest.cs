using System.Text.Json.Serialization;

namespace PcTest.Contracts;

public sealed record EnvironmentOverrides
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; init; }
}

public sealed record NodeOverride
{
    [JsonPropertyName("inputs")]
    public Dictionary<string, object?>? Inputs { get; init; }
}

public sealed record RunRequest
{
    [JsonPropertyName("suite")]
    public string? Suite { get; init; }

    [JsonPropertyName("testCase")]
    public string? TestCase { get; init; }

    [JsonPropertyName("plan")]
    public string? Plan { get; init; }

    [JsonPropertyName("nodeOverrides")]
    public Dictionary<string, NodeOverride>? NodeOverrides { get; init; }

    [JsonPropertyName("caseInputs")]
    public Dictionary<string, object?>? CaseInputs { get; init; }

    [JsonPropertyName("environmentOverrides")]
    public EnvironmentOverrides? EnvironmentOverrides { get; init; }
}
