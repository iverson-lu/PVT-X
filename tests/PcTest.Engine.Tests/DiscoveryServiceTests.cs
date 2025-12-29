using System.Text;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Contracts.Validation;
using PcTest.Engine.Discovery;
using Xunit;

namespace PcTest.Engine.Tests;

/// <summary>
/// Tests for discovery service per spec section 5.
/// </summary>
public class DiscoveryServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _casesRoot;
    private readonly string _suitesRoot;
    private readonly string _plansRoot;

    public DiscoveryServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"PcTest_{Guid.NewGuid():N}");
        _casesRoot = Path.Combine(_tempRoot, "TestCases");
        _suitesRoot = Path.Combine(_tempRoot, "TestSuites");
        _plansRoot = Path.Combine(_tempRoot, "TestPlans");

        Directory.CreateDirectory(_casesRoot);
        Directory.CreateDirectory(_suitesRoot);
        Directory.CreateDirectory(_plansRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public void Discover_EmptyRoots_ReturnsEmptyResult()
    {
        var service = new DiscoveryService();

        var result = service.Discover(_casesRoot, _suitesRoot, _plansRoot);

        Assert.Empty(result.TestCases);
        Assert.Empty(result.TestSuites);
        Assert.Empty(result.TestPlans);
    }

    [Fact]
    public void Discover_FindsTestCase()
    {
        CreateTestCase("MyTest", "1.0.0");
        var service = new DiscoveryService();

        var result = service.Discover(_casesRoot, _suitesRoot, _plansRoot);

        Assert.Single(result.TestCases);
        Assert.True(result.TestCases.ContainsKey("MyTest@1.0.0"));
        Assert.Equal("MyTest", result.TestCases["MyTest@1.0.0"].Manifest.Id);
    }

    [Fact]
    public void Discover_FindsTestSuite()
    {
        CreateTestSuite("MySuite", "1.0.0");
        var service = new DiscoveryService();

        var result = service.Discover(_casesRoot, _suitesRoot, _plansRoot);

        Assert.Single(result.TestSuites);
        Assert.True(result.TestSuites.ContainsKey("MySuite@1.0.0"));
    }

    [Fact]
    public void Discover_FindsTestPlan()
    {
        CreateTestPlan("MyPlan", "1.0.0");
        var service = new DiscoveryService();

        var result = service.Discover(_casesRoot, _suitesRoot, _plansRoot);

        Assert.Single(result.TestPlans);
        Assert.True(result.TestPlans.ContainsKey("MyPlan@1.0.0"));
    }

    [Fact]
    public void Discover_DuplicateIdentity_ThrowsWithConflictPaths()
    {
        // Create two test cases with same id@version in different folders
        CreateTestCaseInFolder("MyTest", "1.0.0", "Folder1");
        CreateTestCaseInFolder("MyTest", "1.0.0", "Folder2");

        var service = new DiscoveryService();

        var ex = Assert.Throws<ValidationException>(() =>
            service.Discover(_casesRoot, _suitesRoot, _plansRoot));

        Assert.Contains(ex.Result.Errors, e => e.Code == ErrorCodes.DuplicateIdentity);
        var error = ex.Result.Errors.First(e => e.Code == ErrorCodes.DuplicateIdentity);
        Assert.NotNull(error.ConflictPaths);
        Assert.Equal(2, error.ConflictPaths.Count);
    }

    [Fact]
    public void Discover_SameIdDifferentVersion_BothDiscovered()
    {
        CreateTestCase("MyTest", "1.0.0");
        CreateTestCaseInFolder("MyTest", "2.0.0", "MyTestV2");
        var service = new DiscoveryService();

        var result = service.Discover(_casesRoot, _suitesRoot, _plansRoot);

        Assert.Equal(2, result.TestCases.Count);
        Assert.True(result.TestCases.ContainsKey("MyTest@1.0.0"));
        Assert.True(result.TestCases.ContainsKey("MyTest@2.0.0"));
    }

    [Fact]
    public void Discover_RecursiveSearch_FindsNestedManifests()
    {
        // Create nested structure
        var nestedPath = Path.Combine(_casesRoot, "Category", "SubCategory", "DeepTest");
        Directory.CreateDirectory(nestedPath);
        var manifest = new TestCaseManifest
        {
            Id = "DeepTest",
            Version = "1.0.0",
            Name = "Deep Test",
            Category = "Nested"
        };
        File.WriteAllText(Path.Combine(nestedPath, "test.manifest.json"),
            JsonDefaults.Serialize(manifest), Encoding.UTF8);

        var service = new DiscoveryService();
        var result = service.Discover(_casesRoot, _suitesRoot, _plansRoot);

        Assert.Single(result.TestCases);
        Assert.True(result.TestCases.ContainsKey("DeepTest@1.0.0"));
    }

    private void CreateTestCase(string id, string version)
    {
        CreateTestCaseInFolder(id, version, id);
    }

    private void CreateTestCaseInFolder(string id, string version, string folderName)
    {
        var folder = Path.Combine(_casesRoot, folderName);
        Directory.CreateDirectory(folder);
        var manifest = new TestCaseManifest
        {
            Id = id,
            Version = version,
            Name = $"{id} Test",
            Category = "Test"
        };
        File.WriteAllText(Path.Combine(folder, "test.manifest.json"),
            JsonDefaults.Serialize(manifest), Encoding.UTF8);
    }

    private void CreateTestSuite(string id, string version)
    {
        var folder = Path.Combine(_suitesRoot, id);
        Directory.CreateDirectory(folder);
        var manifest = new TestSuiteManifest
        {
            Id = id,
            Version = version,
            Name = $"{id} Suite"
        };
        File.WriteAllText(Path.Combine(folder, "suite.manifest.json"),
            JsonDefaults.Serialize(manifest), Encoding.UTF8);
    }

    private void CreateTestPlan(string id, string version)
    {
        var folder = Path.Combine(_plansRoot, id);
        Directory.CreateDirectory(folder);
        var manifest = new TestPlanManifest
        {
            Id = id,
            Version = version,
            Name = $"{id} Plan"
        };
        File.WriteAllText(Path.Combine(folder, "plan.manifest.json"),
            JsonDefaults.Serialize(manifest), Encoding.UTF8);
    }
}
