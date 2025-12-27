using PcTest.Contracts;
using PcTest.Engine;
using Xunit;

namespace PcTest.Engine.Tests;

public class DiscoveryTests
{
    [Fact]
    public void Discovery_FailsOnDuplicateIdentities()
    {
        using var temp = new TempFolder();
        var caseRoot = temp.CreateSubfolder("cases");
        var suiteRoot = temp.CreateSubfolder("suites");
        var planRoot = temp.CreateSubfolder("plans");

        var caseFolder1 = Path.Combine(caseRoot, "CaseA");
        var caseFolder2 = Path.Combine(caseRoot, "CaseB");
        Directory.CreateDirectory(caseFolder1);
        Directory.CreateDirectory(caseFolder2);

        var manifest = new TestCaseManifest { Id = "CpuStress", Version = "1.0.0", Name = "CPU", Category = "Thermal" };
        JsonUtilities.WriteJson(Path.Combine(caseFolder1, "test.manifest.json"), manifest);
        JsonUtilities.WriteJson(Path.Combine(caseFolder2, "test.manifest.json"), manifest);

        var discovery = new DiscoveryService().Discover(new DiscoveryRoots
        {
            TestCaseRoot = caseRoot,
            TestSuiteRoot = suiteRoot,
            TestPlanRoot = planRoot
        });

        Assert.True(discovery.HasErrors);
        var error = Assert.Single(discovery.Errors.Where(e => e.Code == ErrorCodes.IdentityNonUnique));
        Assert.Equal("TestCase", ((dynamic)error.Payload!).entityType);
    }
}
