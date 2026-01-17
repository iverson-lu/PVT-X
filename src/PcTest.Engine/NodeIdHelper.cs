using System.Text.RegularExpressions;

namespace PcTest.Engine;

/// <summary>
/// Helper utilities for working with node identifiers.
/// </summary>
public static class NodeIdHelper
{
    /// <summary>
    /// Strips the instance suffix (_1, _2, etc.) from a nodeId to get the base identity.
    /// Examples:
    /// - "suite.test@1.0.0_1" -> "suite.test@1.0.0"
    /// - "case.test@1.0.0_2" -> "case.test@1.0.0"
    /// - "case.test@1.0.0" -> "case.test@1.0.0" (unchanged if no suffix)
    /// </summary>
    public static string StripInstanceSuffix(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return nodeId;

        var match = Regex.Match(nodeId, @"^(.+)_(\d+)$");
        return match.Success ? match.Groups[1].Value : nodeId;
    }
}
