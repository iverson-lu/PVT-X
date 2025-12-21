using System.Diagnostics;
using PcTest.Contracts.Result;

namespace PcTest.Runner.Process;

public class PowerShellLocator
{
    private static readonly string[] CandidatePaths = new[]
    {
        "pwsh.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PowerShell", "7", "pwsh.exe"),
    };

    public PowerShellInfo Locate()
    {
        foreach (var candidate in CandidatePaths)
        {
            var info = TryProbe(candidate);
            if (info != null)
            {
                return info;
            }
        }

        throw new InvalidOperationException("PowerShell 7+ (pwsh.exe) was not found in PATH or standard locations.");
    }

    private PowerShellInfo? TryProbe(string candidate)
    {
        var executable = ResolveExecutable(candidate);
        if (executable == null)
        {
            return null;
        }

        try
        {
            var version = ReadVersion(executable);
            if (version == null || version.Major < 7)
            {
                return null;
            }

            return new PowerShellInfo(executable, version.ToString());
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveExecutable(string candidate)
    {
        if (Path.IsPathRooted(candidate))
        {
            return File.Exists(candidate) ? candidate : null;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var part in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var combined = Path.Combine(part, candidate);
            if (File.Exists(combined))
            {
                return combined;
            }
        }

        return null;
    }

    private static Version? ReadVersion(string executable)
    {
        var info = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = "--version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(info);
        if (process == null)
        {
            return null;
        }

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit(5000);
        var versionString = output.Replace("PowerShell", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return Version.TryParse(versionString, out var version) ? version : null;
    }
}

public record PowerShellInfo(string Path, string Version);
