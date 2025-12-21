using PcTest.Contracts.Manifest;
using PcTest.Engine.Validation;
using Xunit;

namespace PcTest.Tests;

public class ManifestValidatorTests
{
    [Fact]
    public void Validate_ThrowsOnMissingId()
    {
        var manifest = new TestManifest { Name = "Test", Version = "1.0", Category = "Cat" };
        Assert.Throws<InvalidDataException>(() => ManifestValidator.Validate(manifest));
    }

    [Fact]
    public void Validate_ThrowsOnDuplicateParameters()
    {
        var manifest = new TestManifest
        {
            Id = "Sample",
            Name = "Test",
            Version = "1.0",
            Category = "Cat",
            Parameters = new[]
            {
                new ParameterDefinition { Name = "One", Type = "string" },
                new ParameterDefinition { Name = "one", Type = "string" }
            }
        };

        var ex = Assert.Throws<InvalidDataException>(() => ManifestValidator.Validate(manifest));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact]
    public void Validate_ThrowsOnMissingType()
    {
        var manifest = new TestManifest
        {
            Id = "Sample",
            Name = "Test",
            Version = "1.0",
            Category = "Cat",
            Parameters = new[]
            {
                new ParameterDefinition { Name = "One" }
            }
        };

        var ex = Assert.Throws<InvalidDataException>(() => ManifestValidator.Validate(manifest));
        Assert.Contains("type", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
