using System.Text.Json;

namespace PcTest.Contracts;

/// <summary>
/// Session checkpoint persisted by the Runner for reboot-resume scenarios.
/// </summary>
public sealed class ResumeSession
{
    public string RunId { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string? CurrentCaseId { get; init; }
    public int NextPhase { get; init; }
    public string ResumeToken { get; init; } = string.Empty;
    public int ResumeCount { get; set; }
    public string State { get; set; } = "PendingResume";

    public Dictionary<string, object?>? EffectiveInputs { get; init; }
    public Dictionary<string, string>? EffectiveEnvironment { get; init; }
    public Dictionary<string, bool>? SecretInputs { get; init; }
    public List<string>? SecretEnvVars { get; init; }
    public Dictionary<string, JsonElement>? InputTemplates { get; init; }
}
