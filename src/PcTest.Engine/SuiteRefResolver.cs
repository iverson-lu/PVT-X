using PcTest.Contracts;

namespace PcTest.Engine;

public sealed class SuiteRefResolution
{
    public SuiteRefResolution(string resolvedPath, string manifestPath)
    {
        ResolvedPath = resolvedPath;
        ManifestPath = manifestPath;
    }

    public string ResolvedPath { get; }
    public string ManifestPath { get; }
}

public static class SuiteRefResolver
{
    public static SuiteRefResolution ResolveSuiteTestCaseRef(string testCaseRoot, string suitePath, string reference)
    {
        string expectedRoot = PathUtilities.GetCanonicalPath(testCaseRoot);
        string resolved = PathUtilities.GetCanonicalPath(Path.Combine(testCaseRoot, reference));
        if (!PathUtilities.IsContained(expectedRoot, resolved))
        {
            throw new PcTestException("Suite.TestCaseRef.Invalid", "Suite ref out of root.", new Dictionary<string, object?>
            {
                ["entityType"] = "TestSuite",
                ["suitePath"] = suitePath,
                ["ref"] = reference,
                ["resolvedPath"] = resolved,
                ["expectedRoot"] = expectedRoot,
                ["reason"] = "OutOfRoot"
            });
        }

        if (!Directory.Exists(resolved))
        {
            throw new PcTestException("Suite.TestCaseRef.Invalid", "Suite ref folder missing.", new Dictionary<string, object?>
            {
                ["entityType"] = "TestSuite",
                ["suitePath"] = suitePath,
                ["ref"] = reference,
                ["resolvedPath"] = resolved,
                ["expectedRoot"] = expectedRoot,
                ["reason"] = "NotFound"
            });
        }

        string manifestPath = Path.Combine(resolved, "test.manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new PcTestException("Suite.TestCaseRef.Invalid", "Suite ref missing manifest.", new Dictionary<string, object?>
            {
                ["entityType"] = "TestSuite",
                ["suitePath"] = suitePath,
                ["ref"] = reference,
                ["resolvedPath"] = resolved,
                ["expectedRoot"] = expectedRoot,
                ["reason"] = "MissingManifest"
            });
        }

        return new SuiteRefResolution(resolved, manifestPath);
    }
}
