using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Validation;

namespace PcTest.Runner;

public static class RebootResumeManager
{
    public const string ControlFileName = "reboot.json";
    public const string SessionFileName = "session.json";

    public static string GetControlDir(string runFolder)
        => Path.Combine(runFolder, "control");

    public static string GetSessionPath(string runFolder)
        => Path.Combine(runFolder, "artifacts", SessionFileName);

    public static bool TryReadRebootRequest(string controlDir, out RebootControlRequest? request, out string? error)
    {
        request = null;
        error = null;
        var path = Path.Combine(controlDir, ControlFileName);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            var doc = JsonDocument.Parse(bytes);
            if (!doc.RootElement.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
            {
                error = "reboot.json missing required string 'type'";
                return true;
            }

            if (!doc.RootElement.TryGetProperty("nextPhase", out var nextPhaseProp) || nextPhaseProp.ValueKind != JsonValueKind.Number)
            {
                error = "reboot.json missing required integer 'nextPhase'";
                return true;
            }

            if (!doc.RootElement.TryGetProperty("reason", out var reasonProp) || reasonProp.ValueKind != JsonValueKind.String)
            {
                error = "reboot.json missing required string 'reason'";
                return true;
            }

            var type = typeProp.GetString() ?? string.Empty;
            if (!string.Equals(type, "control.reboot_required", StringComparison.Ordinal))
            {
                error = "reboot.json 'type' must be 'control.reboot_required'";
                return true;
            }

            var nextPhase = nextPhaseProp.GetInt32();
            if (nextPhase < 1)
            {
                error = "reboot.json 'nextPhase' must be >= 1";
                return true;
            }

            var reason = reasonProp.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(reason))
            {
                error = "reboot.json 'reason' must be a non-empty string";
                return true;
            }

            var reboot = new RebootControlOptions();
            if (doc.RootElement.TryGetProperty("reboot", out var rebootProp) && rebootProp.ValueKind == JsonValueKind.Object)
            {
                if (rebootProp.TryGetProperty("delaySec", out var delayProp) && delayProp.ValueKind == JsonValueKind.Number)
                {
                    reboot.DelaySec = delayProp.GetInt32();
                    if (reboot.DelaySec < 0)
                    {
                        error = "reboot.json 'reboot.delaySec' must be >= 0";
                        return true;
                    }
                }
            }

            request = new RebootControlRequest
            {
                Type = type,
                NextPhase = nextPhase,
                Reason = reason,
                Reboot = reboot
            };
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse reboot.json: {ex.Message}";
            return true;
        }
    }

    public static void SaveSession(RebootResumeSession session)
    {
        var sessionPath = GetSessionPath(session.RunFolder);
        var sessionDir = Path.GetDirectoryName(sessionPath);
        if (!string.IsNullOrEmpty(sessionDir))
        {
            Directory.CreateDirectory(sessionDir);
        }

        var json = JsonDefaults.Serialize(session);
        File.WriteAllText(sessionPath, json, Encoding.UTF8);
    }

    public static RebootResumeSession LoadSession(string runsRoot, string runId)
    {
        var runFolder = Path.Combine(PathUtils.NormalizePath(runsRoot), runId);
        return LoadSessionForRunFolder(runFolder);
    }

    public static RebootResumeSession LoadSessionForRunFolder(string runFolder)
    {
        var sessionPath = GetSessionPath(runFolder);
        if (!File.Exists(sessionPath))
        {
            throw new InvalidOperationException($"Resume session not found at {sessionPath}");
        }

        var json = File.ReadAllText(sessionPath, Encoding.UTF8);
        var session = JsonDefaults.Deserialize<RebootResumeSession>(json)
            ?? throw new InvalidOperationException("Failed to deserialize resume session");

        if (string.IsNullOrWhiteSpace(session.RunFolder))
        {
            session.RunFolder = runFolder;
        }

        if (string.IsNullOrWhiteSpace(session.RunsRoot))
        {
            var inferredRunsRoot = Directory.GetParent(runFolder)?.FullName
                                   ?? Path.GetDirectoryName(runFolder)
                                   ?? runFolder;
            session.RunsRoot = PathUtils.NormalizePath(inferredRunsRoot);
        }

        return session;
    }

    public static void CreateResumeTask(RebootResumeSession session, string runnerPath)
    {
        var taskName = GetResumeTaskName(session.RunId);
        var command = new StringBuilder();
        command.Append($"\"{runnerPath}\" --resume --runId \"{session.RunId}\" --token \"{session.ResumeToken}\" --runsRoot \"{session.RunsRoot}\"");

        if (!string.IsNullOrWhiteSpace(session.CasesRoot))
        {
            command.Append($" --casesRoot \"{session.CasesRoot}\"");
        }

        if (!string.IsNullOrWhiteSpace(session.SuitesRoot))
        {
            command.Append($" --suitesRoot \"{session.SuitesRoot}\"");
        }

        if (!string.IsNullOrWhiteSpace(session.PlansRoot))
        {
            command.Append($" --plansRoot \"{session.PlansRoot}\"");
        }

        var escapedCommand = command.ToString().Replace("\"", "\"\"");
        var arguments = $"/Create /TN \"{taskName}\" /SC ONSTART /RL HIGHEST /F /TR \"{escapedCommand}\"";
        RunProcess("schtasks", arguments);
    }

    public static void DeleteResumeTask(string runId)
    {
        var taskName = GetResumeTaskName(runId);
        try
        {
            RunProcess("schtasks", $"/Delete /TN \"{taskName}\" /F");
        }
        catch
        {
            // Best effort cleanup
        }
    }

    public static void RequestReboot(int? delaySec)
    {
        var delay = delaySec.GetValueOrDefault(0);
        var command = delay > 0
            ? $"Start-Sleep -Seconds {delay}; Restart-Computer -Force"
            : "Restart-Computer -Force";
        RunProcess("pwsh", $"-NoProfile -Command \"{command}\"");
    }

    public static void FinalizeSession(RebootResumeSession session)
    {
        session.State = "Finalized";
        SaveSession(session);
        DeleteResumeTask(session.RunId);

        var rebootPath = Path.Combine(GetControlDir(session.RunFolder), ControlFileName);
        if (File.Exists(rebootPath))
        {
            File.Delete(rebootPath);
        }
    }

    private static string GetResumeTaskName(string runId)
        => $"PVTX-Resume-{runId}";

    private static void RunProcess(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"Failed to start process: {fileName}");
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"{fileName} failed with exit code {process.ExitCode}: {stderr}");
        }
    }
}
