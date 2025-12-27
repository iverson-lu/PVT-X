using PcTest.Contracts;

namespace PcTest.Engine;

public sealed class SuiteRefResolver
{
    public string ResolveTestCaseManifest(string suitePath, string resolvedTestCaseRoot, string reference)
    {
        var expectedRoot = PathUtilities.NormalizePath(resolvedTestCaseRoot);
        string resolvedPath;
        try
        {
            resolvedPath = PathUtilities.ResolvePathWithReparsePoints(expectedRoot, reference);
        }
        catch (Exception ex)
        {
            throw CreateRefError(suitePath, reference, expectedRoot, expectedRoot, "OutOfRoot", ex.Message);
        }

        if (!PathUtilities.IsPathContained(expectedRoot, resolvedPath))
        {
            throw CreateRefError(suitePath, reference, resolvedPath, expectedRoot, "OutOfRoot", null);
        }

        if (!Directory.Exists(resolvedPath))
        {
            throw CreateRefError(suitePath, reference, resolvedPath, expectedRoot, "NotFound", null);
        }

        var manifestPath = Path.Combine(resolvedPath, "test.manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw CreateRefError(suitePath, reference, resolvedPath, expectedRoot, "MissingManifest", null);
        }

        return PathUtilities.NormalizePath(manifestPath);
    }

    private static EngineException CreateRefError(string suitePath, string reference, string resolvedPath, string expectedRoot, string reason, string? message)
    {
        var payload = new Dictionary<string, object?>
        {
            ["entityType"] = "TestSuite",
            ["suitePath"] = suitePath,
            ["ref"] = reference,
            ["resolvedPath"] = resolvedPath,
            ["expectedRoot"] = expectedRoot,
            ["reason"] = reason
        };
        return new EngineException(SchemaConstants.SuiteTestCaseRefError, message ?? "Suite testCase ref invalid.", payload);
    }
}
