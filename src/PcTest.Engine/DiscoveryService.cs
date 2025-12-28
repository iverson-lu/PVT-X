using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine;

public sealed class DiscoveryService
{
    public DiscoveryResult Discover(DiscoveryRoots roots)
    {
        var testCases = DiscoverManifests(roots.ResolvedTestCaseRoot, "test.manifest.json", JsonParsing.ReadFile<TestCaseManifest>)
            .Select(item => new DiscoveredTestCase(item.Path, item.Manifest))
            .ToList();

        var suites = DiscoverManifests(roots.ResolvedTestSuiteRoot, "suite.manifest.json", JsonParsing.ReadFile<TestSuiteManifest>)
            .Select(item => new DiscoveredTestSuite(item.Path, item.Manifest))
            .ToList();

        var plans = DiscoverManifests(roots.ResolvedTestPlanRoot, "plan.manifest.json", JsonParsing.ReadFile<TestPlanManifest>)
            .Select(item => new DiscoveredTestPlan(item.Path, item.Manifest))
            .ToList();

        ValidateUniqueness("TestCase", testCases.Select(tc => new IdentityRecord(tc.Manifest.Identity, tc.Path)));
        ValidateUniqueness("TestSuite", suites.Select(suite => new IdentityRecord(suite.Manifest.Identity, suite.Path)));
        ValidateUniqueness("TestPlan", plans.Select(plan => new IdentityRecord(plan.Manifest.Identity, plan.Path)));

        ValidateTestCases(testCases);
        ValidateSuites(testCases, suites, roots.ResolvedTestCaseRoot);
        ValidatePlans(suites, plans);

        return new DiscoveryResult(testCases, suites, plans);
    }

    private static List<ManifestFile<T>> DiscoverManifests<T>(string root, string fileName, Func<string, T> loader)
    {
        var results = new List<ManifestFile<T>>();
        if (!Directory.Exists(root))
        {
            return results;
        }

        foreach (var path in Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories))
        {
            results.Add(new ManifestFile<T>(PathUtils.NormalizePath(path), loader(path)));
        }

        return results;
    }

    private static void ValidateUniqueness(string entityType, IEnumerable<IdentityRecord> records)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var key = record.Identity.ToString();
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<string>();
                map[key] = list;
            }

            list.Add(record.Path);
        }

        foreach (var pair in map)
        {
            if (pair.Value.Count > 1)
            {
                throw new DiscoveryException("Identity.NotUnique", new Dictionary<string, object>
                {
                    ["entityType"] = entityType,
                    ["id"] = pair.Key.Split('@')[0],
                    ["version"] = pair.Key.Split('@')[1],
                    ["conflictPaths"] = pair.Value
                });
            }
        }
    }

    private static void ValidateTestCases(IEnumerable<DiscoveredTestCase> testCases)
    {
        foreach (var testCase in testCases)
        {
            if (testCase.Manifest.Parameters is null)
            {
                continue;
            }

            foreach (var parameter in testCase.Manifest.Parameters)
            {
                if (!ParameterTypeHelper.TryParse(parameter.Type, out _))
                {
                    throw new DiscoveryException("Parameter.Type.Invalid", new Dictionary<string, object>
                    {
                        ["testCase"] = testCase.Path,
                        ["parameter"] = parameter.Name,
                        ["type"] = parameter.Type
                    });
                }

                if (ParameterTypeHelper.IsEnum(ParameterTypeHelper.TryParse(parameter.Type, out var parsed) ? parsed : ParameterType.String)
                    && (parameter.EnumValues is null || parameter.EnumValues.Length == 0))
                {
                    throw new DiscoveryException("Parameter.EnumValues.Missing", new Dictionary<string, object>
                    {
                        ["testCase"] = testCase.Path,
                        ["parameter"] = parameter.Name
                    });
                }
            }
        }
    }

    private static void ValidateSuites(IEnumerable<DiscoveredTestCase> testCases, IEnumerable<DiscoveredTestSuite> suites, string caseRoot)
    {
        var caseIndex = testCases.ToDictionary(tc => tc.Path, tc => tc, StringComparer.OrdinalIgnoreCase);
        var manifestIndex = testCases.ToDictionary(tc => tc.Manifest.Identity.ToString(), tc => tc, StringComparer.Ordinal);

        foreach (var suite in suites)
        {
            if (suite.Manifest.Environment?.Env is not null)
            {
                foreach (var key in suite.Manifest.Environment.Env.Keys)
                {
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        throw new DiscoveryException("Suite.Environment.Invalid", new Dictionary<string, object>
                        {
                            ["suitePath"] = suite.Path,
                            ["reason"] = "EmptyKey"
                        });
                    }
                }
            }

            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in suite.Manifest.TestCases)
            {
                if (!nodeIds.Add(node.NodeId))
                {
                    throw new DiscoveryException("Suite.NodeId.Duplicate", new Dictionary<string, object>
                    {
                        ["suitePath"] = suite.Path,
                        ["nodeId"] = node.NodeId
                    });
                }

                var resolved = ResolveSuiteRef(caseRoot, node.Ref);
                if (resolved.Error is not null)
                {
                    throw new DiscoveryException("Suite.TestCaseRef.Invalid", new Dictionary<string, object>
                    {
                        ["entityType"] = "TestSuite",
                        ["suitePath"] = suite.Path,
                        ["ref"] = node.Ref,
                        ["resolvedPath"] = resolved.ResolvedPath,
                        ["expectedRoot"] = caseRoot,
                        ["reason"] = resolved.Error
                    });
                }

                if (!caseIndex.TryGetValue(resolved.ResolvedPath, out var testCase))
                {
                    if (!File.Exists(resolved.ResolvedPath))
                    {
                        throw new DiscoveryException("Suite.TestCaseRef.Invalid", new Dictionary<string, object>
                        {
                            ["entityType"] = "TestSuite",
                            ["suitePath"] = suite.Path,
                            ["ref"] = node.Ref,
                            ["resolvedPath"] = resolved.ResolvedPath,
                            ["expectedRoot"] = caseRoot,
                            ["reason"] = "MissingManifest"
                        });
                    }

                    throw new DiscoveryException("Suite.TestCaseRef.Invalid", new Dictionary<string, object>
                    {
                        ["entityType"] = "TestSuite",
                        ["suitePath"] = suite.Path,
                        ["ref"] = node.Ref,
                        ["resolvedPath"] = resolved.ResolvedPath,
                        ["expectedRoot"] = caseRoot,
                        ["reason"] = "NotFound"
                    });
                }

                if (node.Inputs is not null)
                {
                    var parameterNames = new HashSet<string>(testCase.Manifest.Parameters?.Select(p => p.Name) ?? Array.Empty<string>(), StringComparer.Ordinal);
                    foreach (var input in node.Inputs.Keys)
                    {
                        if (!parameterNames.Contains(input))
                        {
                            throw new DiscoveryException("Suite.Input.Invalid", new Dictionary<string, object>
                            {
                                ["suitePath"] = suite.Path,
                                ["nodeId"] = node.NodeId,
                                ["input"] = input
                            });
                        }
                    }
                }
            }
        }
    }

    private static void ValidatePlans(IEnumerable<DiscoveredTestSuite> suites, IEnumerable<DiscoveredTestPlan> plans)
    {
        var suiteIndex = suites.GroupBy(suite => suite.Manifest.Identity.ToString(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (var plan in plans)
        {
            if (plan.Manifest.Environment?.Extra is not null && plan.Manifest.Environment.Extra.Count > 0)
            {
                throw new DiscoveryException("Plan.Environment.Invalid", new Dictionary<string, object>
                {
                    ["planPath"] = plan.Path,
                    ["reason"] = "UnexpectedKey"
                });
            }

            if (plan.Manifest.Environment?.Env is not null)
            {
                foreach (var key in plan.Manifest.Environment.Env.Keys)
                {
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        throw new DiscoveryException("Plan.Environment.Invalid", new Dictionary<string, object>
                        {
                            ["planPath"] = plan.Path,
                            ["reason"] = "EmptyKey"
                        });
                    }
                }
            }

            foreach (var suiteRef in plan.Manifest.Suites)
            {
                var identity = Identity.Parse(suiteRef);
                if (!suiteIndex.TryGetValue(identity.ToString(), out var matches))
                {
                    throw new DiscoveryException("Plan.Suite.NotFound", new Dictionary<string, object>
                    {
                        ["entityType"] = "TestSuite",
                        ["id"] = identity.Id,
                        ["version"] = identity.Version,
                        ["reason"] = "NotFound"
                    });
                }

                if (matches.Count > 1)
                {
                    throw new DiscoveryException("Plan.Suite.NonUnique", new Dictionary<string, object>
                    {
                        ["entityType"] = "TestSuite",
                        ["id"] = identity.Id,
                        ["version"] = identity.Version,
                        ["conflictPaths"] = matches.Select(m => m.Path).ToArray()
                    });
                }
            }
        }
    }

    private static SuiteRefResolution ResolveSuiteRef(string caseRoot, string suiteRef)
    {
        var refRoot = Path.Combine(caseRoot, suiteRef);
        var resolvedFolder = PathUtils.ResolveFinalDirectory(refRoot);
        var resolvedManifest = Path.Combine(resolvedFolder, "test.manifest.json");
        var normalizedManifest = PathUtils.NormalizePath(resolvedManifest);

        if (!PathUtils.IsContained(caseRoot, normalizedManifest))
        {
            return new SuiteRefResolution(normalizedManifest, "OutOfRoot");
        }

        if (!Directory.Exists(resolvedFolder))
        {
            return new SuiteRefResolution(normalizedManifest, "NotFound");
        }

        if (!File.Exists(normalizedManifest))
        {
            return new SuiteRefResolution(normalizedManifest, "MissingManifest");
        }

        return new SuiteRefResolution(normalizedManifest, null);
    }

    private sealed record ManifestFile<T>(string Path, T Manifest);

    private sealed record IdentityRecord(Identity Identity, string Path);

    private sealed record SuiteRefResolution(string ResolvedPath, string? Error);
}

public sealed record DiscoveryRoots
{
    public required string ResolvedTestCaseRoot { get; init; }

    public required string ResolvedTestSuiteRoot { get; init; }

    public required string ResolvedTestPlanRoot { get; init; }
}

public sealed record DiscoveryResult(
    IReadOnlyList<DiscoveredTestCase> TestCases,
    IReadOnlyList<DiscoveredTestSuite> TestSuites,
    IReadOnlyList<DiscoveredTestPlan> TestPlans);

public sealed record DiscoveredTestCase(string Path, TestCaseManifest Manifest);

public sealed record DiscoveredTestSuite(string Path, TestSuiteManifest Manifest);

public sealed record DiscoveredTestPlan(string Path, TestPlanManifest Manifest);

public sealed class DiscoveryException : Exception
{
    public DiscoveryException(string code, Dictionary<string, object> payload)
        : base(code)
    {
        Code = code;
        Payload = payload;
    }

    public string Code { get; }

    public Dictionary<string, object> Payload { get; }
}
