using PcTest.Contracts;
using System.Text.Json;

namespace PcTest.Engine;

public sealed class DiscoveryService
{
    public DiscoveryResult Discover(EngineRoots roots)
    {
        var testCases = DiscoverManifests(roots.ResolvedTestCaseRoot, "test.manifest.json", LoadManifest<TestCaseManifest>)
            .Select(item => new DiscoveredTestCase(new Identity(item.Manifest.Id, item.Manifest.Version), item.Path, item.Manifest))
            .ToList();

        var suites = DiscoverManifests(roots.ResolvedTestSuiteRoot, "suite.manifest.json", LoadManifest<TestSuiteManifest>)
            .Select(item => new DiscoveredSuite(new Identity(item.Manifest.Id, item.Manifest.Version), item.Path, item.Manifest))
            .ToList();

        var plans = DiscoverManifests(roots.ResolvedTestPlanRoot, "plan.manifest.json", LoadPlanManifest)
            .Select(item => new DiscoveredPlan(new Identity(item.Manifest.Id, item.Manifest.Version), item.Path, item.Manifest))
            .ToList();

        EnsureUnique("TestCase", testCases.Select(tc => (tc.Identity, tc.ManifestPath)));
        EnsureUnique("TestSuite", suites.Select(suite => (suite.Identity, suite.ManifestPath)));
        EnsureUnique("TestPlan", plans.Select(plan => (plan.Identity, plan.ManifestPath)));

        return new DiscoveryResult(testCases, suites, plans);
    }

    private static IEnumerable<(string Path, T Manifest)> DiscoverManifests<T>(string root, string fileName, Func<string, T> loader)
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<(string, T)>();
        }

        return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .Select(path => (Path: path, Manifest: loader(path)))
            .ToList();
    }

    private static T LoadManifest<T>(string path)
    {
        var json = File.ReadAllText(path);
        var manifest = JsonSerializer.Deserialize<T>(json, JsonUtilities.SerializerOptions);
        if (manifest == null)
        {
            throw new InvalidOperationException($"Unable to deserialize manifest at {path}.");
        }

        return manifest;
    }

    private static TestPlanManifest LoadPlanManifest(string path)
    {
        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("environment", out var envElement))
        {
            foreach (var property in envElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, "env", StringComparison.OrdinalIgnoreCase))
                {
                    throw new EngineException("Plan.Environment.Invalid", "Plan environment only supports env.", new Dictionary<string, object?>
                    {
                        ["planPath"] = path,
                        ["key"] = property.Name
                    });
                }
            }
        }

        return LoadManifest<TestPlanManifest>(path);
    }

    private static void EnsureUnique(string entityType, IEnumerable<(Identity Identity, string Path)> entries)
    {
        var groups = entries.GroupBy(item => item.Identity.ToString()).Where(group => group.Count() > 1).ToList();
        if (groups.Count == 0)
        {
            return;
        }

        var first = groups[0];
        var identity = first.First().Identity;
        var payload = new Dictionary<string, object?>
        {
            ["entityType"] = entityType,
            ["id"] = identity.Id,
            ["version"] = identity.Version,
            ["conflictPaths"] = first.Select(item => item.Path).ToArray()
        };
        throw new EngineException("Discovery.Identity.NonUnique", $"Duplicate {entityType} identity {identity}", payload);
    }
}
