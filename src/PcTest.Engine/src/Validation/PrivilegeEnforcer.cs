using System.Runtime.InteropServices;
using System.Security.Principal;
using PcTest.Contracts.Manifest;

namespace PcTest.Engine.Validation;

public static class PrivilegeEnforcer
{
    public static void EnsureAllowed(PrivilegePolicy policy)
    {
        var elevated = IsElevated();
        if (policy == PrivilegePolicy.AdminRequired && !elevated)
        {
            throw new InvalidOperationException("This test requires elevation. Please run as administrator.");
        }
    }

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
