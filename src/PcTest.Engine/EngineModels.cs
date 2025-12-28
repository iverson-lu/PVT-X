using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine;

public sealed record DiscoveryOptions
{
    public string TestCaseRoot { get; init; } = string.Empty;
    public string TestSuiteRoot { get; init; } = string.Empty;
    public string TestPlanRoot { get; init; } = string.Empty;
}

public sealed record DiscoveryResult
{
    public List<ResolvedTestCase> TestCases { get; init; } = new();
    public List<ResolvedTestSuite> TestSuites { get; init; } = new();
    public List<ResolvedTestPlan> TestPlans { get; init; } = new();
    public ValidationResult Validation { get; init; } = new();
}

public sealed record RunConfiguration
{
    public string RunsRoot { get; init; } = string.Empty;
    public string TestCaseRoot { get; init; } = string.Empty;
    public string TestSuiteRoot { get; init; } = string.Empty;
    public string TestPlanRoot { get; init; } = string.Empty;
    public string PwshPath { get; init; } = "pwsh";
}

public sealed record SuiteExecutionResult
{
    public string RunId { get; init; } = string.Empty;
    public string RunFolder { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public List<string> ChildRunIds { get; init; } = new();
}

public sealed record PlanExecutionResult
{
    public string RunId { get; init; } = string.Empty;
    public string RunFolder { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public List<string> ChildRunIds { get; init; } = new();
}

public sealed record SuiteNodeResolution
{
    public string NodeId { get; init; } = string.Empty;
    public ResolvedTestCase TestCase { get; init; } = new();
    public string RefPath { get; init; } = string.Empty;
}

public sealed record ManifestSnapshot
{
    public JsonElement SourceManifest { get; init; }
    public JsonElement? InputTemplates { get; init; }
}
