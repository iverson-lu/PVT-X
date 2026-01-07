using System.Text.Json;

namespace PcTest.Runner;

public sealed class RebootSession
{
    public string RunId { get; init; } = string.Empty;
    public string EntityType { get; init; } = "TestCase";
    public string EntityId { get; init; } = string.Empty;
    public string? CurrentCaseId { get; init; }
    public int NextPhase { get; init; }
    public string ResumeToken { get; init; } = string.Empty;
    public int ResumeCount { get; set; }
    public string State { get; set; } = "PendingResume";

    public string CaseRunFolder { get; init; } = string.Empty;
    public string RunsRoot { get; init; } = string.Empty;
    public string TestCasePath { get; init; } = string.Empty;
    public string AssetsRoot { get; init; } = string.Empty;
    public string? WorkingDir { get; init; }
    public int? TimeoutSec { get; init; }

    public Dictionary<string, string> EffectiveEnvironment { get; init; } = new();
    public Dictionary<string, JsonElement> EffectiveInputs { get; init; } = new();
    public Dictionary<string, bool> SecretInputs { get; init; } = new();
    public HashSet<string> SecretEnvVars { get; init; } = new();
    public Dictionary<string, JsonElement>? InputTemplates { get; init; }
}
