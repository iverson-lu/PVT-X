using PcTest.Contracts;

namespace PcTest.Engine;

public sealed class DiscoveryRoots
{
    public string TestCaseRoot { get; init; } = string.Empty;
    public string TestSuiteRoot { get; init; } = string.Empty;
    public string TestPlanRoot { get; init; } = string.Empty;
}

public sealed record DiscoveredTestCase(string Path, TestCaseManifest Manifest);
public sealed record DiscoveredTestSuite(string Path, TestSuiteManifest Manifest);
public sealed record DiscoveredTestPlan(string Path, TestPlanManifest Manifest);

public sealed class DiscoveryResult
{
    public List<DiscoveredTestCase> TestCases { get; } = new();
    public List<DiscoveredTestSuite> TestSuites { get; } = new();
    public List<DiscoveredTestPlan> TestPlans { get; } = new();
    public List<ValidationError> Errors { get; } = new();

    public bool HasErrors => Errors.Count > 0;
}

public sealed class DiscoveryService
{
    public DiscoveryResult Discover(DiscoveryRoots roots)
    {
        var result = new DiscoveryResult();
        DiscoverManifests(roots.TestCaseRoot, "test.manifest.json", result.TestCases, result.Errors);
        DiscoverManifests(roots.TestSuiteRoot, "suite.manifest.json", result.TestSuites, result.Errors);
        DiscoverManifests(roots.TestPlanRoot, "plan.manifest.json", result.TestPlans, result.Errors);
        ValidateUniqueness(result.TestCases, "TestCase", result.Errors, x => x.Manifest.Id, x => x.Manifest.Version, x => x.Path);
        ValidateUniqueness(result.TestSuites, "TestSuite", result.Errors, x => x.Manifest.Id, x => x.Manifest.Version, x => x.Path);
        ValidateUniqueness(result.TestPlans, "TestPlan", result.Errors, x => x.Manifest.Id, x => x.Manifest.Version, x => x.Path);
        return result;
    }

    private static void DiscoverManifests<T>(string root, string fileName, List<T> items, List<ValidationError> errors) where T : class
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return;
        }
        foreach (var path in Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories))
        {
            try
            {
                if (typeof(T) == typeof(DiscoveredTestCase))
                {
                    var manifest = JsonUtilities.ReadJson<TestCaseManifest>(path);
                    items.Add((T)(object)new DiscoveredTestCase(path, manifest));
                }
                else if (typeof(T) == typeof(DiscoveredTestSuite))
                {
                    var manifest = JsonUtilities.ReadJson<TestSuiteManifest>(path);
                    ValidateSuiteEnvironment(manifest, path, errors);
                    items.Add((T)(object)new DiscoveredTestSuite(path, manifest));
                }
                else if (typeof(T) == typeof(DiscoveredTestPlan))
                {
                    var manifest = JsonUtilities.ReadJson<TestPlanManifest>(path);
                    ValidatePlanEnvironment(path, errors);
                    items.Add((T)(object)new DiscoveredTestPlan(path, manifest));
                }
            }
            catch (Exception ex)
            {
                errors.Add(new ValidationError("Discovery.ReadFailed", ex.Message, new { path }));
            }
        }
    }

    private static void ValidateUniqueness<T>(IEnumerable<T> items, string entityType, List<ValidationError> errors, Func<T, string> idSelector, Func<T, string> versionSelector, Func<T, string> pathSelector)
    {
        var groups = items.GroupBy(item => $"{idSelector(item)}@{versionSelector(item)}", StringComparer.Ordinal);
        foreach (var group in groups.Where(g => g.Count() > 1))
        {
            errors.Add(new ValidationError(ErrorCodes.IdentityNonUnique, "Duplicate identity discovered.", new
            {
                entityType,
                id = idSelector(group.First()),
                version = versionSelector(group.First()),
                conflictPaths = group.Select(pathSelector).ToArray()
            }));
        }
    }

    private static void ValidateSuiteEnvironment(TestSuiteManifest manifest, string path, List<ValidationError> errors)
    {
        if (manifest.Environment?.Env is null)
        {
            return;
        }
        foreach (var key in manifest.Environment.Env.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                errors.Add(new ValidationError(\"Suite.Environment.Invalid\", \"Environment key must be non-empty.\", new { path }));\n            }\n        }\n    }\n\n    private static void ValidatePlanEnvironment(string path, List<ValidationError> errors)\n    {\n        using var stream = File.OpenRead(path);\n        using var document = System.Text.Json.JsonDocument.Parse(stream);\n        if (!document.RootElement.TryGetProperty(\"environment\", out var envElement))\n        {\n            return;\n        }\n        if (envElement.ValueKind != System.Text.Json.JsonValueKind.Object)\n        {\n            errors.Add(new ValidationError(\"Plan.Environment.Invalid\", \"Environment must be an object.\", new { path }));\n            return;\n        }\n        foreach (var property in envElement.EnumerateObject())\n        {\n            if (!property.Name.Equals(\"env\", StringComparison.Ordinal))\n            {\n                errors.Add(new ValidationError(\"Plan.Environment.Invalid\", \"Plan environment only supports env.\", new { path, key = property.Name }));\n            }\n        }\n    }
}
