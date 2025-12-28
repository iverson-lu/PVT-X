using PcTest.Cli;
using Xunit;

namespace PcTest.Cli.Tests;

public sealed class ProgramTests
{
    [Fact]
    public async Task Main_ReturnsErrorOnEmptyArgs()
    {
        int result = await Program.Main(Array.Empty<string>());
        Assert.Equal(1, result);
    }
}
