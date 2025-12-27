using PcTest.Contracts;

namespace PcTest.Engine;

public sealed record DiscoveredTestCase(Identity Identity, string ManifestPath, TestCaseManifest Manifest);
public sealed record DiscoveredSuite(Identity Identity, string ManifestPath, TestSuiteManifest Manifest);
public sealed record DiscoveredPlan(Identity Identity, string ManifestPath, TestPlanManifest Manifest);

public sealed record DiscoveryResult(
    IReadOnlyList<DiscoveredTestCase> TestCases,
    IReadOnlyList<DiscoveredSuite> Suites,
    IReadOnlyList<DiscoveredPlan> Plans);

public sealed record EngineRoots(
    string ResolvedTestCaseRoot,
    string ResolvedTestSuiteRoot,
    string ResolvedTestPlanRoot,
    string ResolvedRunsRoot);
