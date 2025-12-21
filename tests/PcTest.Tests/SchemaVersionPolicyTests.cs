using PcTest.Contracts.Schema;
using Xunit;

namespace PcTest.Tests;

public class SchemaVersionPolicyTests
{
    [Fact]
    public void EnsureManifestSupported_AllowsV1()
    {
        SchemaVersionPolicy.EnsureManifestSupported("1.2", null);
    }

    [Fact]
    public void EnsureManifestSupported_RejectsV2()
    {
        var path = Path.GetFullPath("manifests/test.manifest.json");
        var ex = Assert.Throws<InvalidDataException>(() => SchemaVersionPolicy.EnsureManifestSupported("2.0", path));
        Assert.Contains("Supported range", ex.Message);
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void EnsureManifestSupported_RejectsMissing()
    {
        var ex = Assert.Throws<InvalidDataException>(() => SchemaVersionPolicy.EnsureManifestSupported("", "test.manifest.json"));
        Assert.Contains("test.manifest.json", ex.Message);
    }

    [Fact]
    public void ResultSchemaVersion_ReturnsMajor()
    {
        Assert.Equal("1.0", SchemaVersionPolicy.ResultSchemaVersion());
    }
}
