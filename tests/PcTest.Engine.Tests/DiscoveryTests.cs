using PcTest.Contracts;
using PcTest.Engine;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class DiscoveryTests
{
    [Fact]
    public void DuplicateIdentity_ReturnsConflictPaths()
    {
        using TempDirectory temp = new();
        string root = temp.CreateSubdirectory("cases");
        CreateTestCase(root, "CaseA", "1.0.0", "A");
        CreateTestCase(root, "CaseA", "1.0.0", "B");

        DiscoveryResult result = new DiscoveryService().Discover(new DiscoveryOptions
        {
            TestCaseRoot = root,
            TestSuiteRoot = string.Empty,
            TestPlanRoot = string.Empty
        });

        ValidationError? error = result.Validation.Errors.FirstOrDefault(e => e.Code == "Identity.Duplicate");
        Assert.NotNull(error);
        Assert.True(((IEnumerable<string>)error.Payload["conflictPaths"]!).Count() == 2);
    }

    [Fact]
    public void SuiteRef_OutOfRoot_IsFlagged()
    {
        using TempDirectory temp = new();
        string caseRoot = temp.CreateSubdirectory("cases");
        string suiteRoot = temp.CreateSubdirectory("suites");
        CreateTestCase(caseRoot, "CaseA", "1.0.0", "A");

        string suitePath = Path.Combine(suiteRoot, "Suite1");
        Directory.CreateDirectory(suitePath);
        string outOfRootRef = $"..{Path.DirectorySeparatorChar}outside";
        string escapedRef = outOfRootRef.Replace(\"\\\\\", \"\\\\\\\\\", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(suitePath, "suite.manifest.json"), $"{{\"schemaVersion\":\"1.5.0\",\"id\":\"Suite\",\"name\":\"Suite\",\"version\":\"1.0.0\",\"testCases\":[{{\"nodeId\":\"n1\",\"ref\":\"{escapedRef}\"}}]}}");

        DiscoveryResult result = new DiscoveryService().Discover(new DiscoveryOptions
        {
            TestCaseRoot = caseRoot,
            TestSuiteRoot = suiteRoot,
            TestPlanRoot = string.Empty
        });

        ValidationError? error = result.Validation.Errors.FirstOrDefault(e => e.Code == "Suite.TestCaseRef.Invalid");
        Assert.NotNull(error);
        Assert.Equal("OutOfRoot", error.Payload["reason"]);
    }

    [Fact]
    public void SuiteRef_Symlink_OutOfRoot_IsFlagged()
    {
        using TempDirectory temp = new();
        string caseRoot = temp.CreateSubdirectory("cases");
        string suiteRoot = temp.CreateSubdirectory("suites");

        string outside = temp.CreateSubdirectory("outside");
        CreateTestCase(outside, "CaseB", "1.0.0", "B");

        string linkPath = Path.Combine(caseRoot, "LinkOut");
        try
        {
            Directory.CreateSymbolicLink(linkPath, Path.Combine(outside, "B"));
        }
        catch (Exception)
        {
            return;
        }

        string suitePath = Path.Combine(suiteRoot, "Suite2");
        Directory.CreateDirectory(suitePath);
        File.WriteAllText(Path.Combine(suitePath, "suite.manifest.json"), "{\"schemaVersion\":\"1.5.0\",\"id\":\"Suite2\",\"name\":\"Suite2\",\"version\":\"1.0.0\",\"testCases\":[{\"nodeId\":\"n1\",\"ref\":\"LinkOut\"}]}");

        DiscoveryResult result = new DiscoveryService().Discover(new DiscoveryOptions
        {
            TestCaseRoot = caseRoot,
            TestSuiteRoot = suiteRoot,
            TestPlanRoot = string.Empty
        });

        ValidationError? error = result.Validation.Errors.FirstOrDefault(e => e.Code == "Suite.TestCaseRef.Invalid");
        Assert.NotNull(error);
        Assert.Equal("OutOfRoot", error.Payload["reason"]);
    }

    private static void CreateTestCase(string root, string id, string version, string folderName)
    {
        string folder = Path.Combine(root, folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "test.manifest.json"), $"{{\"schemaVersion\":\"1.5.0\",\"id\":\"{id}\",\"name\":\"{id}\",\"category\":\"Demo\",\"version\":\"{version}\"}}");
    }
}

internal sealed class TempDirectory : IDisposable
{
    private readonly string _path;

    public TempDirectory()
    {
        _path = Path.Combine(Path.GetTempPath(), "pctest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_path);
    }

    public string CreateSubdirectory(string name)
    {
        string path = Path.Combine(_path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_path))
        {
            Directory.Delete(_path, true);
        }
    }
}
