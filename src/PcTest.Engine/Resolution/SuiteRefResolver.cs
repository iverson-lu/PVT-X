using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Contracts.Requests;
using PcTest.Contracts.Validation;
using PcTest.Engine.Discovery;

namespace PcTest.Engine.Resolution;

/// <summary>
/// Resolves Suite.testCases[].ref to Test Case manifests per spec section 5.2 and 6.3.
/// </summary>
public sealed class SuiteRefResolver
{
    private readonly string _testCaseRoot;

    public SuiteRefResolver(string testCaseRoot)
    {
        _testCaseRoot = PathUtils.NormalizePath(testCaseRoot);
    }

    /// <summary>
    /// Resolves a Suite ref to a Test Case manifest.
    /// Per spec section 5.2 and 6.3:
    /// - ref resolves to &lt;ResolvedTestCaseRoot&gt;/&lt;ref&gt;/test.manifest.json
    /// - Must be contained within ResolvedTestCaseRoot
    /// - Symlinks/junctions must be resolved
    /// </summary>
    public (TestCaseManifest? Manifest, string? ResolvedPath, ValidationError? Error) ResolveRef(
        string suiteManifestPath,
        string nodeRef)
    {
        // Normalize the ref path against the test case root
        var targetPath = PathUtils.Combine(_testCaseRoot, nodeRef);
        var manifestPath = Path.Combine(targetPath, "test.manifest.json");

        // Check containment with symlink resolution
        var (isContained, resolvedPath, containmentError) = PathUtils.CheckContainmentWithSymlinkResolution(
            targetPath, _testCaseRoot);

        if (!isContained)
        {
            return (null, resolvedPath, new ValidationError
            {
                Code = ErrorCodes.SuiteTestCaseRefInvalid,
                Message = $"Suite ref '{nodeRef}' resolves outside TestCaseRoot",
                EntityType = "Suite",
                Location = suiteManifestPath,
                Reason = RefInvalidReasons.OutOfRoot,
                Data = new Dictionary<string, object?>
                {
                    ["suitePath"] = suiteManifestPath,
                    ["ref"] = nodeRef,
                    ["resolvedPath"] = resolvedPath ?? targetPath,
                    ["expectedRoot"] = _testCaseRoot,
                    ["reason"] = RefInvalidReasons.OutOfRoot
                }
            });
        }

        var actualTargetPath = resolvedPath ?? targetPath;
        var actualManifestPath = Path.Combine(actualTargetPath, "test.manifest.json");

        // Check if target folder exists
        if (!Directory.Exists(actualTargetPath))
        {
            return (null, actualTargetPath, new ValidationError
            {
                Code = ErrorCodes.SuiteTestCaseRefInvalid,
                Message = $"Suite ref '{nodeRef}' target folder not found",
                EntityType = "Suite",
                Location = suiteManifestPath,
                Reason = RefInvalidReasons.NotFound,
                Data = new Dictionary<string, object?>
                {
                    ["suitePath"] = suiteManifestPath,
                    ["ref"] = nodeRef,
                    ["resolvedPath"] = actualTargetPath,
                    ["expectedRoot"] = _testCaseRoot,
                    ["reason"] = RefInvalidReasons.NotFound
                }
            });
        }

        // Check if manifest exists
        if (!File.Exists(actualManifestPath))
        {
            return (null, actualTargetPath, new ValidationError
            {
                Code = ErrorCodes.SuiteTestCaseRefInvalid,
                Message = $"Suite ref '{nodeRef}' missing test.manifest.json",
                EntityType = "Suite",
                Location = suiteManifestPath,
                Reason = RefInvalidReasons.MissingManifest,
                Data = new Dictionary<string, object?>
                {
                    ["suitePath"] = suiteManifestPath,
                    ["ref"] = nodeRef,
                    ["resolvedPath"] = actualTargetPath,
                    ["expectedRoot"] = _testCaseRoot,
                    ["reason"] = RefInvalidReasons.MissingManifest
                }
            });
        }

        // Load and parse manifest
        try
        {
            var json = File.ReadAllText(actualManifestPath, System.Text.Encoding.UTF8);
            var manifest = JsonDefaults.Deserialize<TestCaseManifest>(json);
            if (manifest is null)
            {
                return (null, actualTargetPath, new ValidationError
                {
                    Code = ErrorCodes.SuiteTestCaseRefInvalid,
                    Message = $"Suite ref '{nodeRef}' has invalid manifest",
                    EntityType = "Suite",
                    Location = suiteManifestPath,
                    Reason = RefInvalidReasons.MissingManifest,
                    Data = new Dictionary<string, object?>
                    {
                        ["suitePath"] = suiteManifestPath,
                        ["ref"] = nodeRef,
                        ["resolvedPath"] = actualTargetPath,
                        ["expectedRoot"] = _testCaseRoot,
                        ["reason"] = RefInvalidReasons.MissingManifest
                    }
                });
            }

            return (manifest, actualTargetPath, null);
        }
        catch (Exception ex)
        {
            return (null, actualTargetPath, new ValidationError
            {
                Code = ErrorCodes.SuiteTestCaseRefInvalid,
                Message = $"Suite ref '{nodeRef}' manifest parse error: {ex.Message}",
                EntityType = "Suite",
                Location = suiteManifestPath,
                Reason = RefInvalidReasons.MissingManifest,
                Data = new Dictionary<string, object?>
                {
                    ["suitePath"] = suiteManifestPath,
                    ["ref"] = nodeRef,
                    ["resolvedPath"] = actualTargetPath,
                    ["expectedRoot"] = _testCaseRoot,
                    ["reason"] = RefInvalidReasons.MissingManifest
                }
            });
        }
    }

    /// <summary>
    /// Validates all refs in a Suite manifest.
    /// </summary>
    public ValidationResult ValidateSuiteRefs(TestSuiteManifest suite, string suiteManifestPath)
    {
        var result = new ValidationResult();

        foreach (var node in suite.TestCases)
        {
            var (manifest, resolvedPath, error) = ResolveRef(suiteManifestPath, node.Ref);
            if (error is not null)
            {
                result.AddError(error);
            }
        }

        return result;
    }
}
