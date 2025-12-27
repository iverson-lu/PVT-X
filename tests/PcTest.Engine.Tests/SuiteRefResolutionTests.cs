using PcTest.Engine;
using Xunit;

namespace PcTest.Engine.Tests;

public class SuiteRefResolutionTests
{
    [Fact]
    public void SuiteRefResolution_FailsWhenOutOfRoot()
    {
        using var temp = new TempFolder();
        var caseRoot = temp.CreateSubfolder("cases");
        var suitePath = Path.Combine(temp.Path, "suite.manifest.json");

        var (resolved, error) = PathResolver.ResolveTestCaseRef(suitePath, "..", caseRoot);
        Assert.Null(resolved);
        Assert.NotNull(error);
        Assert.Equal("OutOfRoot", ((dynamic)error!.Payload!).reason);
    }

    [Fact]
    public void SuiteRefResolution_FailsWhenMissingManifest()
    {
        using var temp = new TempFolder();
        var caseRoot = temp.CreateSubfolder("cases");
        var suitePath = Path.Combine(temp.Path, "suite.manifest.json");
        var folder = Path.Combine(caseRoot, "MissingManifest");
        Directory.CreateDirectory(folder);

        var (resolved, error) = PathResolver.ResolveTestCaseRef(suitePath, "MissingManifest", caseRoot);
        Assert.Null(resolved);
        Assert.NotNull(error);
        Assert.Equal("MissingManifest", ((dynamic)error!.Payload!).reason);
    }
}
