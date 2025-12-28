using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine;

public sealed class DiscoveryOptions
{
    public string TestCaseRoot { get; init; } = string.Empty;
    public string TestSuiteRoot { get; init; } = string.Empty;
    public string TestPlanRoot { get; init; } = string.Empty;
}

public sealed class DiscoveredTestCase
{
    public required Identity Identity { get; init; }
    public required TestCaseManifest Manifest { get; init; }
    public required string ManifestPath { get; init; }
    public required string FolderPath { get; init; }
    public required JsonElement SourceManifest { get; init; }
}

public sealed class DiscoveredSuite
{
    public required Identity Identity { get; init; }
    public required SuiteManifest Manifest { get; init; }
    public required string ManifestPath { get; init; }
    public required string FolderPath { get; init; }
    public required JsonElement SourceManifest { get; init; }
}

public sealed class DiscoveredPlan
{
    public required Identity Identity { get; init; }
    public required PlanManifest Manifest { get; init; }
    public required string ManifestPath { get; init; }
    public required string FolderPath { get; init; }
    public required JsonElement SourceManifest { get; init; }
}

public sealed class DiscoveryResult
{
    public required IReadOnlyDictionary<Identity, DiscoveredTestCase> TestCases { get; init; }
    public required IReadOnlyDictionary<Identity, DiscoveredSuite> Suites { get; init; }
    public required IReadOnlyDictionary<Identity, DiscoveredPlan> Plans { get; init; }
    public required string TestCaseRoot { get; init; }
    public required string TestSuiteRoot { get; init; }
    public required string TestPlanRoot { get; init; }
}

public static class DiscoveryService
{
    public static DiscoveryResult Discover(DiscoveryOptions options)
    {
        var testCases = DiscoverTestCases(options.TestCaseRoot);
        var suites = DiscoverSuites(options.TestSuiteRoot);
        var plans = DiscoverPlans(options.TestPlanRoot);
        return new DiscoveryResult
        {
            TestCases = testCases,
            Suites = suites,
            Plans = plans,
            TestCaseRoot = options.TestCaseRoot,
            TestSuiteRoot = options.TestSuiteRoot,
            TestPlanRoot = options.TestPlanRoot
        };
    }

    private static IReadOnlyDictionary<Identity, DiscoveredTestCase> DiscoverTestCases(string root)
    {
        var entries = new Dictionary<Identity, DiscoveredTestCase>();
        var conflicts = new Dictionary<Identity, List<string>>();

        foreach (var manifestPath in Directory.EnumerateFiles(root, "test.manifest.json", SearchOption.AllDirectories))
        {
            var manifest = JsonUtils.ReadFile<TestCaseManifest>(manifestPath);
            var source = JsonUtils.ReadJsonElementFile(manifestPath);
            var identity = new Identity(manifest.Id, manifest.Version);
            var folder = Path.GetDirectoryName(manifestPath) ?? root;

            if (!entries.TryAdd(identity, new DiscoveredTestCase
            {
                Identity = identity,
                Manifest = manifest,
                ManifestPath = manifestPath,
                FolderPath = folder,
                SourceManifest = source
            }))
            {
                if (!conflicts.TryGetValue(identity, out var list))
                {
                    list = new List<string>();
                    list.Add(entries[identity].ManifestPath);
                    conflicts[identity] = list;
                }

                list.Add(manifestPath);
            }
        }

        if (conflicts.Count > 0)
        {
            ThrowIdentityConflicts("TestCase", conflicts);
        }

        return entries;
    }

    private static IReadOnlyDictionary<Identity, DiscoveredSuite> DiscoverSuites(string root)
    {
        var entries = new Dictionary<Identity, DiscoveredSuite>();
        var conflicts = new Dictionary<Identity, List<string>>();

        foreach (var manifestPath in Directory.EnumerateFiles(root, "suite.manifest.json", SearchOption.AllDirectories))
        {
            var manifest = JsonUtils.ReadFile<SuiteManifest>(manifestPath);
            var source = JsonUtils.ReadJsonElementFile(manifestPath);
            var identity = new Identity(manifest.Id, manifest.Version);
            var folder = Path.GetDirectoryName(manifestPath) ?? root;

            if (!entries.TryAdd(identity, new DiscoveredSuite
            {
                Identity = identity,
                Manifest = manifest,
                ManifestPath = manifestPath,
                FolderPath = folder,
                SourceManifest = source
            }))
            {
                if (!conflicts.TryGetValue(identity, out var list))
                {
                    list = new List<string>();
                    list.Add(entries[identity].ManifestPath);
                    conflicts[identity] = list;
                }

                list.Add(manifestPath);
            }
        }

        if (conflicts.Count > 0)
        {
            ThrowIdentityConflicts("TestSuite", conflicts);
        }

        return entries;
    }

    private static IReadOnlyDictionary<Identity, DiscoveredPlan> DiscoverPlans(string root)
    {
        var entries = new Dictionary<Identity, DiscoveredPlan>();
        var conflicts = new Dictionary<Identity, List<string>>();

        foreach (var manifestPath in Directory.EnumerateFiles(root, "plan.manifest.json", SearchOption.AllDirectories))
        {
            var manifest = JsonUtils.ReadFile<PlanManifest>(manifestPath);
            var source = JsonUtils.ReadJsonElementFile(manifestPath);
            var identity = new Identity(manifest.Id, manifest.Version);
            var folder = Path.GetDirectoryName(manifestPath) ?? root;

            ValidatePlanEnvironment(manifest.Environment, manifestPath);

            if (!entries.TryAdd(identity, new DiscoveredPlan
            {
                Identity = identity,
                Manifest = manifest,
                ManifestPath = manifestPath,
                FolderPath = folder,
                SourceManifest = source
            }))
            {
                if (!conflicts.TryGetValue(identity, out var list))
                {
                    list = new List<string>();
                    list.Add(entries[identity].ManifestPath);
                    conflicts[identity] = list;
                }

                list.Add(manifestPath);
            }
        }

        if (conflicts.Count > 0)
        {
            ThrowIdentityConflicts("TestPlan", conflicts);
        }

        return entries;
    }

    private static void ValidatePlanEnvironment(JsonElement? environment, string manifestPath)
    {
        if (environment is null || environment.Value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (environment.Value.ValueKind != JsonValueKind.Object)
        {
            throw new PcTestException(new[]
            {
                new PcTestError("Plan.Environment.Invalid", $"Plan environment must be an object in {manifestPath}.")
            });
        }

        foreach (var property in environment.Value.EnumerateObject())
        {
            if (!string.Equals(property.Name, "env", StringComparison.Ordinal))
            {
                throw new PcTestException(new[]
                {
                    new PcTestError("Plan.Environment.Invalid", $"Plan environment must only contain env in {manifestPath}.")
                });
            }
        }

        if (environment.Value.TryGetProperty("env", out var envElement))
        {
            if (envElement.ValueKind != JsonValueKind.Object)
            {
                throw new PcTestException(new[]
                {
                    new PcTestError("Plan.Environment.Invalid", $"Plan environment.env must be an object in {manifestPath}.")
                });
            }

            foreach (var envProp in envElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(envProp.Name))
                {
                    throw new PcTestException(new[]
                    {
                        new PcTestError("Plan.Environment.Invalid", $"Plan environment.env has empty key in {manifestPath}.")
                    });
                }

                if (envProp.Value.ValueKind != JsonValueKind.String)
                {
                    throw new PcTestException(new[]
                    {
                        new PcTestError("Plan.Environment.Invalid", $"Plan environment.env values must be strings in {manifestPath}.")
                    });
                }
            }
        }
    }

    private static void ThrowIdentityConflicts(string entityType, Dictionary<Identity, List<string>> conflicts)
    {
        var errors = new List<PcTestError>();
        foreach (var conflict in conflicts)
        {
            var payload = new Dictionary<string, object?>
            {
                ["entityType"] = entityType,
                ["id"] = conflict.Key.Id,
                ["version"] = conflict.Key.Version,
                ["conflictPaths"] = conflict.Value
            };

            errors.Add(new PcTestError("Identity.Duplicate", $"Duplicate {entityType} identity {conflict.Key}.", JsonUtils.ToJsonElement(payload)));
        }

        throw new PcTestException(errors);
    }
}
