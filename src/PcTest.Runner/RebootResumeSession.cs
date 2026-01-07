using System.Text.Json;
using PcTest.Contracts.Requests;

namespace PcTest.Runner;

public sealed class RebootResumeSession
{
    public string RunId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? CurrentCaseId { get; set; }
    public int? ExecutionIndex { get; set; }
    public int NextPhase { get; set; }
    public string ResumeToken { get; set; } = string.Empty;
    public int ResumeCount { get; set; }
    public string State { get; set; } = string.Empty;
    public string RunFolder { get; set; } = string.Empty;
    public string RunsRoot { get; set; } = string.Empty;
    public string? CasesRoot { get; set; }
    public string? SuitesRoot { get; set; }
    public string? PlansRoot { get; set; }
    public Dictionary<string, JsonElement>? CaseInputs { get; set; }
    public EnvironmentOverrides? EnvironmentOverrides { get; set; }
}
