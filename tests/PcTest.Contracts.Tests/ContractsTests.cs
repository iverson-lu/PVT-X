using PcTest.Contracts;
using Xunit;

namespace PcTest.Contracts.Tests;

public sealed class ContractsTests
{
    [Fact]
    public void Identity_Parses_IdAtVersion()
    {
        Identity identity = Identity.Parse("CpuStress@2.0.0");
        Assert.Equal("CpuStress", identity.Id);
        Assert.Equal("2.0.0", identity.Version);
    }
}
