using PcTest.Contracts;
using Xunit;

namespace PcTest.Contracts.Tests;

public sealed class IdentityParserTests
{
    [Fact]
    public void ParseValidIdentity()
    {
        var identity = IdentityParser.Parse("CpuStress@2.0.0");
        Assert.Equal("CpuStress", identity.Id);
        Assert.Equal("2.0.0", identity.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("CpuStress")]
    [InlineData("Cpu Stress@1.0")]
    [InlineData("CpuStress@1.0 ")]
    [InlineData("@1.0")]
    public void ParseInvalidIdentity(string value)
    {
        Assert.Throws<InvalidOperationException>(() => IdentityParser.Parse(value));
    }
}
