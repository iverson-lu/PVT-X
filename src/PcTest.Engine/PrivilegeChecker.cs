using System.Runtime.InteropServices;
using System.Security.Principal;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Engine.Discovery;

namespace PcTest.Engine;

/// <summary>
/// Utility for checking privilege requirements before execution.
/// Per spec section 9.2: Suite privilege = max(child privileges), AdminRequired > AdminPreferred > User.
/// </summary>
public static class PrivilegeChecker
{
    /// <summary>
    /// Checks if the current process is running with elevated privileges.
    /// </summary>
    public static bool IsProcessElevated()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return true; // Non-Windows platforms don't have the same elevation concept

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Computes the effective privilege level for a test case.
    /// </summary>
    public static Privilege GetTestCasePrivilege(TestCaseManifest manifest)
    {
        return manifest.Privilege;
    }

    /// <summary>
    /// Computes the effective privilege level for a test suite.
    /// Per spec: Suite privilege = max(child privileges).
    /// </summary>
    public static Privilege GetSuitePrivilege(TestSuiteManifest suite, DiscoveryResult discovery)
    {
        if (suite.TestCases == null || suite.TestCases.Count == 0)
            return Privilege.User;

        var maxPrivilege = Privilege.User;

        foreach (var node in suite.TestCases)
        {
            // Try to resolve the test case from discovery
            // First try by NodeId (the authoritative identity), stripping any instance suffix
            // If not found, fallback to Ref for compatibility
            DiscoveredTestCase? testCase = null;
            
            if (!string.IsNullOrWhiteSpace(node.NodeId))
            {
                var identity = NodeIdHelper.StripInstanceSuffix(node.NodeId);
                testCase = discovery.TestCases.Values
                    .FirstOrDefault(tc => tc.Identity.Equals(identity, StringComparison.OrdinalIgnoreCase));
            }
            
            if (testCase == null && !string.IsNullOrWhiteSpace(node.Ref))
            {
                testCase = discovery.TestCases.Values
                    .FirstOrDefault(tc => tc.Identity.Equals(node.Ref, StringComparison.OrdinalIgnoreCase));
            }

            if (testCase != null)
            {
                var casePrivilege = testCase.Manifest.Privilege;
                if (casePrivilege > maxPrivilege)
                    maxPrivilege = casePrivilege;
            }
        }

        return maxPrivilege;
    }

    /// <summary>
    /// Computes the effective privilege level for a test plan.
    /// Per spec: Plan privilege = max(suite privileges).
    /// </summary>
    public static Privilege GetPlanPrivilege(TestPlanManifest plan, DiscoveryResult discovery)
    {
        if (plan.TestSuites == null || plan.TestSuites.Count == 0)
            return Privilege.User;

        var maxPrivilege = Privilege.User;

        foreach (var suiteNode in plan.TestSuites)
        {
            // Strip _1, _2, etc. suffix from nodeId to get the actual suite identity
            var suiteIdentity = NodeIdHelper.StripInstanceSuffix(suiteNode.NodeId);
            
            // Try to resolve the suite from discovery
            var suite = discovery.TestSuites.Values
                .FirstOrDefault(s => s.Identity.Equals(suiteIdentity, StringComparison.OrdinalIgnoreCase));

            if (suite != null)
            {
                var suitePrivilege = GetSuitePrivilege(suite.Manifest, discovery);
                if (suitePrivilege > maxPrivilege)
                    maxPrivilege = suitePrivilege;
            }
        }

        return maxPrivilege;
    }

    /// <summary>
    /// Validates privilege requirements for a run target.
    /// Returns (isValid, requiredPrivilege, message).
    /// </summary>
    public static (bool IsValid, Privilege RequiredPrivilege, string? Message) ValidatePrivilege(
        RunType runType,
        string targetIdentity,
        DiscoveryResult discovery)
    {
        var isElevated = IsProcessElevated();
        Privilege requiredPrivilege;
        string targetName;

        switch (runType)
        {
            case RunType.TestCase:
                var testCase = discovery.TestCases.Values
                    .FirstOrDefault(tc => tc.Identity.Equals(targetIdentity, StringComparison.OrdinalIgnoreCase));
                if (testCase == null)
                    return (false, Privilege.User, $"Test case '{targetIdentity}' not found");
                
                requiredPrivilege = testCase.Manifest.Privilege;
                targetName = testCase.Manifest.Name;
                break;

            case RunType.TestSuite:
                var suite = discovery.TestSuites.Values
                    .FirstOrDefault(s => s.Identity.Equals(targetIdentity, StringComparison.OrdinalIgnoreCase));
                if (suite == null)
                    return (false, Privilege.User, $"Test suite '{targetIdentity}' not found");
                
                requiredPrivilege = GetSuitePrivilege(suite.Manifest, discovery);
                targetName = suite.Manifest.Name;
                break;

            case RunType.TestPlan:
                var plan = discovery.TestPlans.Values
                    .FirstOrDefault(p => p.Identity.Equals(targetIdentity, StringComparison.OrdinalIgnoreCase));
                if (plan == null)
                    return (false, Privilege.User, $"Test plan '{targetIdentity}' not found");
                
                requiredPrivilege = GetPlanPrivilege(plan.Manifest, discovery);
                targetName = plan.Manifest.Name;
                break;

            default:
                return (false, Privilege.User, "Unknown run type");
        }

        // Check privilege requirements
        if (requiredPrivilege == Privilege.AdminRequired && !isElevated)
        {
            return (false, requiredPrivilege, 
                $"'{targetName}' requires administrator privileges but the current process is not elevated.\n\n" +
                $"Please restart the application as administrator to run this {runType.ToString().ToLowerInvariant()}.");
        }

        if (requiredPrivilege == Privilege.AdminPreferred && !isElevated)
        {
            return (false, requiredPrivilege,
                $"'{targetName}' prefers administrator privileges but the current process is not elevated.\n\n" +
                $"Some tests may fail or produce incomplete results. Do you want to continue anyway?");
        }

        return (true, requiredPrivilege, null);
    }
}
