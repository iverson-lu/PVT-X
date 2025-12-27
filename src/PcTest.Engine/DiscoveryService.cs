using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine;

public sealed class DiscoveryService
{
    public DiscoveryResult Discover(ResolvedRoots roots)
    {
        var cases = DiscoverManifests<TestCaseManifest>(roots.TestCaseRoot, "test.manifest.json");
        var suites = DiscoverManifests<TestSuiteManifest>(roots.TestSuiteRoot, "suite.manifest.json");
        var plans = DiscoverManifests<TestPlanManifest>(roots.TestPlanRoot, "plan.manifest.json");

        return new DiscoveryResult(cases, suites, plans);
    }

    private static List<ManifestEntry<T>> DiscoverManifests<T>(string root, string fileName)
    {
        var results = new List<ManifestEntry<T>>();
        if (!Directory.Exists(root))
        {
            return results;
        }

        foreach (var path in Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories))
        {
            using var stream = File.OpenRead(path);
            var manifest = JsonSerializer.Deserialize<T>(stream, JsonDefaults.Options);
            if (manifest is null)
            {
                throw new InvalidOperationException($"{ErrorCodes.ManifestInvalid}: {path}");
            }

            ValidateManifest(path, manifest);
            results.Add(new ManifestEntry<T>(path, manifest));
        }

        EnsureUniqueIds(results);
        return results;
    }

    private static void EnsureUniqueIds<T>(List<ManifestEntry<T>> results)
    {
        var conflicts = results
            .GroupBy(entry => entry.IdVersion, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .ToList();

        if (conflicts.Count == 0)
        {
            return;
        }

        var payload = conflicts.Select(group => new
        {
            idVersion = group.Key,
            conflictPaths = group.Select(item => item.Path).ToArray()
        });

        throw new InvalidOperationException(JsonSerializer.Serialize(new
        {
            code = ErrorCodes.DiscoveryDuplicateId,
            payload
        }, JsonDefaults.Options));
    }

    private static void ValidateManifest<T>(string path, T manifest)
    {
        switch (manifest)
        {
            case TestCaseManifest testCase:
                RequireField(testCase.Id, path, nameof(testCase.Id));
                RequireField(testCase.Version, path, nameof(testCase.Version));
                RequireField(testCase.Script, path, nameof(testCase.Script));
                break;
            case TestSuiteManifest suite:
                RequireField(suite.Id, path, nameof(suite.Id));
                RequireField(suite.Version, path, nameof(suite.Version));
                if (suite.Nodes.Count == 0)
                {
                    throw new InvalidOperationException($"{ErrorCodes.ManifestInvalid}: {path} missing nodes");
                }
                break;
            case TestPlanManifest plan:
                RequireField(plan.Id, path, nameof(plan.Id));
                RequireField(plan.Version, path, nameof(plan.Version));
                if (plan.Suites.Count == 0)
                {
                    throw new InvalidOperationException($"{ErrorCodes.ManifestInvalid}: {path} missing suites");
                }
                break;
        }
    }

    private static void RequireField(string value, string path, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{ErrorCodes.ManifestInvalid}: {path} missing {field}");
        }
    }
}

public sealed record ManifestEntry<T>(string Path, T Manifest)
{
    public string IdVersion => Manifest switch
    {
        TestCaseManifest m => IdVersion.Format(m.Id, m.Version),
        TestSuiteManifest m => IdVersion.Format(m.Id, m.Version),
        TestPlanManifest m => IdVersion.Format(m.Id, m.Version),
        _ => string.Empty
    };
}

public sealed record DiscoveryResult(
    List<ManifestEntry<TestCaseManifest>> Cases,
    List<ManifestEntry<TestSuiteManifest>> Suites,
    List<ManifestEntry<TestPlanManifest>> Plans);
