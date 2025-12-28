using PcTest.Contracts;
using Xunit;

namespace PcTest.Contracts.Tests;

public sealed class IdentityTests
{
    [Fact]
    public void TryParse_AcceptsValidIdentity()
    {
        bool result = Identity.TryParse("CpuStress@1.0.0", out Identity identity, out string? error);

        Assert.True(result);
        Assert.Null(error);
        Assert.Equal("CpuStress", identity.Id);
        Assert.Equal("1.0.0", identity.Version);
    }

    [Theory]
    [InlineData("NoAtSymbol")]
    [InlineData("Too@Many@At")]
    [InlineData("Bad Id@1.0.0")]
    [InlineData("CpuStress@ ")]
    public void TryParse_RejectsInvalidIdentity(string value)
    {
        bool result = Identity.TryParse(value, out _, out string? error);

        Assert.False(result);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
