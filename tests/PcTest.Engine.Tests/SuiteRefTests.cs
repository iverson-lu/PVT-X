using PcTest.Contracts;
using PcTest.Engine;
using PcTest.Runner;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class SuiteRefTests
{
    [Fact]
    public void SuiteRef_OutOfRoot_ThrowsExpectedError()
    {
        var root = TestHelpers.CreateTempDirectory();
        var caseRoot = Path.Combine(root, "cases");
        var suiteRoot = Path.Combine(root, "suites");
        var planRoot = Path.Combine(root, "plans");
        var runsRoot = Path.Combine(root, "runs");
        Directory.CreateDirectory(caseRoot);
        Directory.CreateDirectory(suiteRoot);
        Directory.CreateDirectory(planRoot);
        Directory.CreateDirectory(runsRoot);

        var manifest = new
        {
            schemaVersion = "1.5.0",
            id = "CpuStress",
            name = "CPU",
            category = "Thermal",
            version = "1.0.0"
        };
        TestHelpers.WriteJson(Path.Combine(caseRoot, "CpuStress", "test.manifest.json"), manifest);
        File.WriteAllText(Path.Combine(caseRoot, "CpuStress", "run.ps1"), "exit 0");

        var suiteManifest = new
        {
            schemaVersion = "1.5.0",
            id = "BadSuite",
            name = "BadSuite",
            version = "1.0.0",
            testCases = new[]
            {
                new { nodeId = "n1", @ref = ".." }
            }
        };
        TestHelpers.WriteJson(Path.Combine(suiteRoot, "suite.manifest.json"), suiteManifest);

        var engine = new PcTestEngine(new RunnerService());
        var ex = Assert.Throws<ValidationException>(() => engine.RunSuite(
            new EngineOptions(caseRoot, suiteRoot, planRoot, runsRoot),
            new RunRequest { Suite = "BadSuite@1.0.0" }));

        Assert.Equal("Suite.TestCaseRef.Invalid", ex.Code);
        Assert.Equal("OutOfRoot", ex.Payload["reason"]);
    }
}
