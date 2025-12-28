using System.Text;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Contracts.Validation;

namespace PcTest.Engine.Discovery;

/// <summary>
/// Discovery service that scans roots for manifests per spec section 5.
/// </summary>
public sealed class DiscoveryService
{
    private const string TestCaseManifestName = "test.manifest.json";
    private const string TestSuiteManifestName = "suite.manifest.json";
    private const string TestPlanManifestName = "plan.manifest.json";

    /// <summary>
    /// Discovers all entities under the given roots.
    /// Per spec section 5.3: id@version must be unique within each entity type.
    /// </summary>
    public DiscoveryResult Discover(
        string testCaseRoot,
        string testSuiteRoot,
        string testPlanRoot)
    {
        var result = new DiscoveryResult
        {
            ResolvedTestCaseRoot = PathUtils.NormalizePath(testCaseRoot),
            ResolvedTestSuiteRoot = PathUtils.NormalizePath(testSuiteRoot),
            ResolvedTestPlanRoot = PathUtils.NormalizePath(testPlanRoot)
        };

        var errors = new List<ValidationError>();

        // Discover Test Cases
        var testCaseDuplicates = new Dictionary<string, List<string>>();
        DiscoverTestCases(result.ResolvedTestCaseRoot, result.TestCases, testCaseDuplicates);
        AddDuplicateErrors(errors, "TestCase", testCaseDuplicates);

        // Discover Test Suites
        var testSuiteDuplicates = new Dictionary<string, List<string>>();
        DiscoverTestSuites(result.ResolvedTestSuiteRoot, result.TestSuites, testSuiteDuplicates);
        AddDuplicateErrors(errors, "TestSuite", testSuiteDuplicates);

        // Discover Test Plans
        var testPlanDuplicates = new Dictionary<string, List<string>>();
        DiscoverTestPlans(result.ResolvedTestPlanRoot, result.TestPlans, testPlanDuplicates);
        AddDuplicateErrors(errors, "TestPlan", testPlanDuplicates);

        if (errors.Count > 0)
        {
            throw new ValidationException(new ValidationResult { Errors = { } }.Also(r => errors.ForEach(r.AddError)));
        }

        return result;
    }

    private void DiscoverTestCases(
        string root,
        Dictionary<string, DiscoveredTestCase> discovered,
        Dictionary<string, List<string>> duplicates)
    {
        if (!Directory.Exists(root))
            return;

        foreach (var manifestPath in Directory.EnumerateFiles(root, TestCaseManifestName, SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(manifestPath, Encoding.UTF8);
                var manifest = JsonDefaults.Deserialize<TestCaseManifest>(json);
                if (manifest is null)
                    continue;

                var folderPath = PathUtils.NormalizePath(Path.GetDirectoryName(manifestPath) ?? root);
                var normalizedManifestPath = PathUtils.NormalizePath(manifestPath);

                var identity = manifest.Identity;
                if (discovered.ContainsKey(identity))
                {
                    // Track duplicate
                    if (!duplicates.ContainsKey(identity))
                    {
                        duplicates[identity] = new List<string> { discovered[identity].ManifestPath };
                    }
                    duplicates[identity].Add(normalizedManifestPath);
                }
                else
                {
                    discovered[identity] = new DiscoveredTestCase
                    {
                        Manifest = manifest,
                        ManifestPath = normalizedManifestPath,
                        FolderPath = folderPath
                    };
                }
            }
            catch
            {
                // Skip invalid manifests
            }
        }
    }

    private void DiscoverTestSuites(
        string root,
        Dictionary<string, DiscoveredTestSuite> discovered,
        Dictionary<string, List<string>> duplicates)
    {
        if (!Directory.Exists(root))
            return;

        foreach (var manifestPath in Directory.EnumerateFiles(root, TestSuiteManifestName, SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(manifestPath, Encoding.UTF8);
                var manifest = JsonDefaults.Deserialize<TestSuiteManifest>(json);
                if (manifest is null)
                    continue;

                var folderPath = PathUtils.NormalizePath(Path.GetDirectoryName(manifestPath) ?? root);
                var normalizedManifestPath = PathUtils.NormalizePath(manifestPath);

                var identity = manifest.Identity;
                if (discovered.ContainsKey(identity))
                {
                    if (!duplicates.ContainsKey(identity))
                    {
                        duplicates[identity] = new List<string> { discovered[identity].ManifestPath };
                    }
                    duplicates[identity].Add(normalizedManifestPath);
                }
                else
                {
                    discovered[identity] = new DiscoveredTestSuite
                    {
                        Manifest = manifest,
                        ManifestPath = normalizedManifestPath,
                        FolderPath = folderPath
                    };
                }
            }
            catch
            {
                // Skip invalid manifests
            }
        }
    }

    private void DiscoverTestPlans(
        string root,
        Dictionary<string, DiscoveredTestPlan> discovered,
        Dictionary<string, List<string>> duplicates)
    {
        if (!Directory.Exists(root))
            return;

        foreach (var manifestPath in Directory.EnumerateFiles(root, TestPlanManifestName, SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(manifestPath, Encoding.UTF8);
                var manifest = JsonDefaults.Deserialize<TestPlanManifest>(json);
                if (manifest is null)
                    continue;

                var folderPath = PathUtils.NormalizePath(Path.GetDirectoryName(manifestPath) ?? root);
                var normalizedManifestPath = PathUtils.NormalizePath(manifestPath);

                var identity = manifest.Identity;
                if (discovered.ContainsKey(identity))
                {
                    if (!duplicates.ContainsKey(identity))
                    {
                        duplicates[identity] = new List<string> { discovered[identity].ManifestPath };
                    }
                    duplicates[identity].Add(normalizedManifestPath);
                }
                else
                {
                    discovered[identity] = new DiscoveredTestPlan
                    {
                        Manifest = manifest,
                        ManifestPath = normalizedManifestPath,
                        FolderPath = folderPath
                    };
                }
            }
            catch
            {
                // Skip invalid manifests
            }
        }
    }

    private static void AddDuplicateErrors(
        List<ValidationError> errors,
        string entityType,
        Dictionary<string, List<string>> duplicates)
    {
        foreach (var (identity, paths) in duplicates)
        {
            var parseResult = IdentityParser.Parse(identity);
            errors.Add(new ValidationError
            {
                Code = ErrorCodes.DuplicateIdentity,
                Message = $"Duplicate {entityType} identity: {identity}",
                EntityType = entityType,
                Id = parseResult.Success ? parseResult.Id : identity,
                Version = parseResult.Success ? parseResult.Version : null,
                ConflictPaths = paths
            });
        }
    }
}

/// <summary>
/// Extension method helper.
/// </summary>
internal static class ObjectExtensions
{
    public static T Also<T>(this T obj, Action<T> action)
    {
        action(obj);
        return obj;
    }
}
