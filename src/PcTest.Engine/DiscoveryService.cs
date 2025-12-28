using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine;

public sealed class DiscoveryService
{
    public DiscoveryResult Discover(string testCaseRoot, string testSuiteRoot, string testPlanRoot)
    {
        var testCases = DiscoverTestCases(testCaseRoot);
        var suites = DiscoverSuites(testSuiteRoot);
        var plans = DiscoverPlans(testPlanRoot);
        return new DiscoveryResult(testCases, suites, plans);
    }

    private static IReadOnlyDictionary<Identity, TestCaseEntry> DiscoverTestCases(string root)
    {
        var entries = new Dictionary<Identity, TestCaseEntry>();
        var conflicts = new Dictionary<Identity, List<string>>();
        foreach (string file in Directory.EnumerateFiles(root, "test.manifest.json", SearchOption.AllDirectories))
        {
            var manifest = JsonUtilities.ReadJsonFile<TestCaseManifest>(file);
            ValidateTestCaseManifest(file, manifest);
            var identity = new Identity(manifest.Id, manifest.Version);
            if (entries.ContainsKey(identity))
            {
                if (!conflicts.TryGetValue(identity, out var list))
                {
                    list = new List<string> { entries[identity].ManifestPath };
                    conflicts[identity] = list;
                }
                list.Add(file);
                continue;
            }

            entries[identity] = new TestCaseEntry(file, manifest);
        }

        if (conflicts.Count > 0)
        {
            var first = conflicts.First();
            throw new PcTestException(
                "Discovery.Identity.Duplicate",
                "Duplicate identity found.",
                new Dictionary<string, object?>
                {
                    ["entityType"] = "TestCase",
                    ["id"] = first.Key.Id,
                    ["version"] = first.Key.Version,
                    ["conflictPaths"] = first.Value.ToArray()
                });
        }

        return entries;
    }

    private static IReadOnlyDictionary<Identity, TestSuiteEntry> DiscoverSuites(string root)
    {
        var entries = new Dictionary<Identity, TestSuiteEntry>();
        var conflicts = new Dictionary<Identity, List<string>>();
        foreach (string file in Directory.EnumerateFiles(root, "suite.manifest.json", SearchOption.AllDirectories))
        {
            var manifest = JsonUtilities.ReadJsonFile<TestSuiteManifest>(file);
            ValidateSuiteManifest(file, manifest);
            var identity = new Identity(manifest.Id, manifest.Version);
            if (entries.ContainsKey(identity))
            {
                if (!conflicts.TryGetValue(identity, out var list))
                {
                    list = new List<string> { entries[identity].ManifestPath };
                    conflicts[identity] = list;
                }
                list.Add(file);
                continue;
            }

            entries[identity] = new TestSuiteEntry(file, manifest);
        }

        if (conflicts.Count > 0)
        {
            var first = conflicts.First();
            throw new PcTestException(
                "Discovery.Identity.Duplicate",
                "Duplicate identity found.",
                new Dictionary<string, object?>
                {
                    ["entityType"] = "TestSuite",
                    ["id"] = first.Key.Id,
                    ["version"] = first.Key.Version,
                    ["conflictPaths"] = first.Value.ToArray()
                });
        }

        return entries;
    }

    private static IReadOnlyDictionary<Identity, TestPlanEntry> DiscoverPlans(string root)
    {
        var entries = new Dictionary<Identity, TestPlanEntry>();
        var conflicts = new Dictionary<Identity, List<string>>();
        foreach (string file in Directory.EnumerateFiles(root, "plan.manifest.json", SearchOption.AllDirectories))
        {
            var manifest = ReadPlanManifest(file);
            ValidatePlanManifest(file, manifest);
            var identity = new Identity(manifest.Id, manifest.Version);
            if (entries.ContainsKey(identity))
            {
                if (!conflicts.TryGetValue(identity, out var list))
                {
                    list = new List<string> { entries[identity].ManifestPath };
                    conflicts[identity] = list;
                }
                list.Add(file);
                continue;
            }

            entries[identity] = new TestPlanEntry(file, manifest);
        }

        if (conflicts.Count > 0)
        {
            var first = conflicts.First();
            throw new PcTestException(
                "Discovery.Identity.Duplicate",
                "Duplicate identity found.",
                new Dictionary<string, object?>
                {
                    ["entityType"] = "TestPlan",
                    ["id"] = first.Key.Id,
                    ["version"] = first.Key.Version,
                    ["conflictPaths"] = first.Value.ToArray()
                });
        }

        return entries;
    }

    private static TestPlanManifest ReadPlanManifest(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.TryGetProperty("environment", out var envElement))
        {
            if (envElement.ValueKind != JsonValueKind.Object)
            {
                throw new PcTestException("Plan.Environment.Invalid", "Plan environment must be an object.");
            }

            foreach (var property in envElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, "env", StringComparison.Ordinal))
                {
                    throw new PcTestException("Plan.Environment.Invalid", "Plan environment must contain only env.");
                }
            }
        }

        return JsonUtilities.ReadJsonFile<TestPlanManifest>(path);
    }

    private static void ValidateTestCaseManifest(string path, TestCaseManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.SchemaVersion))
        {
            throw new PcTestException("Manifest.Invalid", $"Missing schemaVersion in {path}.");
        }
        SchemaUtilities.EnsureSchemaVersion(manifest.SchemaVersion, path);

        if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new PcTestException("Manifest.Invalid", $"Missing id/version in {path}.");
        }

        if (manifest.Parameters is not null)
        {
            foreach (var parameter in manifest.Parameters)
            {
                if (string.IsNullOrWhiteSpace(parameter.Name) || string.IsNullOrWhiteSpace(parameter.Type))
                {
                    throw new PcTestException("Manifest.Invalid", $"Parameter name/type required in {path}.");
                }
            }
        }
    }

    private static void ValidateSuiteManifest(string path, TestSuiteManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.SchemaVersion))
        {
            throw new PcTestException("Manifest.Invalid", $"Missing schemaVersion in {path}.");
        }
        SchemaUtilities.EnsureSchemaVersion(manifest.SchemaVersion, path);

        if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new PcTestException("Manifest.Invalid", $"Missing id/version in {path}.");
        }

        var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in manifest.TestCases)
        {
            if (string.IsNullOrWhiteSpace(node.NodeId))
            {
                throw new PcTestException("Manifest.Invalid", $"Suite nodeId required in {path}.");
            }

            if (!nodeIds.Add(node.NodeId))
            {
                throw new PcTestException("Manifest.Invalid", $"Duplicate nodeId {node.NodeId} in {path}.");
            }

            if (string.IsNullOrWhiteSpace(node.Ref))
            {
                throw new PcTestException("Manifest.Invalid", $"Suite ref required in {path}.");
            }
        }

        if (manifest.Environment?.Env is not null)
        {
            foreach (var kvp in manifest.Environment.Env)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    throw new PcTestException("Suite.Environment.Invalid", $"Suite environment key invalid in {path}.");
                }
            }
        }
    }

    private static void ValidatePlanManifest(string path, TestPlanManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.SchemaVersion))
        {
            throw new PcTestException("Manifest.Invalid", $"Missing schemaVersion in {path}.");
        }
        SchemaUtilities.EnsureSchemaVersion(manifest.SchemaVersion, path);

        if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new PcTestException("Manifest.Invalid", $"Missing id/version in {path}.");
        }

        if (manifest.Environment?.Env is not null)
        {
            foreach (var kvp in manifest.Environment.Env)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    throw new PcTestException("Plan.Environment.Invalid", $"Plan environment key invalid in {path}.");
                }
            }
        }

        if (manifest.Suites.Count == 0)
        {
            throw new PcTestException("Manifest.Invalid", $"Plan suites required in {path}.");
        }
    }
}
