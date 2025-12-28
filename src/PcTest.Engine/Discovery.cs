using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine;

public sealed record DiscoveryRequest(string TestCaseRoot, string SuiteRoot, string PlanRoot);

public sealed record DiscoveredTestCase(TestCaseManifest Manifest, string ManifestPath, string FolderPath, JsonElement Source);
public sealed record DiscoveredSuite(SuiteManifest Manifest, string ManifestPath, string FolderPath, JsonElement Source);
public sealed record DiscoveredPlan(PlanManifest Manifest, string ManifestPath, string FolderPath, JsonElement Source);

public sealed record DiscoveryResult(
    IReadOnlyDictionary<Identity, DiscoveredTestCase> TestCases,
    IReadOnlyDictionary<Identity, DiscoveredSuite> Suites,
    IReadOnlyDictionary<Identity, DiscoveredPlan> Plans);

public sealed class DiscoveryService
{
    public DiscoveryResult Discover(DiscoveryRequest request)
    {
        var testCases = DiscoverTestCases(request.TestCaseRoot);
        var suites = DiscoverSuites(request.SuiteRoot);
        var plans = DiscoverPlans(request.PlanRoot);
        return new DiscoveryResult(testCases, suites, plans);
    }

    private static IReadOnlyDictionary<Identity, DiscoveredTestCase> DiscoverTestCases(string root)
    {
        var manifests = new Dictionary<Identity, DiscoveredTestCase>();
        var conflicts = new Dictionary<Identity, List<string>>();
        var errors = new List<ValidationError>();
        foreach (var path in Directory.EnumerateFiles(root, "test.manifest.json", SearchOption.AllDirectories))
        {
            using var doc = JsonUtils.ReadJsonDocument(path);
            var manifest = doc.RootElement.Deserialize<TestCaseManifest>(JsonUtils.SerializerOptions)
                ?? throw new InvalidDataException($"Invalid manifest: {path}");
            var identity = new Identity(manifest.Id, manifest.Version);
            ValidateParameters(manifest.Parameters, errors, path);

            if (manifests.ContainsKey(identity))
            {
                if (!conflicts.TryGetValue(identity, out var list))
                {
                    list = new List<string> { manifests[identity].ManifestPath };
                    conflicts[identity] = list;
                }

                list.Add(path);
            }
            else
            {
                manifests[identity] = new DiscoveredTestCase(manifest, path, Path.GetDirectoryName(path) ?? root, doc.RootElement.Clone());
            }
        }

        foreach (var conflict in conflicts)
        {
            errors.Add(new ValidationError("Identity.Duplicate", new Dictionary<string, object>
            {
                ["entityType"] = "TestCase",
                ["id"] = conflict.Key.Id,
                ["version"] = conflict.Key.Version,
                ["conflictPaths"] = conflict.Value.ToArray()
            }));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return manifests;
    }

    private static IReadOnlyDictionary<Identity, DiscoveredSuite> DiscoverSuites(string root)
    {
        var manifests = new Dictionary<Identity, DiscoveredSuite>();
        var conflicts = new Dictionary<Identity, List<string>>();
        var errors = new List<ValidationError>();
        foreach (var path in Directory.EnumerateFiles(root, "suite.manifest.json", SearchOption.AllDirectories))
        {
            using var doc = JsonUtils.ReadJsonDocument(path);
            var manifest = doc.RootElement.Deserialize<SuiteManifest>(JsonUtils.SerializerOptions)
                ?? throw new InvalidDataException($"Invalid manifest: {path}");
            var identity = new Identity(manifest.Id, manifest.Version);

            if (manifests.ContainsKey(identity))
            {
                if (!conflicts.TryGetValue(identity, out var list))
                {
                    list = new List<string> { manifests[identity].ManifestPath };
                    conflicts[identity] = list;
                }

                list.Add(path);
            }
            else
            {
                manifests[identity] = new DiscoveredSuite(manifest, path, Path.GetDirectoryName(path) ?? root, doc.RootElement.Clone());
            }
        }

        foreach (var conflict in conflicts)
        {
            errors.Add(new ValidationError("Identity.Duplicate", new Dictionary<string, object>
            {
                ["entityType"] = "TestSuite",
                ["id"] = conflict.Key.Id,
                ["version"] = conflict.Key.Version,
                ["conflictPaths"] = conflict.Value.ToArray()
            }));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return manifests;
    }

    private static IReadOnlyDictionary<Identity, DiscoveredPlan> DiscoverPlans(string root)
    {
        var manifests = new Dictionary<Identity, DiscoveredPlan>();
        var conflicts = new Dictionary<Identity, List<string>>();
        var errors = new List<ValidationError>();
        foreach (var path in Directory.EnumerateFiles(root, "plan.manifest.json", SearchOption.AllDirectories))
        {
            using var doc = JsonUtils.ReadJsonDocument(path);
            var manifest = doc.RootElement.Deserialize<PlanManifest>(JsonUtils.SerializerOptions)
                ?? throw new InvalidDataException($"Invalid manifest: {path}");
            var identity = new Identity(manifest.Id, manifest.Version);

            ValidatePlanEnvironment(doc.RootElement, errors, path);

            if (manifests.ContainsKey(identity))
            {
                if (!conflicts.TryGetValue(identity, out var list))
                {
                    list = new List<string> { manifests[identity].ManifestPath };
                    conflicts[identity] = list;
                }

                list.Add(path);
            }
            else
            {
                manifests[identity] = new DiscoveredPlan(manifest, path, Path.GetDirectoryName(path) ?? root, doc.RootElement.Clone());
            }
        }

        foreach (var conflict in conflicts)
        {
            errors.Add(new ValidationError("Identity.Duplicate", new Dictionary<string, object>
            {
                ["entityType"] = "TestPlan",
                ["id"] = conflict.Key.Id,
                ["version"] = conflict.Key.Version,
                ["conflictPaths"] = conflict.Value.ToArray()
            }));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return manifests;
    }

    private static void ValidateParameters(ParameterDefinition[]? parameters, List<ValidationError> errors, string path)
    {
        if (parameters is null)
        {
            return;
        }

        foreach (var parameter in parameters)
        {
            if (!ParameterTypes.Supported.Contains(parameter.Type))
            {
                errors.Add(new ValidationError("Parameter.Type.Unsupported", new Dictionary<string, object>
                {
                    ["path"] = path,
                    ["parameter"] = parameter.Name,
                    ["type"] = parameter.Type
                }));
            }

            if (parameter.Type.StartsWith("enum", StringComparison.OrdinalIgnoreCase) && (parameter.EnumValues is null || parameter.EnumValues.Length == 0))
            {
                errors.Add(new ValidationError("Parameter.Enum.MissingValues", new Dictionary<string, object>
                {
                    ["path"] = path,
                    ["parameter"] = parameter.Name
                }));
            }
        }
    }

    private static void ValidatePlanEnvironment(JsonElement root, List<ValidationError> errors, string path)
    {
        if (!root.TryGetProperty("environment", out var envElement) || envElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var prop in envElement.EnumerateObject())
        {
            if (!prop.NameEquals("env"))
            {
                errors.Add(new ValidationError("Plan.Environment.Invalid", new Dictionary<string, object>
                {
                    ["path"] = path,
                    ["key"] = prop.Name
                }));
            }
        }
    }
}
