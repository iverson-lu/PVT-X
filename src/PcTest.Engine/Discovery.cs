using PcTest.Contracts;
using PcTest.Contracts.Models;

namespace PcTest.Engine;

public sealed class DiscoveryResult
{
    public required string TestCaseRoot { get; init; }
    public required string TestSuiteRoot { get; init; }
    public required string TestPlanRoot { get; init; }

    public List<TestCaseDefinition> TestCases { get; } = new();
    public List<TestSuiteDefinition> Suites { get; } = new();
    public List<TestPlanDefinition> Plans { get; } = new();

    public TestCaseDefinition GetTestCase(Identity identity)
    {
        var match = TestCases.SingleOrDefault(x => x.Identity == identity);
        return match ?? throw new EngineException("Identity.NotFound", new { entityType = "TestCase", id = identity.Id, version = identity.Version, reason = "NotFound" });
    }

    public TestSuiteDefinition GetSuite(Identity identity)
    {
        var match = Suites.SingleOrDefault(x => x.Identity == identity);
        return match ?? throw new EngineException("Identity.NotFound", new { entityType = "TestSuite", id = identity.Id, version = identity.Version, reason = "NotFound" });
    }

    public TestPlanDefinition GetPlan(Identity identity)
    {
        var match = Plans.SingleOrDefault(x => x.Identity == identity);
        return match ?? throw new EngineException("Identity.NotFound", new { entityType = "TestPlan", id = identity.Id, version = identity.Version, reason = "NotFound" });
    }
}

public sealed class TestCaseDefinition
{
    public required Identity Identity { get; init; }
    public required string ManifestPath { get; init; }
    public required TestCaseManifest Manifest { get; init; }
}

public sealed class TestSuiteDefinition
{
    public required Identity Identity { get; init; }
    public required string ManifestPath { get; init; }
    public required TestSuiteManifest Manifest { get; init; }
    public List<TestCaseReference> ResolvedTestCases { get; } = new();
}

public sealed class TestPlanDefinition
{
    public required Identity Identity { get; init; }
    public required string ManifestPath { get; init; }
    public required TestPlanManifest Manifest { get; init; }
    public List<Identity> SuiteIdentities { get; } = new();
}

public sealed class TestCaseReference
{
    public required TestCaseNode Node { get; init; }
    public required TestCaseDefinition Definition { get; init; }
    public required string ResolvedManifestPath { get; init; }
}

public sealed class DiscoveryService
{
    public DiscoveryResult Discover(string testCaseRoot, string suiteRoot, string planRoot)
    {
        var result = new DiscoveryResult
        {
            TestCaseRoot = PathUtil.NormalizePath(testCaseRoot),
            TestSuiteRoot = PathUtil.NormalizePath(suiteRoot),
            TestPlanRoot = PathUtil.NormalizePath(planRoot)
        };

        DiscoverTestCases(result);
        DiscoverSuites(result);
        DiscoverPlans(result);

        return result;
    }

    private static void DiscoverTestCases(DiscoveryResult result)
    {
        if (!Directory.Exists(result.TestCaseRoot))
        {
            return;
        }

        var manifests = Directory.GetFiles(result.TestCaseRoot, "test.manifest.json", SearchOption.AllDirectories);
        foreach (var manifestPath in manifests)
        {
            var manifest = JsonUtil.ReadJsonFile<TestCaseManifest>(manifestPath);
            var identity = new Identity(manifest.Id, manifest.Version);
            result.TestCases.Add(new TestCaseDefinition
            {
                Identity = identity,
                ManifestPath = PathUtil.NormalizePath(manifestPath),
                Manifest = manifest
            });
        }

        EnsureUniqueIdentities(result.TestCases.Select(x => (x.Identity, x.ManifestPath, "TestCase")));
    }

    private static void DiscoverSuites(DiscoveryResult result)
    {
        if (!Directory.Exists(result.TestSuiteRoot))
        {
            return;
        }

        var manifests = Directory.GetFiles(result.TestSuiteRoot, "suite.manifest.json", SearchOption.AllDirectories);
        foreach (var manifestPath in manifests)
        {
            var manifest = JsonUtil.ReadJsonFile<TestSuiteManifest>(manifestPath);
            ValidateSuiteEnvironment(manifest, manifestPath);
            var identity = new Identity(manifest.Id, manifest.Version);
            var suite = new TestSuiteDefinition
            {
                Identity = identity,
                ManifestPath = PathUtil.NormalizePath(manifestPath),
                Manifest = manifest
            };

            foreach (var node in manifest.TestCases)
            {
                ResolveSuiteTestCase(result.TestCaseRoot, suite, node);
            }

            result.Suites.Add(suite);
        }

        EnsureUniqueIdentities(result.Suites.Select(x => (x.Identity, x.ManifestPath, "TestSuite")));
    }

    private static void DiscoverPlans(DiscoveryResult result)
    {
        if (!Directory.Exists(result.TestPlanRoot))
        {
            return;
        }

        var manifests = Directory.GetFiles(result.TestPlanRoot, "plan.manifest.json", SearchOption.AllDirectories);
        foreach (var manifestPath in manifests)
        {
            var manifest = JsonUtil.ReadJsonFile<TestPlanManifest>(manifestPath);
            ValidatePlanEnvironment(manifest, manifestPath);
            var identity = new Identity(manifest.Id, manifest.Version);
            var plan = new TestPlanDefinition
            {
                Identity = identity,
                ManifestPath = PathUtil.NormalizePath(manifestPath),
                Manifest = manifest
            };

            foreach (var suiteIdentity in manifest.Suites)
            {
                plan.SuiteIdentities.Add(Identity.Parse(suiteIdentity));
            }

            result.Plans.Add(plan);
        }

        EnsureUniqueIdentities(result.Plans.Select(x => (x.Identity, x.ManifestPath, "TestPlan")));
        ResolvePlanSuites(result);
    }

    private static void ResolveSuiteTestCase(string testCaseRoot, TestSuiteDefinition suite, TestCaseNode node)
    {
        var resolvedPath = PathUtil.CombineAndNormalize(testCaseRoot, node.Ref);
        var resolvedCanonical = PathUtil.ResolveLinkTargetPath(resolvedPath);

        if (!PathUtil.IsContained(testCaseRoot, resolvedCanonical))
        {
            throw new EngineException("Suite.TestCaseRef.Invalid", new
            {
                entityType = "TestSuite",
                suitePath = suite.ManifestPath,
                @ref = node.Ref,
                resolvedPath = resolvedCanonical,
                expectedRoot = PathUtil.NormalizePath(testCaseRoot),
                reason = "OutOfRoot"
            });
        }

        if (!Directory.Exists(resolvedCanonical))
        {
            throw new EngineException("Suite.TestCaseRef.Invalid", new
            {
                entityType = "TestSuite",
                suitePath = suite.ManifestPath,
                @ref = node.Ref,
                resolvedPath = resolvedCanonical,
                expectedRoot = PathUtil.NormalizePath(testCaseRoot),
                reason = "NotFound"
            });
        }

        var manifestPath = Path.Combine(resolvedCanonical, "test.manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new EngineException("Suite.TestCaseRef.Invalid", new
            {
                entityType = "TestSuite",
                suitePath = suite.ManifestPath,
                @ref = node.Ref,
                resolvedPath = resolvedCanonical,
                expectedRoot = PathUtil.NormalizePath(testCaseRoot),
                reason = "MissingManifest"
            });
        }

        var manifest = JsonUtil.ReadJsonFile<TestCaseManifest>(manifestPath);
        var identity = new Identity(manifest.Id, manifest.Version);
        var definition = new TestCaseDefinition
        {
            Identity = identity,
            ManifestPath = PathUtil.NormalizePath(manifestPath),
            Manifest = manifest
        };

        suite.ResolvedTestCases.Add(new TestCaseReference
        {
            Node = node,
            Definition = definition,
            ResolvedManifestPath = PathUtil.NormalizePath(manifestPath)
        });
    }

    private static void ResolvePlanSuites(DiscoveryResult result)
    {
        foreach (var plan in result.Plans)
        {
            foreach (var suiteIdentity in plan.SuiteIdentities)
            {
                var matches = result.Suites.Where(x => x.Identity == suiteIdentity).ToList();
                if (matches.Count == 0)
                {
                    throw new EngineException("Identity.NotFound", new
                    {
                        entityType = "suite",
                        id = suiteIdentity.Id,
                        version = suiteIdentity.Version,
                        reason = "NotFound"
                    });
                }

                if (matches.Count > 1)
                {
                    throw new EngineException("Identity.NonUnique", new
                    {
                        entityType = "suite",
                        id = suiteIdentity.Id,
                        version = suiteIdentity.Version,
                        reason = "NonUnique",
                        conflictPaths = matches.Select(m => m.ManifestPath).ToArray()
                    });
                }
            }
        }
    }

    private static void EnsureUniqueIdentities(IEnumerable<(Identity identity, string path, string entityType)> entries)
    {
        var grouped = entries.GroupBy(x => x.identity).ToList();
        foreach (var group in grouped)
        {
            if (group.Count() > 1)
            {
                var entityType = group.First().entityType;
                throw new EngineException("Identity.NonUnique", new
                {
                    entityType,
                    id = group.Key.Id,
                    version = group.Key.Version,
                    conflictPaths = group.Select(x => x.path).ToArray()
                });
            }
        }
    }

    private static void ValidateSuiteEnvironment(TestSuiteManifest manifest, string manifestPath)
    {
        if (manifest.Environment?.Env is null)
        {
            return;
        }

        foreach (var key in manifest.Environment.Env.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new EngineException("Suite.Environment.Invalid", new { suitePath = manifestPath, reason = "EmptyKey" });
            }
        }
    }

    private static void ValidatePlanEnvironment(TestPlanManifest manifest, string manifestPath)
    {
        if (manifest.Environment?.ExtensionData is { Count: > 0 })
        {
            throw new EngineException("Plan.Environment.Invalid", new { planPath = manifestPath, reason = "NonEnvKey" });
        }

        if (manifest.Environment?.Env is null)
        {
            return;
        }

        foreach (var key in manifest.Environment.Env.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new EngineException("Plan.Environment.Invalid", new { planPath = manifestPath, reason = "EmptyKey" });
            }
        }
    }
}
