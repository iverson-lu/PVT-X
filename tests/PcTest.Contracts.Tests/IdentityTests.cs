using PcTest.Contracts;
using Xunit;

namespace PcTest.Contracts.Tests;

public sealed class IdentityTests
{
    [Fact]
    public void Parse_IdAtVersion_Works()
    {
        Identity identity = Identity.Parse("CpuStress@2.0.0");
        Assert.Equal("CpuStress", identity.Id);
        Assert.Equal("2.0.0", identity.Version);
    }

    [Theory]
    [InlineData("NoAt")]
    [InlineData("@1.0.0")]
    [InlineData("id@")]
    public void Parse_Invalid_Throws(string value)
    {
        Assert.Throws<FormatException>(() => Identity.Parse(value));
    }
}
