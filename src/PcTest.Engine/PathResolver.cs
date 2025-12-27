using PcTest.Contracts;

namespace PcTest.Engine;

public static class PathResolver
{
    public static (string? resolvedPath, ValidationError? error) ResolveTestCaseRef(string suitePath, string refValue, string testCaseRoot)
    {
        var expectedRoot = PathUtilities.NormalizePath(testCaseRoot);
        var combined = PathUtilities.NormalizePath(Path.Combine(expectedRoot, refValue));

        if (!PathUtilities.IsContainedBy(combined, expectedRoot))
        {
            return (null, BuildSuiteRefError(suitePath, refValue, combined, expectedRoot, "OutOfRoot"));
        }

        if (!Directory.Exists(combined))
        {
            return (null, BuildSuiteRefError(suitePath, refValue, combined, expectedRoot, "NotFound"));
        }

        var resolved = ResolveReparse(combined);
        if (!PathUtilities.IsContainedBy(resolved, expectedRoot))
        {
            return (null, BuildSuiteRefError(suitePath, refValue, resolved, expectedRoot, "OutOfRoot"));
        }

        var manifestPath = Path.Combine(resolved, "test.manifest.json");
        if (!File.Exists(manifestPath))
        {
            return (null, BuildSuiteRefError(suitePath, refValue, resolved, expectedRoot, "MissingManifest"));
        }

        return (manifestPath, null);
    }

    private static string ResolveReparse(string path)
    {
        var info = new DirectoryInfo(path);
        if (!info.Exists)
        {
            return path;
        }
        if (!info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return path;
        }
        var resolved = info.ResolveLinkTarget(true);
        return resolved?.FullName ?? path;
    }

    private static ValidationError BuildSuiteRefError(string suitePath, string refValue, string resolvedPath, string expectedRoot, string reason)
    {
        return new ValidationError(ErrorCodes.SuiteTestCaseRefInvalid, "Suite TestCase ref resolution failed.", new
        {
            entityType = "suite",
            suitePath,
            @ref = refValue,
            resolvedPath,
            expectedRoot,
            reason
        });
    }
}
