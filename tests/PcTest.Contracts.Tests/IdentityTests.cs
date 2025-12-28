using PcTest.Contracts;
using Xunit;

namespace PcTest.Contracts.Tests;

public sealed class IdentityTests
{
    [Theory]
    [InlineData("CpuStress@1.0.0", "CpuStress", "1.0.0")]
    [InlineData("ThermalSuite@2.1", "ThermalSuite", "2.1")]
    public void ParseValidIdentity(string value, string id, string version)
    {
        var identity = Identity.Parse(value);
        Assert.Equal(id, identity.Id);
        Assert.Equal(version, identity.Version);
    }

    [Theory]
    [InlineData("CpuStress", "Identity must contain exactly one '@'.")]
    [InlineData("Cpu@Stress@1.0", "Identity must contain exactly one '@'.")]
    [InlineData(" Cpu@1.0", "Identity contains leading or trailing whitespace.")]
    [InlineData("Cpu@1.0 ", "Identity contains leading or trailing whitespace.")]
    [InlineData("Cpu Stress@1.0", "Identity contains whitespace.")]
    [InlineData("Cpu!@1.0", "Identity id contains invalid characters.")]
    public void RejectsInvalidIdentity(string value, string message)
    {
        var ex = Assert.Throws<ArgumentException>(() => Identity.Parse(value));
        Assert.Contains(message, ex.Message);
    }
}
