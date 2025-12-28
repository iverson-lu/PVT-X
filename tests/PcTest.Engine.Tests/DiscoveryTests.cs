using PcTest.Contracts;
using PcTest.Engine;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class DiscoveryTests
{
    [Fact]
    public void Discovery_DuplicateTestCaseIdentity_FailsWithConflictPaths()
    {
        var root = TestHelpers.CreateTempDirectory();
        var caseRoot = Path.Combine(root, "cases");
        var suiteRoot = Path.Combine(root, "suites");
        var planRoot = Path.Combine(root, "plans");
        Directory.CreateDirectory(caseRoot);
        Directory.CreateDirectory(suiteRoot);
        Directory.CreateDirectory(planRoot);

        var manifest = new
        {
            schemaVersion = "1.5.0",
            id = "CpuStress",
            name = "CPU",
            category = "Thermal",
            version = "1.0.0"
        };
        TestHelpers.WriteJson(Path.Combine(caseRoot, "A", "test.manifest.json"), manifest);
        TestHelpers.WriteJson(Path.Combine(caseRoot, "B", "test.manifest.json"), manifest);

        var service = new DiscoveryService();
        var ex = Assert.Throws<ValidationException>(() => service.Discover(new DiscoveryRequest(caseRoot, suiteRoot, planRoot)));
        Assert.Contains(ex.Errors, error =>
            error.Code == "Identity.Duplicate" &&
            error.Payload["entityType"].Equals("TestCase") &&
            ((string[])error.Payload["conflictPaths"]).Length == 2);
    }
}
