using PcTest.Engine;
using Xunit;

namespace PcTest.Engine.Tests;

public class SuiteRefSymlinkTests
{
    [Fact]
    public void SuiteRefResolution_FailsWhenSymlinkEscapesRoot()
    {
        using var temp = new TempFolder();
        var caseRoot = temp.CreateSubfolder("cases");
        var outside = temp.CreateSubfolder("outside");
        var suitePath = Path.Combine(temp.Path, "suite.manifest.json");
        var linkPath = Path.Combine(caseRoot, "LinkOut");

        try
        {
            Directory.CreateSymbolicLink(linkPath, outside);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.SkipException($"Symlink not supported: {ex.Message}");
        }

        var (resolved, error) = PathResolver.ResolveTestCaseRef(suitePath, "LinkOut", caseRoot);
        Assert.Null(resolved);
        Assert.NotNull(error);
        Assert.Equal("OutOfRoot", ((dynamic)error!.Payload!).reason);
    }
}
