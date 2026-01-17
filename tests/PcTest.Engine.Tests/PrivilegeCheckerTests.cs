using System.Text;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Engine;
using PcTest.Engine.Discovery;
using Xunit;

namespace PcTest.Engine.Tests;

/// <summary>
/// Tests for PrivilegeChecker utility per spec section 9.2.
/// </summary>
public class PrivilegeCheckerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _casesRoot;
    private readonly string _suitesRoot;
    private readonly string _plansRoot;

    public PrivilegeCheckerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"PcTest_{Guid.NewGuid():N}");
        _casesRoot = Path.Combine(_tempRoot, "TestCases");
        _suitesRoot = Path.Combine(_tempRoot, "TestSuites");
        _plansRoot = Path.Combine(_tempRoot, "TestPlans");

        Directory.CreateDirectory(_casesRoot);
        Directory.CreateDirectory(_suitesRoot);
        Directory.CreateDirectory(_plansRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public void IsProcessElevated_ReturnsBoolean()
    {
        // Test that the method returns a valid boolean value
        var result = PrivilegeChecker.IsProcessElevated();
        Assert.True(result is true || result is false);
    }

    [Fact]
    public void GetSuitePrivilege_NoTestCases_ReturnsUser()
    {
        // Create a suite with no test cases
        var suiteId = "S-EmptySuite";
        var suiteFolderPath = Path.Combine(_suitesRoot, suiteId);
        Directory.CreateDirectory(suiteFolderPath);
        var suitePath = Path.Combine(suiteFolderPath, "suite.manifest.json");

        var suite = new TestSuiteManifest
        {
            Id = suiteId,
            Name = "Empty Suite",
            Version = "1.0.0",
            TestCases = new List<TestCaseNode>()
        };

        File.WriteAllText(suitePath, JsonDefaults.Serialize(suite), Encoding.UTF8);

        var discoveryService = new DiscoveryService();
        var discovery = discoveryService.Discover(_casesRoot, _suitesRoot, _plansRoot);

        var suiteManifest = discovery.TestSuites.Values.First(s => s.Manifest.Id == suiteId).Manifest;
        var privilege = PrivilegeChecker.GetSuitePrivilege(suiteManifest, discovery);

        Assert.Equal(Privilege.User, privilege);
    }

    [Fact]
    public void GetSuitePrivilege_AllUserCases_ReturnsUser()
    {
        // Create test cases with User privilege
        CreateTestCase("TC-User1", Privilege.User);
        CreateTestCase("TC-User2", Privilege.User);

        // Create suite referencing them
        var suiteId = "S-AllUser";
        var suiteFolderPath = Path.Combine(_suitesRoot, suiteId);
        Directory.CreateDirectory(suiteFolderPath);
        var suitePath = Path.Combine(suiteFolderPath, "suite.manifest.json");

        var suite = new TestSuiteManifest
        {
            Id = suiteId,
            Name = "All User Suite",
            Version = "1.0.0",
            TestCases = new List<TestCaseNode>
            {
                new() { Ref = "TC-User1@1.0.0" },
                new() { Ref = "TC-User2@1.0.0" }
            }
        };

        File.WriteAllText(suitePath, JsonDefaults.Serialize(suite), Encoding.UTF8);

        var discoveryService = new DiscoveryService();
        var discovery = discoveryService.Discover(_casesRoot, _suitesRoot, _plansRoot);

        var suiteManifest = discovery.TestSuites.Values.First(s => s.Manifest.Id == suiteId).Manifest;
        var privilege = PrivilegeChecker.GetSuitePrivilege(suiteManifest, discovery);

        Assert.Equal(Privilege.User, privilege);
    }

    [Fact]
    public void GetSuitePrivilege_MixedWithAdminPreferred_ReturnsAdminPreferred()
    {
        // Create test cases with mixed privileges
        CreateTestCase("TC-User", Privilege.User);
        CreateTestCase("TC-AdminPref", Privilege.AdminPreferred);

        // Create suite referencing them
        var suiteId = "S-Mixed1";
        var suiteFolderPath = Path.Combine(_suitesRoot, suiteId);
        Directory.CreateDirectory(suiteFolderPath);
        var suitePath = Path.Combine(suiteFolderPath, "suite.manifest.json");

        var suite = new TestSuiteManifest
        {
            Id = suiteId,
            Name = "Mixed Suite",
            Version = "1.0.0",
            TestCases = new List<TestCaseNode>
            {
                new() { Ref = "TC-User@1.0.0" },
                new() { Ref = "TC-AdminPref@1.0.0" }
            }
        };

        File.WriteAllText(suitePath, JsonDefaults.Serialize(suite), Encoding.UTF8);

        var discoveryService = new DiscoveryService();
        var discovery = discoveryService.Discover(_casesRoot, _suitesRoot, _plansRoot);

        var suiteManifest = discovery.TestSuites.Values.First(s => s.Manifest.Id == suiteId).Manifest;
        var privilege = PrivilegeChecker.GetSuitePrivilege(suiteManifest, discovery);

        Assert.Equal(Privilege.AdminPreferred, privilege);
    }

    [Fact]
    public void GetSuitePrivilege_MixedWithAdminRequired_ReturnsAdminRequired()
    {
        // Create test cases with mixed privileges including AdminRequired
        CreateTestCase("TC-User", Privilege.User);
        CreateTestCase("TC-AdminPref", Privilege.AdminPreferred);
        CreateTestCase("TC-AdminReq", Privilege.AdminRequired);

        // Create suite referencing them
        var suiteId = "S-Mixed2";
        var suiteFolderPath = Path.Combine(_suitesRoot, suiteId);
        Directory.CreateDirectory(suiteFolderPath);
        var suitePath = Path.Combine(suiteFolderPath, "suite.manifest.json");

        var suite = new TestSuiteManifest
        {
            Id = suiteId,
            Name = "Mixed Suite with Required",
            Version = "1.0.0",
            TestCases = new List<TestCaseNode>
            {
                new() { Ref = "TC-User@1.0.0" },
                new() { Ref = "TC-AdminPref@1.0.0" },
                new() { Ref = "TC-AdminReq@1.0.0" }
            }
        };

        File.WriteAllText(suitePath, JsonDefaults.Serialize(suite), Encoding.UTF8);

        var discoveryService = new DiscoveryService();
        var discovery = discoveryService.Discover(_casesRoot, _suitesRoot, _plansRoot);

        var suiteManifest = discovery.TestSuites.Values.First(s => s.Manifest.Id == suiteId).Manifest;
        var privilege = PrivilegeChecker.GetSuitePrivilege(suiteManifest, discovery);

        Assert.Equal(Privilege.AdminRequired, privilege);
    }

    [Fact]
    public void GetPlanPrivilege_NoSuites_ReturnsUser()
    {
        // Create a plan with no suites
        var planId = "P-Empty";
        var planFolderPath = Path.Combine(_plansRoot, planId);
        Directory.CreateDirectory(planFolderPath);
        var planPath = Path.Combine(planFolderPath, "plan.manifest.json");

        var plan = new TestPlanManifest
        {
            Id = planId,
            Name = "Empty Plan",
            Version = "1.0.0",
            TestSuites = new List<TestSuiteNode>()
        };

        File.WriteAllText(planPath, JsonDefaults.Serialize(plan), Encoding.UTF8);

        var discoveryService = new DiscoveryService();
        var discovery = discoveryService.Discover(_casesRoot, _suitesRoot, _plansRoot);

        var planManifest = discovery.TestPlans.Values.First(p => p.Manifest.Id == planId).Manifest;
        var privilege = PrivilegeChecker.GetPlanPrivilege(planManifest, discovery);

        Assert.Equal(Privilege.User, privilege);
    }

    [Fact]
    public void GetPlanPrivilege_AllUserSuites_ReturnsUser()
    {
        // Create test cases
        CreateTestCase("TC-User1", Privilege.User);
        CreateTestCase("TC-User2", Privilege.User);

        // Create suites
        CreateSuite("S-User1", new[] { "TC-User1" });
        CreateSuite("S-User2", new[] { "TC-User2" });

        // Create plan
        var planId = "P-AllUser";
        var planFolderPath = Path.Combine(_plansRoot, planId);
        Directory.CreateDirectory(planFolderPath);
        var planPath = Path.Combine(planFolderPath, "plan.manifest.json");

        var plan = new TestPlanManifest
        {
            Id = planId,
            Name = "All User Plan",
            Version = "1.0.0",
            TestSuites = new List<TestSuiteNode> 
            { 
                new TestSuiteNode { NodeId = "S-User1@1.0.0", Ref = "S-User1" }, 
                new TestSuiteNode { NodeId = "S-User2@1.0.0", Ref = "S-User2" } 
            }
        };

        File.WriteAllText(planPath, JsonDefaults.Serialize(plan), Encoding.UTF8);

        var discoveryService = new DiscoveryService();
        var discovery = discoveryService.Discover(_casesRoot, _suitesRoot, _plansRoot);

        var planManifest = discovery.TestPlans.Values.First(p => p.Manifest.Id == planId).Manifest;
        var privilege = PrivilegeChecker.GetPlanPrivilege(planManifest, discovery);

        Assert.Equal(Privilege.User, privilege);
    }

    [Fact]
    public void GetPlanPrivilege_MixedSuites_ReturnsMaxPrivilege()
    {
        // Create test cases with different privileges
        CreateTestCase("TC-User", Privilege.User);
        CreateTestCase("TC-AdminPref", Privilege.AdminPreferred);
        CreateTestCase("TC-AdminReq", Privilege.AdminRequired);

        // Create suites with different privilege levels
        CreateSuite("S-User", new[] { "TC-User" });
        CreateSuite("S-AdminPref", new[] { "TC-AdminPref" });
        CreateSuite("S-AdminReq", new[] { "TC-AdminReq" });

        // Create plan with mixed suites
        var planId = "P-Mixed";
        var planFolderPath = Path.Combine(_plansRoot, planId);
        Directory.CreateDirectory(planFolderPath);
        var planPath = Path.Combine(planFolderPath, "plan.manifest.json");

        var plan = new TestPlanManifest
        {
            Id = planId,
            Name = "Mixed Plan",
            Version = "1.0.0",
            TestSuites = new List<TestSuiteNode> 
            { 
                new TestSuiteNode { NodeId = "S-User@1.0.0", Ref = "S-User" }, 
                new TestSuiteNode { NodeId = "S-AdminPref@1.0.0", Ref = "S-AdminPref" }, 
                new TestSuiteNode { NodeId = "S-AdminReq@1.0.0", Ref = "S-AdminReq" } 
            }
        };

        File.WriteAllText(planPath, JsonDefaults.Serialize(plan), Encoding.UTF8);

        var discoveryService = new DiscoveryService();
        var discovery = discoveryService.Discover(_casesRoot, _suitesRoot, _plansRoot);

        var planManifest = discovery.TestPlans.Values.First(p => p.Manifest.Id == planId).Manifest;
        var privilege = PrivilegeChecker.GetPlanPrivilege(planManifest, discovery);

        Assert.Equal(Privilege.AdminRequired, privilege);
    }

    [Fact]
    public void ValidatePrivilege_TestCase_User_AlwaysValid()
    {
        CreateTestCase("TC-User", Privilege.User);

        var discoveryService = new DiscoveryService();
        var discovery = discoveryService.Discover(_casesRoot, _suitesRoot, _plansRoot);

        var (isValid, requiredPrivilege, message) = PrivilegeChecker.ValidatePrivilege(
            RunType.TestCase, "TC-User@1.0.0", discovery);

        Assert.True(isValid);
        Assert.Equal(Privilege.User, requiredPrivilege);
        Assert.Null(message);
    }

    [Fact]
    public void ValidatePrivilege_TestCase_AdminPreferred_WhenNotElevated_ReturnsWarning()
    {
        CreateTestCase("TC-AdminPref", Privilege.AdminPreferred);

        var discoveryService = new DiscoveryService();
        var discovery = discoveryService.Discover(_casesRoot, _suitesRoot, _plansRoot);

        var (isValid, requiredPrivilege, message) = PrivilegeChecker.ValidatePrivilege(
            RunType.TestCase, "TC-AdminPref@1.0.0", discovery);

        var isElevated = PrivilegeChecker.IsProcessElevated();

        if (isElevated)
        {
            Assert.True(isValid);
            Assert.Equal(Privilege.AdminPreferred, requiredPrivilege);
            Assert.Null(message);
        }
        else
        {
            Assert.False(isValid);
            Assert.Equal(Privilege.AdminPreferred, requiredPrivilege);
            Assert.NotNull(message);
            Assert.Contains("administrator", message.ToLowerInvariant());
        }
    }

    [Fact]
    public void ValidatePrivilege_TestCase_AdminRequired_WhenNotElevated_ReturnsError()
    {
        CreateTestCase("TC-AdminReq", Privilege.AdminRequired);

        var discoveryService = new DiscoveryService();
        var discovery = discoveryService.Discover(_casesRoot, _suitesRoot, _plansRoot);

        var (isValid, requiredPrivilege, message) = PrivilegeChecker.ValidatePrivilege(
            RunType.TestCase, "TC-AdminReq@1.0.0", discovery);

        var isElevated = PrivilegeChecker.IsProcessElevated();

        if (isElevated)
        {
            Assert.True(isValid);
            Assert.Equal(Privilege.AdminRequired, requiredPrivilege);
            Assert.Null(message);
        }
        else
        {
            Assert.False(isValid);
            Assert.Equal(Privilege.AdminRequired, requiredPrivilege);
            Assert.NotNull(message);
            Assert.Contains("administrator", message.ToLowerInvariant());
            Assert.Contains("requires", message.ToLowerInvariant());
        }
    }

    [Fact]
    public void ValidatePrivilege_Suite_ChecksMaxPrivilege()
    {
        // Create test cases with mixed privileges
        CreateTestCase("TC-User", Privilege.User);
        CreateTestCase("TC-AdminReq", Privilege.AdminRequired);

        CreateSuite("S-Mixed", new[] { "TC-User", "TC-AdminReq" });

        var discoveryService = new DiscoveryService();
        var discovery = discoveryService.Discover(_casesRoot, _suitesRoot, _plansRoot);

        var (isValid, requiredPrivilege, message) = PrivilegeChecker.ValidatePrivilege(
            RunType.TestSuite, "S-Mixed@1.0.0", discovery);

        var isElevated = PrivilegeChecker.IsProcessElevated();

        Assert.Equal(Privilege.AdminRequired, requiredPrivilege);

        if (!isElevated)
        {
            Assert.False(isValid);
            Assert.NotNull(message);
        }
    }

    [Fact]
    public void ValidatePrivilege_Plan_ChecksMaxPrivilege()
    {
        // Create test cases
        CreateTestCase("TC-User", Privilege.User);
        CreateTestCase("TC-AdminPref", Privilege.AdminPreferred);

        // Create suites
        CreateSuite("S-User", new[] { "TC-User" });
        CreateSuite("S-AdminPref", new[] { "TC-AdminPref" });

        // Create plan
        var planId = "P-Mixed";
        var planFolderPath = Path.Combine(_plansRoot, planId);
        Directory.CreateDirectory(planFolderPath);
        var planPath = Path.Combine(planFolderPath, "plan.manifest.json");

        var plan = new TestPlanManifest
        {
            Id = planId,
            Name = "Mixed Plan",
            Version = "1.0.0",
            TestSuites = new List<TestSuiteNode> 
            { 
                new TestSuiteNode { NodeId = "S-User@1.0.0", Ref = "S-User" }, 
                new TestSuiteNode { NodeId = "S-AdminPref@1.0.0", Ref = "S-AdminPref" } 
            }
        };

        File.WriteAllText(planPath, JsonDefaults.Serialize(plan), Encoding.UTF8);

        var discoveryService = new DiscoveryService();
        var discovery = discoveryService.Discover(_casesRoot, _suitesRoot, _plansRoot);

        var (isValid, requiredPrivilege, message) = PrivilegeChecker.ValidatePrivilege(
            RunType.TestPlan, $"{planId}@1.0.0", discovery);

        var isElevated = PrivilegeChecker.IsProcessElevated();

        Assert.Equal(Privilege.AdminPreferred, requiredPrivilege);

        if (!isElevated)
        {
            Assert.False(isValid);
            Assert.NotNull(message);
        }
    }

    // Helper methods
    private void CreateTestCase(string id, Privilege privilege)
    {
        var casePath = Path.Combine(_casesRoot, id);
        Directory.CreateDirectory(casePath);

        var manifest = new TestCaseManifest
        {
            Id = id,
            Name = $"Test {id}",
            Version = "1.0.0",
            Privilege = privilege,
            Parameters = new List<ParameterDefinition>()
        };

        var manifestPath = Path.Combine(casePath, "test.manifest.json");
        File.WriteAllText(manifestPath, JsonDefaults.Serialize(manifest), Encoding.UTF8);

        // Create a minimal test script
        var scriptPath = Path.Combine(casePath, "test.ps1");
        File.WriteAllText(scriptPath, "# Minimal test script\nWrite-Output 'Test executed'\n", Encoding.UTF8);
    }

    private void CreateSuite(string id, string[] testCaseIds)
    {
        var suiteFolderPath = Path.Combine(_suitesRoot, id);
        Directory.CreateDirectory(suiteFolderPath);
        var suitePath = Path.Combine(suiteFolderPath, "suite.manifest.json");

        var suite = new TestSuiteManifest
        {
            Id = id,
            Name = $"Suite {id}",
            Version = "1.0.0",
            TestCases = testCaseIds.Select(tcId => new TestCaseNode { Ref = $"{tcId}@1.0.0" }).ToList()
        };

        File.WriteAllText(suitePath, JsonDefaults.Serialize(suite), Encoding.UTF8);
    }
}
