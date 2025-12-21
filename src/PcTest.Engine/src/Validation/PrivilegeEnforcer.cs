using System.Runtime.InteropServices;
using System.Security.Principal;
using PcTest.Contracts.Manifest;

namespace PcTest.Engine.Validation;

/// <summary>
/// Ensures tests are executed with the required privilege level.
/// </summary>
public static class PrivilegeEnforcer
{
    /// <summary>
    /// Validates that the current process satisfies the requested privilege policy.
    /// </summary>
    /// <param name="policy">Privilege policy required by the test.</param>
    /// <exception cref="InvalidOperationException">Thrown when elevation is required but not present.</exception>
    public static void EnsureAllowed(PrivilegePolicy policy)
    {
        var elevated = IsElevated();
        if (policy == PrivilegePolicy.AdminRequired && !elevated)
        {
            throw new InvalidOperationException("This test requires elevation. Please run as administrator.");
        }
    }

    /// <summary>
    /// Determines whether the current process is running with administrative privileges.
    /// </summary>
    /// <returns>True when elevated, otherwise false.</returns>
    public static bool IsElevated()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
