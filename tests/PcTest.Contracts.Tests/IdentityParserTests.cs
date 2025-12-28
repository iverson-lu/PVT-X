using PcTest.Contracts;
using Xunit;

namespace PcTest.Contracts.Tests;

public sealed class IdentityParserTests
{
    [Fact]
    public void Parse_ValidIdentity_ReturnsParts()
    {
        var identity = IdentityParser.Parse("CpuStress@1.0.0");
        Assert.Equal("CpuStress", identity.Id);
        Assert.Equal("1.0.0", identity.Version);
    }

    [Theory]
    [InlineData("CpuStress")]
    [InlineData("CpuStress@")]
    [InlineData("@1.0.0")]
    [InlineData("Cpu Stress@1.0.0")]
    [InlineData("CpuStress@1.0.0@extra")]
    public void Parse_InvalidIdentity_Throws(string input)
    {
        Assert.Throws<ValidationException>(() => IdentityParser.Parse(input));
    }
}
