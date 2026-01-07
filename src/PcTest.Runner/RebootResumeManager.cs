using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using PcTest.Contracts;

namespace PcTest.Runner;

internal sealed class RebootRequest
{
    public string Type { get; init; } = string.Empty;
    public int NextPhase { get; init; }
    public string Reason { get; init; } = string.Empty;
    public RebootOptions? Reboot { get; init; }
}

internal sealed class RebootOptions
{
    public int? DelaySec { get; init; }
}

public static class RebootResumeManager
{
    private const string RebootRequestType = "control.reboot_required";

    public static string GetControlDirectory(string caseRunFolder)
        => Path.Combine(caseRunFolder, "artifacts", "control");

    public static string GetSessionPath(string caseRunFolder)
        => Path.Combine(caseRunFolder, "artifacts", "session.json");

    public static string GetRebootRequestPath(string controlDir)
        => Path.Combine(controlDir, "reboot.json");

    public static string GetResumeTaskName(string runId)
        => $"PVTX-Resume-{runId}";

    public static void EnsureControlDirectory(string caseRunFolder)
    {
        Directory.CreateDirectory(GetControlDirectory(caseRunFolder));
    }

    public static async Task<(RebootRequest? Request, string? Error)> TryLoadRebootRequestAsync(string controlDir)
    {
        var requestPath = GetRebootRequestPath(controlDir);
        if (!File.Exists(requestPath))
        {
            return (null, null);
        }

        try
        {
            var json = await File.ReadAllTextAsync(requestPath, Encoding.UTF8);
            var request = JsonDefaults.Deserialize<RebootRequest>(json);
            if (request is null)
            {
                return (null, "Invalid reboot request: JSON could not be parsed.");
            }

            if (!string.Equals(request.Type, RebootRequestType, StringComparison.Ordinal))
            {
                return (null, $"Invalid reboot request: type must be '{RebootRequestType}'.");
            }

            if (request.NextPhase < 1)
            {
                return (null, "Invalid reboot request: nextPhase must be >= 1.");
            }

            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return (null, "Invalid reboot request: reason is required.");
            }

            if (request.Reboot?.DelaySec is < 0)
            {
                return (null, "Invalid reboot request: reboot.delaySec must be >= 0.");
            }

            return (request, null);
        }
        catch (Exception ex)
        {
            return (null, $"Invalid reboot request: {ex.Message}");
        }
    }

    public static string CreateResumeToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

    public static async Task WriteSessionAsync(string sessionPath, ResumeSession session)
    {
        var json = JsonDefaults.Serialize(session);
        await File.WriteAllTextAsync(sessionPath, json, Encoding.UTF8);
    }

    public static async Task<ResumeSession?> LoadSessionAsync(string sessionPath)
    {
        if (!File.Exists(sessionPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(sessionPath, Encoding.UTF8);
        return JsonDefaults.Deserialize<ResumeSession>(json);
    }

    public static void CreateResumeTask(string runId, string resumeToken, string runnerPath, string runsRoot)
    {
        EnsureWindows("Task Scheduler resume tasks are only supported on Windows.");

        var taskName = GetResumeTaskName(runId);
        var arguments = $"--resume --runId {runId} --token {resumeToken} --runsRoot \"{runsRoot}\"";
        var taskCommand = $"\"{runnerPath}\" {arguments}";

        var args =
            $"/Create /TN \"{taskName}\" /SC ONSTART /RL HIGHEST /RU SYSTEM /TR \"{taskCommand}\" /F";

        ExecuteProcess("schtasks.exe", args, "Failed to create resume task.");
    }

    public static void DeleteResumeTask(string runId)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            var taskName = GetResumeTaskName(runId);
            var args = $"/Delete /TN \"{taskName}\" /F";
            ExecuteProcess("schtasks.exe", args, "Failed to delete resume task.", ignoreFailures: true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    public static void RequestReboot(int? delaySec)
    {
        EnsureWindows("Reboot requests are only supported on Windows.");
        var delay = delaySec ?? 10;
        if (delay < 0)
        {
            delay = 0;
        }

        var args = $"/r /t {delay} /f";
        ExecuteProcess("shutdown.exe", args, "Failed to initiate reboot.");
    }

    public static async Task FinalizeSessionAsync(string caseRunFolder, string runId)
    {
        var sessionPath = GetSessionPath(caseRunFolder);
        var session = await LoadSessionAsync(sessionPath);
        if (session is not null)
        {
            session.State = "Finalized";
            await WriteSessionAsync(sessionPath, session);
        }

        var controlDir = GetControlDirectory(caseRunFolder);
        var rebootPath = GetRebootRequestPath(controlDir);
        if (File.Exists(rebootPath))
        {
            File.Delete(rebootPath);
        }

        DeleteResumeTask(runId);
    }

    private static void EnsureWindows(string message)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void ExecuteProcess(string fileName, string arguments, string errorMessage, bool ignoreFailures = false)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process is null)
        {
            throw new InvalidOperationException($"{errorMessage} Process could not be started.");
        }

        process.WaitForExit();
        if (process.ExitCode != 0 && !ignoreFailures)
        {
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"{errorMessage} ExitCode={process.ExitCode}. {output} {error}".Trim());
        }
    }
}
