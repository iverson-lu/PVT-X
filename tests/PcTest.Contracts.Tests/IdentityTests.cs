using PcTest.Contracts;
using Xunit;

namespace PcTest.Contracts.Tests;

public class IdentityTests
{
    [Fact]
    public void ParseIdentity_AllowsValidFormat()
    {
        var identity = Validation.ParseIdentity("CpuStress@1.0.0");
        Assert.Equal("CpuStress", identity.Id);
        Assert.Equal("1.0.0", identity.Version);
    }

    [Theory]
    [InlineData(" CpuStress@1.0.0")]
    [InlineData("CpuStress@1.0.0 ")]
    [InlineData("CpuStress  @1.0.0")]
    [InlineData("Cpu Stress@1.0.0")]
    [InlineData("CpuStress@@1.0.0")]
    [InlineData("CpuStress")]
    [InlineData("CpuStress@ ")]
    public void ParseIdentity_RejectsInvalidFormat(string value)
    {
        Assert.Throws<InvalidOperationException>(() => Validation.ParseIdentity(value));
    }
}
