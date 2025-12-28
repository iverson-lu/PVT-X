using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcTest.Contracts.Models;

public sealed class RunRequest
{
    [JsonPropertyName("suite")]
    public string? Suite { get; set; }

    [JsonPropertyName("plan")]
    public string? Plan { get; set; }

    [JsonPropertyName("testCase")]
    public string? TestCase { get; set; }

    [JsonPropertyName("nodeOverrides")]
    public Dictionary<string, NodeOverride>? NodeOverrides { get; set; }

    [JsonPropertyName("caseInputs")]
    public Dictionary<string, JsonElement>? CaseInputs { get; set; }

    [JsonPropertyName("environmentOverrides")]
    public EnvironmentOverrides? EnvironmentOverrides { get; set; }
}

public sealed class NodeOverride
{
    [JsonPropertyName("inputs")]
    public Dictionary<string, JsonElement>? Inputs { get; set; }
}

public sealed class EnvironmentOverrides
{
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }
}
