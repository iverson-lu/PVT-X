using PcTest.Contracts;
using System.Text.Json;

namespace PcTest.Engine;

public sealed class DiscoveryResult
{
    public required Dictionary<Identity, string> TestCases { get; init; }
    public required Dictionary<Identity, string> TestSuites { get; init; }
    public required Dictionary<Identity, string> TestPlans { get; init; }
}

public sealed class DiscoveryService
{
    public DiscoveryResult Discover(string testCaseRoot, string testSuiteRoot, string testPlanRoot)
    {
        var testCases = DiscoverManifests<TestCaseManifest>(testCaseRoot, "test.manifest.json", "TestCase");
        var suites = DiscoverManifests<TestSuiteManifest>(testSuiteRoot, "suite.manifest.json", "TestSuite");
        var plans = DiscoverManifests<TestPlanManifest>(testPlanRoot, "plan.manifest.json", "TestPlan");

        return new DiscoveryResult
        {
            TestCases = testCases,
            TestSuites = suites,
            TestPlans = plans
        };
    }

    private Dictionary<Identity, string> DiscoverManifests<T>(string root, string fileName, string entityType)
    {
        var result = new Dictionary<Identity, string>();
        var conflicts = new Dictionary<Identity, List<string>>();

        if (!Directory.Exists(root))
        {
            return result;
        }

        foreach (var path in Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories))
        {
            var manifest = JsonUtilities.ReadFile<T>(path);
            var identity = manifest switch
            {
                TestCaseManifest tc => new Identity(tc.Id, tc.Version),
                TestSuiteManifest ts => new Identity(ts.Id, ts.Version),
                TestPlanManifest tp => new Identity(tp.Id, tp.Version),
                _ => throw new InvalidOperationException("Unknown manifest type.")
            };

            if (!result.TryAdd(identity, path))
            {
                if (!conflicts.TryGetValue(identity, out var list))
                {
                    list = [result[identity]];
                    conflicts[identity] = list;
                }

                list.Add(path);
            }
        }

        if (conflicts.Count > 0)
        {
            var first = conflicts.First();
            throw new PcTestException("Identity.Duplicate", "Duplicate identity discovered.",
                ErrorPayload.IdentityConflict(entityType, first.Key.Id, first.Key.Version, first.Value));
        }

        return result;
    }
}

public static class SuiteRefResolver
{
    public static string ResolveTestCaseRef(string suitePath, string testCaseRoot, string refValue)
    {
        var candidate = Path.Combine(testCaseRoot, refValue, "test.manifest.json");
        var normalized = PathUtilities.NormalizePath(candidate);

        if (!PathUtilities.IsContained(testCaseRoot, normalized))
        {
            throw new PcTestException("Suite.TestCaseRef.Invalid", "Suite TestCase ref escaped root.",
                ErrorPayload.SuiteRefInvalid(suitePath, refValue, normalized, PathUtilities.NormalizePath(testCaseRoot), "OutOfRoot"));
        }

        if (!File.Exists(normalized))
        {
            var folder = Path.GetDirectoryName(normalized) ?? string.Empty;
            var reason = Directory.Exists(folder) ? "MissingManifest" : "NotFound";
            throw new PcTestException("Suite.TestCaseRef.Invalid", "Suite TestCase ref missing manifest.",
                ErrorPayload.SuiteRefInvalid(suitePath, refValue, normalized, PathUtilities.NormalizePath(testCaseRoot), reason));
        }

        var resolved = PathUtilities.ResolveLinkTarget(normalized);
        if (!PathUtilities.IsContained(testCaseRoot, resolved))
        {
            throw new PcTestException("Suite.TestCaseRef.Invalid", "Suite TestCase ref resolved out of root.",
                ErrorPayload.SuiteRefInvalid(suitePath, refValue, resolved, PathUtilities.NormalizePath(testCaseRoot), "OutOfRoot"));
        }

        return resolved;
    }
}

public sealed class IdentityResolver
{
    public static string ResolvePath(Dictionary<Identity, string> manifestMap, Identity identity, string entityType)
    {
        var matches = manifestMap.Where(kvp => kvp.Key.Id == identity.Id && kvp.Key.Version == identity.Version).ToArray();
        if (matches.Length == 0)
        {
            throw new PcTestException("Identity.ResolveFailed", "Identity not found.",
                ErrorPayload.IdentityResolutionFailed(entityType, identity.Id, identity.Version, "NotFound"));
        }

        if (matches.Length > 1)
        {
            throw new PcTestException("Identity.ResolveFailed", "Identity not unique.",
                ErrorPayload.IdentityResolutionFailed(entityType, identity.Id, identity.Version, "NonUnique", matches.Select(m => m.Value)));
        }

        return matches[0].Value;
    }
}
