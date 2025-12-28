using PcTest.Contracts;
using Xunit;

namespace PcTest.Contracts.Tests;

public sealed class IdentityTests
{
    [Fact]
    public void Parse_ValidIdentity_Parses()
    {
        var identity = Identity.Parse("CpuStress@1.0.0");
        Assert.Equal("CpuStress", identity.Id);
        Assert.Equal("1.0.0", identity.Version);
    }

    [Theory]
    [InlineData("CpuStress")]
    [InlineData("CpuStress@@1.0.0")]
    [InlineData("Cpu Stress@1.0.0")]
    public void Parse_InvalidIdentity_Throws(string value)
    {
        Assert.Throws<InvalidDataException>(() => Identity.Parse(value));
    }
}
