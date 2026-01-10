using System.IO;
using System.Linq;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Engine.Discovery;

namespace PcTest.Ui.ViewModels;

internal static class PrivilegeIndicatorHelper
{
    public static Privilege GetSuitePrivilege(TestSuiteManifest suite, DiscoveryResult discovery)
    {
        var privilege = Privilege.User;
        foreach (var node in suite.TestCases)
        {
            var nodePrivilege = GetNodePrivilege(node, discovery);
            if (nodePrivilege == Privilege.AdminRequired)
            {
                return Privilege.AdminRequired;
            }

            if (nodePrivilege == Privilege.AdminPreferred)
            {
                privilege = Privilege.AdminPreferred;
            }
        }

        return privilege;
    }

    private static Privilege GetNodePrivilege(TestCaseNode node, DiscoveryResult discovery)
    {
        var testCase = FindTestCase(node, discovery);
        return testCase?.Manifest.Privilege ?? Privilege.User;
    }

    private static DiscoveredTestCase? FindTestCase(TestCaseNode node, DiscoveryResult discovery)
    {
        if (!string.IsNullOrWhiteSpace(node.NodeId))
        {
            var identity = StripNodeIdSuffix(node.NodeId);
            var match = discovery.TestCases.Values.FirstOrDefault(tc =>
                tc.Identity.Equals(identity, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        if (!string.IsNullOrWhiteSpace(node.Ref))
        {
            var match = discovery.TestCases.Values.FirstOrDefault(tc =>
                string.Equals(Path.GetFileName(tc.FolderPath), node.Ref, StringComparison.OrdinalIgnoreCase) ||
                tc.Manifest.Id.Equals(node.Ref, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string StripNodeIdSuffix(string nodeId)
    {
        var match = System.Text.RegularExpressions.Regex.Match(nodeId, @"^(.+)_(\d+)$");
        return match.Success ? match.Groups[1].Value : nodeId;
    }
}
