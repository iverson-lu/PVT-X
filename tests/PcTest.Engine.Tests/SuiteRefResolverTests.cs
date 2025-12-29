using System.Text;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Engine.Resolution;
using Xunit;

namespace PcTest.Engine.Tests;

/// <summary>
/// Tests for Suite ref resolution per spec section 5.2 and 6.3.
/// </summary>
public class SuiteRefResolverTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _casesRoot;

    public SuiteRefResolverTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"PcTest_{Guid.NewGuid():N}");
        _casesRoot = Path.Combine(_tempRoot, "TestCases");
        Directory.CreateDirectory(_casesRoot);
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
        catch { }
    }

    [Fact]
    public void ResolveRef_ValidRef_ReturnsManifest()
    {
        CreateTestCase("MyTest", "1.0.0");
        var resolver = new SuiteRefResolver(_casesRoot);

        var (manifest, path, error) = resolver.ResolveRef("suite.manifest.json", "MyTest");

        Assert.Null(error);
        Assert.NotNull(manifest);
        Assert.Equal("MyTest", manifest.Id);
        Assert.Equal("1.0.0", manifest.Version);
    }

    [Fact]
    public void ResolveRef_NotFound_ReturnsErrorWithReason()
    {
        var resolver = new SuiteRefResolver(_casesRoot);

        var (manifest, path, error) = resolver.ResolveRef("suite.manifest.json", "NonExistent");

        Assert.Null(manifest);
        Assert.NotNull(error);
        Assert.Equal(ErrorCodes.SuiteTestCaseRefInvalid, error.Code);
        Assert.Equal(RefInvalidReasons.NotFound, error.Reason);
    }

    [Fact]
    public void ResolveRef_MissingManifest_ReturnsErrorWithReason()
    {
        // Create folder but no manifest
        Directory.CreateDirectory(Path.Combine(_casesRoot, "EmptyFolder"));
        var resolver = new SuiteRefResolver(_casesRoot);

        var (manifest, path, error) = resolver.ResolveRef("suite.manifest.json", "EmptyFolder");

        Assert.Null(manifest);
        Assert.NotNull(error);
        Assert.Equal(ErrorCodes.SuiteTestCaseRefInvalid, error.Code);
        Assert.Equal(RefInvalidReasons.MissingManifest, error.Reason);
    }

    [Fact]
    public void ResolveRef_OutOfRoot_ReturnsErrorWithReason()
    {
        var resolver = new SuiteRefResolver(_casesRoot);

        // Try to escape using ..
        var (manifest, path, error) = resolver.ResolveRef("suite.manifest.json", "../../../etc");

        Assert.Null(manifest);
        Assert.NotNull(error);
        Assert.Equal(ErrorCodes.SuiteTestCaseRefInvalid, error.Code);
        Assert.Equal(RefInvalidReasons.OutOfRoot, error.Reason);
    }

    [Fact]
    public void ResolveRef_ErrorPayload_ContainsRequiredFields()
    {
        var resolver = new SuiteRefResolver(_casesRoot);
        var suitePath = "MySuite/suite.manifest.json";

        var (_, _, error) = resolver.ResolveRef(suitePath, "NonExistent");

        Assert.NotNull(error);
        Assert.NotNull(error.Data);
        Assert.True(error.Data.ContainsKey("suitePath"));
        Assert.True(error.Data.ContainsKey("ref"));
        Assert.True(error.Data.ContainsKey("resolvedPath"));
        Assert.True(error.Data.ContainsKey("expectedRoot"));
        Assert.True(error.Data.ContainsKey("reason"));
    }

    [Fact]
    public void ResolveRef_NestedPath_ResolvesCorrectly()
    {
        // Create nested test case
        var nestedPath = Path.Combine(_casesRoot, "Category", "SubTest");
        Directory.CreateDirectory(nestedPath);
        var manifest = new TestCaseManifest
        {
            Id = "SubTest",
            Version = "1.0.0",
            Name = "Sub Test",
            Category = "Category"
        };
        File.WriteAllText(Path.Combine(nestedPath, "test.manifest.json"),
            JsonDefaults.Serialize(manifest), Encoding.UTF8);

        var resolver = new SuiteRefResolver(_casesRoot);

        var (result, path, error) = resolver.ResolveRef("suite.manifest.json", "Category/SubTest");

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("SubTest", result.Id);
    }

    [Fact]
    public void ValidateSuiteRefs_AllValid_ReturnsSuccess()
    {
        CreateTestCase("Test1", "1.0.0");
        CreateTestCase("Test2", "1.0.0");

        var suite = new TestSuiteManifest
        {
            Id = "MySuite",
            Version = "1.0.0",
            TestCases = new List<TestCaseNode>
            {
                new() { NodeId = "node1", Ref = "Test1" },
                new() { NodeId = "node2", Ref = "Test2" }
            }
        };

        var resolver = new SuiteRefResolver(_casesRoot);
        var result = resolver.ValidateSuiteRefs(suite, "suite.manifest.json");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateSuiteRefs_InvalidRef_ReturnsErrors()
    {
        CreateTestCase("Test1", "1.0.0");

        var suite = new TestSuiteManifest
        {
            Id = "MySuite",
            Version = "1.0.0",
            TestCases = new List<TestCaseNode>
            {
                new() { NodeId = "node1", Ref = "Test1" },
                new() { NodeId = "node2", Ref = "NonExistent" }
            }
        };

        var resolver = new SuiteRefResolver(_casesRoot);
        var result = resolver.ValidateSuiteRefs(suite, "suite.manifest.json");

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    private void CreateTestCase(string id, string version)
    {
        var folder = Path.Combine(_casesRoot, id);
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
}
