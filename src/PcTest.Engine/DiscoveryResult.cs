using PcTest.Contracts;

namespace PcTest.Engine;

public sealed class TestCaseEntry
{
    public TestCaseEntry(string manifestPath, TestCaseManifest manifest)
    {
        ManifestPath = manifestPath;
        Manifest = manifest;
        Identity = new Identity(manifest.Id, manifest.Version);
    }

    public string ManifestPath { get; }
    public TestCaseManifest Manifest { get; }
    public Identity Identity { get; }
}

public sealed class TestSuiteEntry
{
    public TestSuiteEntry(string manifestPath, TestSuiteManifest manifest)
    {
        ManifestPath = manifestPath;
        Manifest = manifest;
        Identity = new Identity(manifest.Id, manifest.Version);
    }

    public string ManifestPath { get; }
    public TestSuiteManifest Manifest { get; }
    public Identity Identity { get; }
}

public sealed class TestPlanEntry
{
    public TestPlanEntry(string manifestPath, TestPlanManifest manifest)
    {
        ManifestPath = manifestPath;
        Manifest = manifest;
        Identity = new Identity(manifest.Id, manifest.Version);
    }

    public string ManifestPath { get; }
    public TestPlanManifest Manifest { get; }
    public Identity Identity { get; }
}

public sealed class DiscoveryResult
{
    public DiscoveryResult(
        IReadOnlyDictionary<Identity, TestCaseEntry> testCases,
        IReadOnlyDictionary<Identity, TestSuiteEntry> testSuites,
        IReadOnlyDictionary<Identity, TestPlanEntry> testPlans)
    {
        TestCases = testCases;
        TestSuites = testSuites;
        TestPlans = testPlans;
    }

    public IReadOnlyDictionary<Identity, TestCaseEntry> TestCases { get; }
    public IReadOnlyDictionary<Identity, TestSuiteEntry> TestSuites { get; }
    public IReadOnlyDictionary<Identity, TestPlanEntry> TestPlans { get; }
}
