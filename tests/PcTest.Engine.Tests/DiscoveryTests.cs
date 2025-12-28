using PcTest.Contracts;
using PcTest.Engine;
using Xunit;

namespace PcTest.Engine.Tests;

public sealed class DiscoveryTests
{
    [Fact]
    public void DuplicateIdentityFailsDiscovery()
    {
        var root = EngineTestUtilities.CreateTempDirectory();
        var casesRoot = Path.Combine(root, "TestCases");
        var suitesRoot = Path.Combine(root, "TestSuites");
        var plansRoot = Path.Combine(root, "TestPlans");
        Directory.CreateDirectory(casesRoot);
        Directory.CreateDirectory(suitesRoot);
        Directory.CreateDirectory(plansRoot);

        var caseA = Path.Combine(casesRoot, "A");
        var caseB = Path.Combine(casesRoot, "B");
        Directory.CreateDirectory(caseA);
        Directory.CreateDirectory(caseB);

        EngineTestUtilities.WriteJson(Path.Combine(caseA, "test.manifest.json"), EngineTestUtilities.SampleTestCase("Dup", "1.0.0"));
        EngineTestUtilities.WriteJson(Path.Combine(caseB, "test.manifest.json"), EngineTestUtilities.SampleTestCase("Dup", "1.0.0"));

        var discovery = new DiscoveryService();
        var exception = Assert.Throws<DiscoveryException>(() =>
            discovery.Discover(new DiscoveryRoots
            {
                ResolvedTestCaseRoot = casesRoot,
                ResolvedTestSuiteRoot = suitesRoot,
                ResolvedTestPlanRoot = plansRoot
            }));

        Assert.Equal("Identity.NotUnique", exception.Code);
    }

    [Fact]
    public void SuiteRefOutOfRootReportsError()
    {
        var root = EngineTestUtilities.CreateTempDirectory();
        var casesRoot = Path.Combine(root, "TestCases");
        var suitesRoot = Path.Combine(root, "TestSuites");
        var plansRoot = Path.Combine(root, "TestPlans");
        Directory.CreateDirectory(casesRoot);
        Directory.CreateDirectory(suitesRoot);
        Directory.CreateDirectory(plansRoot);

        var outside = Path.Combine(root, "Outside");
        Directory.CreateDirectory(outside);
        EngineTestUtilities.WriteJson(Path.Combine(outside, "test.manifest.json"), EngineTestUtilities.SampleTestCase("Outside", "1.0.0"));

        var suiteFolder = Path.Combine(suitesRoot, "Suite");
        Directory.CreateDirectory(suiteFolder);

        var suite = EngineTestUtilities.SampleSuite("Suite", "1.0.0", "..\\Outside");
        EngineTestUtilities.WriteJson(Path.Combine(suiteFolder, "suite.manifest.json"), suite);

        var discovery = new DiscoveryService();
        var exception = Assert.Throws<DiscoveryException>(() =>
            discovery.Discover(new DiscoveryRoots
            {
                ResolvedTestCaseRoot = casesRoot,
                ResolvedTestSuiteRoot = suitesRoot,
                ResolvedTestPlanRoot = plansRoot
            }));

        Assert.Equal("Suite.TestCaseRef.Invalid", exception.Code);
        Assert.Equal("OutOfRoot", exception.Payload["reason"]);
    }

    [Fact]
    public void SuiteRefSymlinkOutOfRootReportsError()
    {
        var root = EngineTestUtilities.CreateTempDirectory();
        var casesRoot = Path.Combine(root, "TestCases");
        var suitesRoot = Path.Combine(root, "TestSuites");
        var plansRoot = Path.Combine(root, "TestPlans");
        Directory.CreateDirectory(casesRoot);
        Directory.CreateDirectory(suitesRoot);
        Directory.CreateDirectory(plansRoot);

        var outside = Path.Combine(root, "Outside");
        Directory.CreateDirectory(outside);
        EngineTestUtilities.WriteJson(Path.Combine(outside, "test.manifest.json"), EngineTestUtilities.SampleTestCase("Outside", "1.0.0"));

        var linkPath = Path.Combine(casesRoot, "Link");
        try
        {
            Directory.CreateSymbolicLink(linkPath, outside);
        }
        catch (Exception)
        {
            return;
        }

        var suiteFolder = Path.Combine(suitesRoot, "Suite");
        Directory.CreateDirectory(suiteFolder);
        var suite = EngineTestUtilities.SampleSuite("Suite", "1.0.0", "Link");
        EngineTestUtilities.WriteJson(Path.Combine(suiteFolder, "suite.manifest.json"), suite);

        var discovery = new DiscoveryService();
        var exception = Assert.Throws<DiscoveryException>(() =>
            discovery.Discover(new DiscoveryRoots
            {
                ResolvedTestCaseRoot = casesRoot,
                ResolvedTestSuiteRoot = suitesRoot,
                ResolvedTestPlanRoot = plansRoot
            }));

        Assert.Equal("Suite.TestCaseRef.Invalid", exception.Code);
        Assert.Equal("OutOfRoot", exception.Payload["reason"]);
    }
}
