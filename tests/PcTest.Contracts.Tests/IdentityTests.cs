using PcTest.Contracts;
using Xunit;

namespace PcTest.Contracts.Tests;

public sealed class IdentityTests
{
    [Theory]
    [InlineData("CpuStress@1.0.0")]
    [InlineData("Test_1@0.1.2")]
    public void ParseValidIdentity(string value)
    {
        var identity = Identity.Parse(value);
        Assert.Equal(value, identity.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("NoAt")]
    [InlineData("A@@B")]
    [InlineData("Bad Id@1.0.0")]
    [InlineData("Id@1.0.0 ")]
    public void ParseInvalidIdentity(string value)
    {
        Assert.Throws<FormatException>(() => Identity.Parse(value));
    }
}
