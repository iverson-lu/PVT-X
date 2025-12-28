using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine;

public sealed class DiscoveryService
{
    public DiscoveryResult Discover(DiscoveryOptions options)
    {
        DiscoveryResult result = new();
        ResolveTestCases(options, result);
        ResolveTestSuites(options, result);
        ResolveTestPlans(options, result);
        ValidatePlanSuites(result);
        ValidateUniqueness(result);
        return result;
    }

    private static void ResolveTestCases(DiscoveryOptions options, DiscoveryResult result)
    {
        if (string.IsNullOrWhiteSpace(options.TestCaseRoot))
        {
            return;
        }

        foreach (string manifestPath in Directory.EnumerateFiles(options.TestCaseRoot, "test.manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                TestCaseManifest manifest = JsonFile.Read<TestCaseManifest>(manifestPath);
                Identity identity = new(manifest.Id, manifest.Version);
                result.TestCases.Add(new ResolvedTestCase
                {
                    Manifest = manifest,
                    ManifestPath = manifestPath,
                    Identity = identity
                });
            }
            catch (Exception ex)
            {
                result.Validation.Add("TestCase.Manifest.Invalid", ex.Message, new Dictionary<string, object?>
                {
                    ["path"] = manifestPath
                });
            }
        }
    }

    private static void ResolveTestSuites(DiscoveryOptions options, DiscoveryResult result)
    {
        if (string.IsNullOrWhiteSpace(options.TestSuiteRoot))
        {
            return;
        }

        foreach (string manifestPath in Directory.EnumerateFiles(options.TestSuiteRoot, "suite.manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                TestSuiteManifest manifest = JsonFile.Read<TestSuiteManifest>(manifestPath);
                Identity identity = new(manifest.Id, manifest.Version);

                ValidateSuiteRefs(options.TestCaseRoot, manifest, manifestPath, result);
                ValidateSuiteEnvironment(manifest, manifestPath, result);

                result.TestSuites.Add(new ResolvedTestSuite
                {
                    Manifest = manifest,
                    ManifestPath = manifestPath,
                    Identity = identity
                });
            }
            catch (Exception ex)
            {
                result.Validation.Add("Suite.Manifest.Invalid", ex.Message, new Dictionary<string, object?>
                {
                    ["path"] = manifestPath
                });
            }
        }
    }

    private static void ResolveTestPlans(DiscoveryOptions options, DiscoveryResult result)
    {
        if (string.IsNullOrWhiteSpace(options.TestPlanRoot))
        {
            return;
        }

        foreach (string manifestPath in Directory.EnumerateFiles(options.TestPlanRoot, "plan.manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                JsonDocument doc = JsonFile.ReadDocument(manifestPath);
                TestPlanManifest manifest = JsonSerializer.Deserialize<TestPlanManifest>(doc.RootElement.GetRawText(), JsonDefaults.Options) ?? new TestPlanManifest();
                Identity identity = new(manifest.Id, manifest.Version);

                ValidatePlanEnvironment(doc.RootElement, manifestPath, result);

                result.TestPlans.Add(new ResolvedTestPlan
                {
                    Manifest = manifest,
                    ManifestPath = manifestPath,
                    Identity = identity
                });
            }
            catch (Exception ex)
            {
                result.Validation.Add("Plan.Manifest.Invalid", ex.Message, new Dictionary<string, object?>
                {
                    ["path"] = manifestPath
                });
            }
        }
    }

    private static void ValidateSuiteRefs(string testCaseRoot, TestSuiteManifest manifest, string suitePath, DiscoveryResult result)
    {
        string normalizedRoot = PathUtils.NormalizePath(testCaseRoot);
        HashSet<string> nodeIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (TestCaseNode node in manifest.TestCases)
        {
            if (!nodeIds.Add(node.NodeId))
            {
                result.Validation.Add("Suite.NodeId.Duplicate", "Duplicate nodeId.", new Dictionary<string, object?>
                {
                    ["suitePath"] = suitePath,
                    ["nodeId"] = node.NodeId
                });
            }

            string resolvedPath = PathUtils.CombineNormalized(normalizedRoot, node.Ref);
            string resolvedWithLinks = PathUtils.ResolvePathWithLinks(resolvedPath);
            if (!PathUtils.IsContainedBy(normalizedRoot, resolvedWithLinks))
            {
                AddSuiteRefError(result, suitePath, node.Ref, resolvedWithLinks, normalizedRoot, "OutOfRoot");
                continue;
            }

            if (!Directory.Exists(resolvedWithLinks))
            {
                AddSuiteRefError(result, suitePath, node.Ref, resolvedWithLinks, normalizedRoot, "NotFound");
                continue;
            }

            string manifestPath = Path.Combine(resolvedWithLinks, "test.manifest.json");
            if (!File.Exists(manifestPath))
            {
                AddSuiteRefError(result, suitePath, node.Ref, manifestPath, normalizedRoot, "MissingManifest");
            }
        }
    }

    private static void ValidateSuiteEnvironment(TestSuiteManifest manifest, string suitePath, DiscoveryResult result)
    {
        if (manifest.Environment?.Env is null)
        {
            return;
        }

        foreach (string key in manifest.Environment.Env.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                result.Validation.Add("Suite.Environment.Invalid", "Environment keys must be non-empty.", new Dictionary<string, object?>
                {
                    ["suitePath"] = suitePath
                });
            }
        }
    }

    private static void ValidatePlanEnvironment(JsonElement root, string planPath, DiscoveryResult result)
    {
        if (!root.TryGetProperty("environment", out JsonElement envElement))
        {
            return;
        }

        if (envElement.ValueKind != JsonValueKind.Object)
        {
            result.Validation.Add("Plan.Environment.Invalid", "Plan environment must be an object.", new Dictionary<string, object?>
            {
                ["planPath"] = planPath
            });
            return;
        }

        foreach (JsonProperty property in envElement.EnumerateObject())
        {
            if (property.NameEquals("env"))
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    result.Validation.Add("Plan.Environment.Invalid", "Plan environment env must be an object.", new Dictionary<string, object?>
                    {
                        ["planPath"] = planPath
                    });
                }

                continue;
            }

            result.Validation.Add("Plan.Environment.Invalid", "Plan environment only allows env.", new Dictionary<string, object?>
            {
                ["planPath"] = planPath,
                ["key"] = property.Name
            });
        }
    }

    private static void ValidateUniqueness(DiscoveryResult result)
    {
        ValidateIdentityUniqueness(result.TestCases.Select(tc => ("TestCase", tc.Identity, tc.ManifestPath)).ToList(), result);
        ValidateIdentityUniqueness(result.TestSuites.Select(tc => ("TestSuite", tc.Identity, tc.ManifestPath)).ToList(), result);
        ValidateIdentityUniqueness(result.TestPlans.Select(tc => ("TestPlan", tc.Identity, tc.ManifestPath)).ToList(), result);
    }

    private static void ValidatePlanSuites(DiscoveryResult result)
    {
        foreach (ResolvedTestPlan plan in result.TestPlans)
        {
            foreach (string suiteRef in plan.Manifest.Suites)
            {
                if (!Identity.TryParse(suiteRef, out Identity identity))
                {
                    result.Validation.Add("Plan.SuiteRef.Invalid", "Suite reference must be id@version.", new Dictionary<string, object?>
                    {
                        ["planPath"] = plan.ManifestPath,
                        ["ref"] = suiteRef
                    });
                    continue;
                }

                List<ResolvedTestSuite> matches = result.TestSuites.Where(suite => suite.Identity.Id == identity.Id && suite.Identity.Version == identity.Version).ToList();
                if (matches.Count == 0)
                {
                    result.Validation.Add("Plan.SuiteRef.NotFound", "Suite reference not found.", new Dictionary<string, object?>
                    {
                        ["planPath"] = plan.ManifestPath,
                        ["id"] = identity.Id,
                        ["version"] = identity.Version
                    });
                }
                else if (matches.Count > 1)
                {
                    result.Validation.Add("Plan.SuiteRef.NonUnique", "Suite reference not unique.", new Dictionary<string, object?>
                    {
                        ["planPath"] = plan.ManifestPath,
                        ["id"] = identity.Id,
                        ["version"] = identity.Version,
                        ["conflictPaths"] = matches.Select(m => m.ManifestPath).ToList()
                    });
                }
            }
        }
    }

    private static void ValidateIdentityUniqueness(List<(string EntityType, Identity Identity, string Path)> items, DiscoveryResult result)
    {
        var groups = items.GroupBy(item => item.Identity.ToString(), StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, (string EntityType, Identity Identity, string Path)> group in groups)
        {
            List<string> paths = group.Select(item => item.Path).ToList();
            if (paths.Count <= 1)
            {
                continue;
            }

            (string entityType, Identity identity, _) = group.First();
            result.Validation.Add("Identity.Duplicate", "Duplicate identity detected.", new Dictionary<string, object?>
            {
                ["entityType"] = entityType,
                ["id"] = identity.Id,
                ["version"] = identity.Version,
                ["conflictPaths"] = paths
            });
        }
    }

    private static void AddSuiteRefError(DiscoveryResult result, string suitePath, string reference, string resolvedPath, string expectedRoot, string reason)
    {
        result.Validation.Add("Suite.TestCaseRef.Invalid", "Suite test case reference is invalid.", new Dictionary<string, object?>
        {
            ["entityType"] = "TestSuite",
            ["suitePath"] = suitePath,
            ["ref"] = reference,
            ["resolvedPath"] = resolvedPath,
            ["expectedRoot"] = expectedRoot,
            ["reason"] = reason
        });
    }
}
