using System.Text.Json;

namespace PcTest.Contracts;

public sealed class RunRequest
{
    public string? Suite { get; set; }
    public string? TestCase { get; set; }
    public string? Plan { get; set; }
    public Dictionary<string, NodeOverride>? NodeOverrides { get; set; }
    public Dictionary<string, JsonElement>? CaseInputs { get; set; }
    public EnvironmentOverrides? EnvironmentOverrides { get; set; }
}

public sealed class NodeOverride
{
    public Dictionary<string, JsonElement>? Inputs { get; set; }
}

public sealed class EnvironmentOverrides
{
    public Dictionary<string, string>? Env { get; set; }
}
