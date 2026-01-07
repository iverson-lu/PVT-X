using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;

namespace PcTest.Runner;

public sealed class RebootControlRequest
{
    public int NextPhase { get; init; }
    public string Reason { get; init; } = string.Empty;
    public int? DelaySec { get; init; }
}

public sealed class ResumeSession
{
    public string RunId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? CurrentCaseId { get; set; }
    public int NextPhase { get; set; }
    public string ResumeToken { get; set; } = string.Empty;
    public int ResumeCount { get; set; }
    public string State { get; set; } = "PendingResume";
    public string? TestCasePath { get; set; }
    public TestCaseManifest Manifest { get; set; } = new();
    public Dictionary<string, object?> EffectiveInputs { get; set; } = new();
    public Dictionary<string, string> EffectiveEnvironment { get; set; } = new();
    public Dictionary<string, bool> SecretInputs { get; set; } = new();
    public HashSet<string> SecretEnvVars { get; set; } = new();
    public Dictionary<string, JsonElement>? InputTemplates { get; set; }
    public string? WorkingDir { get; set; }
    public int? TimeoutSec { get; set; }
    public string AssetsRoot { get; set; } = string.Empty;
    public string? NodeId { get; set; }
    public string? SuiteId { get; set; }
    public string? SuiteVersion { get; set; }
    public string? PlanId { get; set; }
    public string? PlanVersion { get; set; }
    public string? ParentRunId { get; set; }
}

public static class RebootResumeManager
{
    public const string ControlDirectoryName = "control";
    public const string RebootRequestFileName = "reboot.json";

    public static string GetControlDir(string caseRunFolder)
    {
        return Path.Combine(caseRunFolder, ControlDirectoryName);
    }

    public static string GetRebootRequestPath(string controlDir)
    {
        return Path.Combine(controlDir, RebootRequestFileName);
    }

    public static string GetSessionPath(string caseRunFolder)
    {
        return Path.Combine(caseRunFolder, "session.json");
    }

    public static string GetResumeTaskName(string runId)
    {
        return $"PVTX-Resume-{runId}";
    }

    public static string CreateResumeToken()
    {
        return Guid.NewGuid().ToString("N");
    }

    public static RebootControlRequest? ReadRebootRequest(string controlDir, out string? error)
    {
        error = null;
        var rebootPath = GetRebootRequestPath(controlDir);
        if (!File.Exists(rebootPath))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(rebootPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "reboot.json must contain a JSON object";
                return null;
            }

            var root = doc.RootElement;
            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "type",
                "nextPhase",
                "reason",
                "reboot"
            };

            foreach (var property in root.EnumerateObject())
            {
                if (!allowed.Contains(property.Name))
                {
                    error = $"Unexpected property '{property.Name}' in reboot.json";
                    return null;
                }
            }

            if (!root.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String ||
                typeElement.GetString() != "control.reboot_required")
            {
                error = "reboot.json type must equal 'control.reboot_required'";
                return null;
            }

            if (!root.TryGetProperty("nextPhase", out var nextPhaseElement) ||
                nextPhaseElement.ValueKind != JsonValueKind.Number ||
                !nextPhaseElement.TryGetInt32(out var nextPhase) ||
                nextPhase < 1)
            {
                error = "reboot.json nextPhase must be an integer >= 1";
                return null;
            }

            if (!root.TryGetProperty("reason", out var reasonElement) ||
                reasonElement.ValueKind != JsonValueKind.String)
            {
                error = "reboot.json reason must be a non-empty string";
                return null;
            }

            var reason = reasonElement.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(reason))
            {
                error = "reboot.json reason must be a non-empty string";
                return null;
            }

            int? delaySec = null;
            if (root.TryGetProperty("reboot", out var rebootElement))
            {
                if (rebootElement.ValueKind != JsonValueKind.Object)
                {
                    error = "reboot.json reboot must be an object";
                    return null;
                }

                var rebootAllowed = new HashSet<string>(StringComparer.Ordinal)
                {
                    "delaySec"
                };

                foreach (var property in rebootElement.EnumerateObject())
                {
                    if (!rebootAllowed.Contains(property.Name))
                    {
                        error = $"Unexpected property '{property.Name}' in reboot.json reboot section";
                        return null;
                    }
                }

                if (rebootElement.TryGetProperty("delaySec", out var delayElement))
                {
                    if (delayElement.ValueKind != JsonValueKind.Number ||
                        !delayElement.TryGetInt32(out var parsedDelay) ||
                        parsedDelay < 0)
                    {
                        error = "reboot.json reboot.delaySec must be an integer >= 0";
                        return null;
                    }

                    delaySec = parsedDelay;
                }
            }

            return new RebootControlRequest
            {
                NextPhase = nextPhase,
                Reason = reason,
                DelaySec = delaySec
            };
        }
        catch (JsonException ex)
        {
            error = $"reboot.json is invalid JSON: {ex.Message}";
            return null;
        }
    }

    public static async Task SaveSessionAsync(string caseRunFolder, ResumeSession session)
    {
        var path = GetSessionPath(caseRunFolder);
        var json = JsonDefaults.Serialize(session);
        await File.WriteAllTextAsync(path, json);
    }

    public static ResumeSession LoadSession(string caseRunFolder)
    {
        var path = GetSessionPath(caseRunFolder);
        var json = File.ReadAllText(path);
        return JsonDefaults.Deserialize<ResumeSession>(json)
            ?? throw new InvalidOperationException("session.json could not be parsed");
    }

    public static bool TryLoadSession(string caseRunFolder, out ResumeSession? session)
    {
        session = null;
        var path = GetSessionPath(caseRunFolder);
        if (!File.Exists(path))
        {
            return false;
        }

        session = LoadSession(caseRunFolder);
        return true;
    }

    public static async Task CreateResumeTaskAsync(string runId, string resumeToken, string runsRoot)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("Resume task creation is only supported on Windows.");
        }

        var runnerPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(runnerPath))
        {
            throw new InvalidOperationException("Unable to determine runner executable path.");
        }

        var taskName = GetResumeTaskName(runId);
        var arguments = $"--resume --runId \"{runId}\" --token \"{resumeToken}\" --runsRoot \"{runsRoot}\"";
        var taskAction = $"\"{runnerPath}\" {arguments}";
        var taskArgs = $"/Create /TN \"{taskName}\" /SC ONSTART /RL HIGHEST /RU SYSTEM /TR \"{taskAction}\" /F";

        await RunProcessAsync("schtasks.exe", taskArgs);
    }

    public static async Task DeleteResumeTaskAsync(string runId)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var taskName = GetResumeTaskName(runId);
        var taskArgs = $"/Delete /TN \"{taskName}\" /F";

        try
        {
            await RunProcessAsync("schtasks.exe", taskArgs);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    public static async Task RequestRebootAsync(int? delaySec)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("Reboot is only supported on Windows.");
        }

        var delay = delaySec.GetValueOrDefault();
        var command = delay > 0
            ? $"Start-Sleep -Seconds {delay}; Restart-Computer -Force"
            : "Restart-Computer -Force";

        await RunProcessAsync("pwsh", $"-NoProfile -Command \"{command}\"");
    }

    private static async Task RunProcessAsync(string fileName, string arguments)
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

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException(
                $"Process '{fileName}' failed with exit code {process.ExitCode}: {stderr}");
        }
    }
}
