using PcTest.Contracts;

namespace PcTest.Engine;

public sealed class DiscoveryResult
{
    public required string TestCaseRoot { get; init; }
    public required string SuiteRoot { get; init; }
    public required string PlanRoot { get; init; }

    public required IReadOnlyDictionary<Identity, ManifestRecord<TestCaseManifest>> TestCases { get; init; }
    public required IReadOnlyDictionary<Identity, ManifestRecord<SuiteManifest>> Suites { get; init; }
    public required IReadOnlyDictionary<Identity, ManifestRecord<PlanManifest>> Plans { get; init; }
}

public sealed class ManifestRecord<T>
{
    public required Identity Identity { get; init; }
    public required string Path { get; init; }
    public required T Manifest { get; init; }
}

public sealed class DiscoveryService
{
    public ValidationResult<DiscoveryResult> Discover(string testCaseRoot, string suiteRoot, string planRoot)
    {
        List<ValidationError> errors = new();
        string resolvedCaseRoot = PathUtils.GetCanonicalPath(testCaseRoot);
        string resolvedSuiteRoot = PathUtils.GetCanonicalPath(suiteRoot);
        string resolvedPlanRoot = PathUtils.GetCanonicalPath(planRoot);

        Dictionary<Identity, ManifestRecord<TestCaseManifest>> testCases = new();
        Dictionary<Identity, ManifestRecord<SuiteManifest>> suites = new();
        Dictionary<Identity, ManifestRecord<PlanManifest>> plans = new();

        errors.AddRange(LoadManifests(resolvedCaseRoot, "test.manifest.json", testCases, ValidateTestCase));
        errors.AddRange(LoadManifests(resolvedSuiteRoot, "suite.manifest.json", suites, ValidateSuite));
        errors.AddRange(LoadManifests(resolvedPlanRoot, "plan.manifest.json", plans, ValidatePlan));

        errors.AddRange(ValidateSuiteRefs(resolvedCaseRoot, suites.Values));
        errors.AddRange(ValidatePlanSuiteRefs(plans.Values, suites));

        if (errors.Count > 0)
        {
            return ValidationResult<DiscoveryResult>.Failure(errors);
        }

        return ValidationResult<DiscoveryResult>.Success(new DiscoveryResult
        {
            TestCaseRoot = resolvedCaseRoot,
            SuiteRoot = resolvedSuiteRoot,
            PlanRoot = resolvedPlanRoot,
            TestCases = testCases,
            Suites = suites,
            Plans = plans
        });
    }

    private static IEnumerable<ValidationError> LoadManifests<T>(
        string root,
        string fileName,
        Dictionary<Identity, ManifestRecord<T>> sink,
        Func<T, string, IEnumerable<ValidationError>> validator)
    {
        if (!Directory.Exists(root))
        {
            return new[] { new ValidationError("Discovery.RootMissing", $"Root not found: {root}") };
        }

        List<ValidationError> errors = new();
        Dictionary<Identity, List<string>> duplicates = new();

        foreach (string path in Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories))
        {
            T manifest;
            try
            {
                manifest = JsonHelpers.ReadJsonFile<T>(path);
            }
            catch (Exception ex)
            {
                errors.Add(new ValidationError("Manifest.ParseFailed", $"Failed to parse {path}: {ex.Message}"));
                continue;
            }

            foreach (ValidationError error in validator(manifest, path))
            {
                errors.Add(error);
            }

            Identity? identity = ExtractIdentity(manifest);
            if (identity is null)
            {
                errors.Add(new ValidationError("Manifest.IdentityMissing", $"Missing identity in {path}"));
                continue;
            }

            if (!sink.ContainsKey(identity.Value))
            {
                sink[identity.Value] = new ManifestRecord<T> { Identity = identity.Value, Manifest = manifest, Path = path };
            }

            if (!duplicates.TryGetValue(identity.Value, out List<string>? list))
            {
                list = new List<string>();
                duplicates[identity.Value] = list;
            }

            list.Add(path);
        }

        foreach (KeyValuePair<Identity, List<string>> duplicate in duplicates)
        {
            if (duplicate.Value.Count > 1)
            {
                errors.Add(new ValidationError("Identity.NotUnique", "Identity is not unique.", new Dictionary<string, object?>
                {
                    ["entityType"] = typeof(T).Name.Replace("Manifest", string.Empty, StringComparison.Ordinal),
                    ["id"] = duplicate.Key.Id,
                    ["version"] = duplicate.Key.Version,
                    ["conflictPaths"] = duplicate.Value.ToArray()
                }));
            }
        }

        return errors;
    }

    private static Identity? ExtractIdentity<T>(T manifest)
    {
        return manifest switch
        {
            TestCaseManifest test => new Identity(test.Id, test.Version),
            SuiteManifest suite => new Identity(suite.Id, suite.Version),
            PlanManifest plan => new Identity(plan.Id, plan.Version),
            _ => null
        };
    }

    private static IEnumerable<ValidationError> ValidateTestCase(TestCaseManifest manifest, string path)
    {
        List<ValidationError> errors = new();
        if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Version))
        {
            errors.Add(new ValidationError("Manifest.IdentityMissing", $"Missing id/version in {path}"));
        }

        if (manifest.Parameters is not null)
        {
            foreach (ParameterDefinition param in manifest.Parameters)
            {
                if (!ParameterTypeParser.TryParse(param.Type, out _))
                {
                    errors.Add(new ValidationError("Parameter.TypeInvalid", $"Invalid parameter type {param.Type} in {path}"));
                }
            }
        }

        return errors;
    }

    private static IEnumerable<ValidationError> ValidateSuite(SuiteManifest manifest, string path)
    {
        List<ValidationError> errors = new();
        if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Version))
        {
            errors.Add(new ValidationError("Manifest.IdentityMissing", $"Missing id/version in {path}"));
        }

        HashSet<string> nodeIds = new(StringComparer.Ordinal);
        foreach (SuiteTestCaseNode node in manifest.TestCases)
        {
            if (!nodeIds.Add(node.NodeId))
            {
                errors.Add(new ValidationError("Suite.NodeId.Duplicate", $"Duplicate nodeId {node.NodeId} in {path}"));
            }
        }

        if (manifest.Environment?.Env is not null)
        {
            foreach (KeyValuePair<string, string> pair in manifest.Environment.Env)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    errors.Add(new ValidationError("Suite.Environment.Invalid", $"Empty env key in {path}"));
                }
            }
        }

        return errors;
    }

    private static IEnumerable<ValidationError> ValidatePlan(PlanManifest manifest, string path)
    {
        List<ValidationError> errors = new();
        if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Version))
        {
            errors.Add(new ValidationError("Manifest.IdentityMissing", $"Missing id/version in {path}"));
        }

        if (manifest.Environment?.Extra is not null && manifest.Environment.Extra.Count > 0)
        {
            errors.Add(new ValidationError("Plan.Environment.Invalid", $"Plan environment has unsupported keys in {path}"));
        }

        if (manifest.Environment?.Env is not null)
        {
            foreach (KeyValuePair<string, string> pair in manifest.Environment.Env)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    errors.Add(new ValidationError("Plan.Environment.Invalid", $"Empty env key in {path}"));
                }
            }
        }

        return errors;
    }

    private static IEnumerable<ValidationError> ValidateSuiteRefs(string testCaseRoot, IEnumerable<ManifestRecord<SuiteManifest>> suites)
    {
        List<ValidationError> errors = new();
        foreach (ManifestRecord<SuiteManifest> suite in suites)
        {
            foreach (SuiteTestCaseNode node in suite.Manifest.TestCases)
            {
                ValidationError? error = ValidateSuiteRef(testCaseRoot, suite.Path, node.Ref);
                if (error is not null)
                {
                    errors.Add(error);
                }
            }
        }

        return errors;
    }

    private static ValidationError? ValidateSuiteRef(string testCaseRoot, string suitePath, string refPath)
    {
        string resolvedRoot = PathUtils.GetCanonicalPath(testCaseRoot);
        string combined = Path.GetFullPath(Path.Combine(resolvedRoot, refPath));
        string resolvedTarget = PathUtils.ResolveLinkTargetIfExists(combined);

        if (!PathUtils.IsContained(resolvedRoot, resolvedTarget))
        {
            return BuildSuiteRefError(suitePath, refPath, resolvedTarget, resolvedRoot, "OutOfRoot");
        }

        if (!Directory.Exists(resolvedTarget))
        {
            return BuildSuiteRefError(suitePath, refPath, resolvedTarget, resolvedRoot, "NotFound");
        }

        string manifestPath = Path.Combine(resolvedTarget, "test.manifest.json");
        if (!File.Exists(manifestPath))
        {
            return BuildSuiteRefError(suitePath, refPath, resolvedTarget, resolvedRoot, "MissingManifest");
        }

        return null;
    }

    private static ValidationError BuildSuiteRefError(string suitePath, string refPath, string resolvedPath, string expectedRoot, string reason)
    {
        return new ValidationError("Suite.TestCaseRef.Invalid", "Suite testCase ref is invalid.", new Dictionary<string, object?>
        {
            ["entityType"] = "TestSuite",
            ["suitePath"] = suitePath,
            ["ref"] = refPath,
            ["resolvedPath"] = resolvedPath,
            ["expectedRoot"] = expectedRoot,
            ["reason"] = reason
        });
    }

    private static IEnumerable<ValidationError> ValidatePlanSuiteRefs(
        IEnumerable<ManifestRecord<PlanManifest>> plans,
        IReadOnlyDictionary<Identity, ManifestRecord<SuiteManifest>> suites)
    {
        List<ValidationError> errors = new();
        Dictionary<string, List<string>> identityMap = suites.Values
            .GroupBy(suite => suite.Identity.ToString(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Path).ToList(), StringComparer.Ordinal);

        foreach (ManifestRecord<PlanManifest> plan in plans)
        {
            foreach (string suiteRef in plan.Manifest.Suites)
            {
                if (!Identity.TryParse(suiteRef, out Identity identity, out string? error))
                {
                    errors.Add(new ValidationError("RunRequest.Identity.Invalid", error ?? "Invalid identity", new Dictionary<string, object?>
                    {
                        ["entityType"] = "suite",
                        ["id"] = suiteRef
                    }));
                    continue;
                }

                if (!identityMap.TryGetValue(identity.ToString(), out List<string>? paths))
                {
                    errors.Add(new ValidationError("Suite.Ref.NotFound", "Suite reference not found.", new Dictionary<string, object?>
                    {
                        ["entityType"] = "suite",
                        ["id"] = identity.Id,
                        ["version"] = identity.Version,
                        ["reason"] = "NotFound"
                    }));
                    continue;
                }

                if (paths.Count > 1)
                {
                    errors.Add(new ValidationError("Suite.Ref.NonUnique", "Suite reference is not unique.", new Dictionary<string, object?>
                    {
                        ["entityType"] = "suite",
                        ["id"] = identity.Id,
                        ["version"] = identity.Version,
                        ["conflictPaths"] = paths.ToArray()
                    }));
                }
            }
        }

        return errors;
    }
}
