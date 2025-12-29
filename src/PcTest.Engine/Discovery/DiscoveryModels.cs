using PcTest.Contracts.Manifests;

namespace PcTest.Engine.Discovery;

/// <summary>
/// Discovered entity with manifest and location.
/// </summary>
public sealed class DiscoveredTestCase
{
    public TestCaseManifest Manifest { get; init; } = new();
    public string ManifestPath { get; init; } = string.Empty;
    public string FolderPath { get; init; } = string.Empty;
    public string Identity => Manifest.Identity;
}

public sealed class DiscoveredTestSuite
{
    public TestSuiteManifest Manifest { get; init; } = new();
    public string ManifestPath { get; init; } = string.Empty;
    public string FolderPath { get; init; } = string.Empty;
    public string Identity => Manifest.Identity;
}

public sealed class DiscoveredTestPlan
{
    public TestPlanManifest Manifest { get; init; } = new();
    public string ManifestPath { get; init; } = string.Empty;
    public string FolderPath { get; init; } = string.Empty;
    public string Identity => Manifest.Identity;
}

/// <summary>
/// Discovery result container.
/// </summary>
public sealed class DiscoveryResult
{
    public Dictionary<string, DiscoveredTestCase> TestCases { get; } = new();
    public Dictionary<string, DiscoveredTestSuite> TestSuites { get; } = new();
    public Dictionary<string, DiscoveredTestPlan> TestPlans { get; } = new();

    public string ResolvedTestCaseRoot { get; set; } = string.Empty;
    public string ResolvedTestSuiteRoot { get; set; } = string.Empty;
    public string ResolvedTestPlanRoot { get; set; } = string.Empty;
}
