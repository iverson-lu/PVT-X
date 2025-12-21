using PcTest.Contracts.Manifest;
using PcTest.Engine.Validation;
using Xunit;

namespace PcTest.Tests;

public class ParameterBinderTests
{
    [Fact]
    public void Bind_AppliesDefault_WhenMissing()
    {
        var manifest = new TestManifest
        {
            Parameters = new[]
            {
                new ParameterDefinition { Name = "Name", Type = "string", Default = "DefaultName" }
            }
        };

        var result = ParameterBinder.Bind(manifest, new Dictionary<string, string>());

        Assert.Equal("DefaultName", result["Name"].Value);
        Assert.True(result["Name"].IsSupplied);
    }

    [Fact]
    public void Bind_RequiredMissing_Throws()
    {
        var manifest = new TestManifest
        {
            Parameters = new[]
            {
                new ParameterDefinition { Name = "Count", Type = "int", Required = true }
            }
        };

        var ex = Assert.Throws<InvalidDataException>(() => ParameterBinder.Bind(manifest, new Dictionary<string, string>()));
        Assert.Contains("Missing required parameter", ex.Message);
    }

    [Fact]
    public void Bind_PatternMismatch_Throws()
    {
        var manifest = new TestManifest
        {
            Parameters = new[]
            {
                new ParameterDefinition { Name = "Code", Type = "string", Pattern = "^[A-Z]{3}$" }
            }
        };

        var ex = Assert.Throws<InvalidDataException>(() => ParameterBinder.Bind(manifest, new Dictionary<string, string>
        {
            ["Code"] = "abc123"
        }));

        Assert.Contains("pattern", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bind_IntArrayInvalid_UsesValidationException()
    {
        var manifest = new TestManifest
        {
            Parameters = new[]
            {
                new ParameterDefinition { Name = "Numbers", Type = "int[]" }
            }
        };

        var ex = Assert.Throws<InvalidDataException>(() => ParameterBinder.Bind(manifest, new Dictionary<string, string>
        {
            ["Numbers"] = "1,two,3"
        }));

        Assert.Contains("int[]", ex.Message);
    }
}
