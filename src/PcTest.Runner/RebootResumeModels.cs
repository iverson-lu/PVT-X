using System.Diagnostics;
using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Contracts.Requests;

namespace PcTest.Runner;

public enum RebootRequestParseStatus
{
    None,
    Valid,
    Invalid
}

public sealed class RebootRequest
{
    public string Type { get; init; } = string.Empty;
    public int NextPhase { get; init; }
    public string Reason { get; init; } = string.Empty;
    public RebootOptions? Reboot { get; init; }
}

public sealed class RebootOptions
{
    public int? DelaySec { get; init; }
}

public static class RebootRequestReader
{
    public static RebootRequestParseStatus TryRead(string controlDir, out RebootRequest? request, out string? error)
    {
        request = null;
        error = null;

        var rebootPath = Path.Combine(controlDir, "reboot.json");
        if (!File.Exists(rebootPath))
        {
            return RebootRequestParseStatus.None;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(rebootPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "reboot.json root must be an object";
                return RebootRequestParseStatus.Invalid;
            }

            var root = doc.RootElement;
            var allowedRootProps = new HashSet<string> { "type", "nextPhase", "reason", "reboot" };
            foreach (var property in root.EnumerateObject())
            {
                if (!allowedRootProps.Contains(property.Name))
                {
                    error = $"Unexpected property '{property.Name}' in reboot.json";
                    return RebootRequestParseStatus.Invalid;
                }
            }

            if (!root.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
            {
                error = "reboot.json must include string property 'type'";
                return RebootRequestParseStatus.Invalid;
            }

            var typeValue = typeProp.GetString() ?? string.Empty;
            if (!string.Equals(typeValue, "control.reboot_required", StringComparison.Ordinal))
            {
                error = "reboot.json type must equal 'control.reboot_required'";
                return RebootRequestParseStatus.Invalid;
            }

            if (!root.TryGetProperty("nextPhase", out var nextPhaseProp) ||
                nextPhaseProp.ValueKind != JsonValueKind.Number ||
                !nextPhaseProp.TryGetInt32(out var nextPhase) ||
                nextPhase < 1)
            {
                error = "reboot.json nextPhase must be an integer >= 1";
                return RebootRequestParseStatus.Invalid;
            }

            if (!root.TryGetProperty("reason", out var reasonProp) || reasonProp.ValueKind != JsonValueKind.String)
            {
                error = "reboot.json must include string property 'reason'";
                return RebootRequestParseStatus.Invalid;
            }

            var reason = reasonProp.GetString();
            if (string.IsNullOrWhiteSpace(reason))
            {
                error = "reboot.json reason must be a non-empty string";
                return RebootRequestParseStatus.Invalid;
            }

            RebootOptions? rebootOptions = null;
            if (root.TryGetProperty("reboot", out var rebootProp))
            {
                if (rebootProp.ValueKind != JsonValueKind.Object)
                {
                    error = "reboot.json reboot must be an object";
                    return RebootRequestParseStatus.Invalid;
                }

                var allowedRebootProps = new HashSet<string> { "delaySec" };
                foreach (var property in rebootProp.EnumerateObject())
                {
                    if (!allowedRebootProps.Contains(property.Name))
                    {
                        error = $"Unexpected property '{property.Name}' in reboot section";
                        return RebootRequestParseStatus.Invalid;
                    }
                }

                if (rebootProp.TryGetProperty("delaySec", out var delayProp))
                {
                    if (delayProp.ValueKind != JsonValueKind.Number || !delayProp.TryGetInt32(out var delaySec) || delaySec < 0)
                    {
                        error = "reboot.json reboot.delaySec must be a non-negative integer";
                        return RebootRequestParseStatus.Invalid;
                    }

                    rebootOptions = new RebootOptions { DelaySec = delaySec };
                }
            }

            request = new RebootRequest
            {
                Type = typeValue,
                NextPhase = nextPhase,
                Reason = reason,
                Reboot = rebootOptions
            };
            return RebootRequestParseStatus.Valid;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse reboot.json: {ex.Message}";
            return RebootRequestParseStatus.Invalid;
        }
    }
}

public sealed class RebootResumeSession
{
    public string RunId { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string State { get; init; } = "PendingResume";
    public int CurrentNodeIndex { get; init; }
    public int NextPhase { get; init; }
    public int ResumeCount { get; init; }
    public string ResumeToken { get; init; } = string.Empty;
    public string? CurrentNodeId { get; init; }
    public string? CurrentChildRunId { get; init; }
    public string? OriginTestId { get; init; }
    public string RunFolder { get; init; } = string.Empty;
    public ResumeRunContext? CaseContext { get; init; }
    public SuiteResumeContext? SuiteContext { get; init; }
    public PlanResumeContext? PlanContext { get; init; }
    public ResumePaths? Paths { get; init; }

    public static string GetSessionPath(string runFolder) => Path.Combine(runFolder, "session.json");

    public async Task SaveAsync()
    {
        var json = JsonDefaults.Serialize(this);
        await File.WriteAllTextAsync(GetSessionPath(RunFolder), json);
    }

    public static async Task<RebootResumeSession> LoadAsync(string runFolder)
    {
        var path = GetSessionPath(runFolder);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"session.json not found: {path}");
        }

        var json = await File.ReadAllTextAsync(path);
        var session = JsonDefaults.Deserialize<RebootResumeSession>(json);
        if (session is null)
        {
            throw new InvalidOperationException("session.json could not be parsed");
        }

        return session;
    }
}

public sealed class ResumePaths
{
    public string TestCasesRoot { get; init; } = string.Empty;
    public string TestSuitesRoot { get; init; } = string.Empty;
    public string TestPlansRoot { get; init; } = string.Empty;
    public string AssetsRoot { get; init; } = string.Empty;
    public string RunsRoot { get; init; } = string.Empty;
}

public sealed class SuiteResumeContext
{
    public string SuiteIdentity { get; init; } = string.Empty;
    public RunRequest RunRequest { get; init; } = new();
    public string? PlanId { get; init; }
    public string? PlanVersion { get; init; }
    public string? ParentPlanRunId { get; init; }
    public string? ParentNodeId { get; init; }
    public string? ParentPlanRunFolder { get; init; }
    public TestPlanManifest? PlanManifest { get; init; }
    public SuiteControls? ControlOverrides { get; init; }
    public int CurrentIteration { get; init; }
}

public sealed class PlanResumeContext
{
    public string PlanIdentity { get; init; } = string.Empty;
    public RunRequest RunRequest { get; init; } = new();
}

public sealed class ResumeRunContext
{
    public TestCaseManifest Manifest { get; init; } = new();
    public string TestCasePath { get; init; } = string.Empty;
    public Dictionary<string, object?> EffectiveInputs { get; init; } = new();
    public Dictionary<string, string> EffectiveEnvironment { get; init; } = new();
    public Dictionary<string, bool> SecretInputs { get; init; } = new();
    public HashSet<string> SecretEnvVars { get; init; } = new();
    public string? WorkingDir { get; init; }
    public int? TimeoutSec { get; init; }
    public string RunsRoot { get; init; } = string.Empty;
    public string AssetsRoot { get; init; } = string.Empty;
    public string? NodeId { get; init; }
    public string? SuiteId { get; init; }
    public string? SuiteVersion { get; init; }
    public string? PlanId { get; init; }
    public string? PlanVersion { get; init; }
    public string? ParentRunId { get; init; }
    public Dictionary<string, JsonElement>? InputTemplates { get; init; }
    public string? RunnerExecutablePath { get; init; }
    public string? RunFolderPath { get; init; }
    public bool IsTopLevel { get; init; }
}

public static class ResumeContextConverter
{
    public static ResumeRunContext FromRunContext(RunContext context, string runFolder)
    {
        return new ResumeRunContext
        {
            Manifest = context.Manifest,
            TestCasePath = context.TestCasePath,
            EffectiveInputs = context.EffectiveInputs,
            EffectiveEnvironment = context.EffectiveEnvironment,
            SecretInputs = context.SecretInputs,
            SecretEnvVars = context.SecretEnvVars,
            WorkingDir = context.WorkingDir,
            TimeoutSec = context.TimeoutSec,
            RunsRoot = context.RunsRoot,
            AssetsRoot = context.AssetsRoot,
            NodeId = context.NodeId,
            SuiteId = context.SuiteId,
            SuiteVersion = context.SuiteVersion,
            PlanId = context.PlanId,
            PlanVersion = context.PlanVersion,
            ParentRunId = context.ParentRunId,
            InputTemplates = context.InputTemplates,
            RunnerExecutablePath = context.RunnerExecutablePath,
            RunFolderPath = runFolder,
            IsTopLevel = context.IsTopLevel
        };
    }

    public static RunContext ToRunContext(ResumeRunContext resumeContext, string runId, int phase, bool isResume)
    {
        return new RunContext
        {
            RunId = runId,
            Manifest = resumeContext.Manifest,
            TestCasePath = resumeContext.TestCasePath,
            EffectiveInputs = resumeContext.EffectiveInputs,
            EffectiveEnvironment = resumeContext.EffectiveEnvironment,
            SecretInputs = resumeContext.SecretInputs,
            SecretEnvVars = resumeContext.SecretEnvVars,
            WorkingDir = resumeContext.WorkingDir,
            TimeoutSec = resumeContext.TimeoutSec,
            RunsRoot = resumeContext.RunsRoot,
            AssetsRoot = resumeContext.AssetsRoot,
            NodeId = resumeContext.NodeId,
            SuiteId = resumeContext.SuiteId,
            SuiteVersion = resumeContext.SuiteVersion,
            PlanId = resumeContext.PlanId,
            PlanVersion = resumeContext.PlanVersion,
            ParentRunId = resumeContext.ParentRunId,
            InputTemplates = resumeContext.InputTemplates,
            Phase = phase,
            IsResume = isResume,
            IsTopLevel = resumeContext.IsTopLevel,
            RunnerExecutablePath = resumeContext.RunnerExecutablePath ?? string.Empty,
            RunFolderPath = resumeContext.RunFolderPath
        };
    }
}

public static class ResumeTaskScheduler
{
    public static void CreateResumeTask(string runId, string resumeToken, string runnerExecutablePath, string runsRoot)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Resume task creation is only supported on Windows.");
        }

        var taskName = GetTaskName(runId);
        var action = $"\"{runnerExecutablePath}\" --resume --runId \"{runId}\" --token \"{resumeToken}\" --runsRoot \"{runsRoot}\"";

        var psi = new ProcessStartInfo("schtasks.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        psi.ArgumentList.Add("/Create");
        psi.ArgumentList.Add("/F");
        psi.ArgumentList.Add("/SC");
        psi.ArgumentList.Add("ONSTART");
        psi.ArgumentList.Add("/RU");
        psi.ArgumentList.Add("SYSTEM");
        psi.ArgumentList.Add("/TN");
        psi.ArgumentList.Add(taskName);
        psi.ArgumentList.Add("/TR");
        psi.ArgumentList.Add(action);

        RunProcess(psi, "create resume task");
    }

    public static void DeleteResumeTask(string runId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var psi = new ProcessStartInfo("schtasks.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        psi.ArgumentList.Add("/Delete");
        psi.ArgumentList.Add("/F");
        psi.ArgumentList.Add("/TN");
        psi.ArgumentList.Add(GetTaskName(runId));

        RunProcess(psi, "delete resume task");
    }

    internal static void RunProcess(ProcessStartInfo psi, string actionDescription)
    {
        using var process = Process.Start(psi);
        if (process is null)
        {
            throw new InvalidOperationException($"Failed to {actionDescription}.");
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"Failed to {actionDescription}. ExitCode={process.ExitCode} Error={error}");
        }
    }

    public static string GetTaskName(string runId) => $"PVTX-Resume-{runId}";
}

public static class RebootExecutor
{
    public static void RestartMachine(int? delaySec)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Reboot is only supported on Windows.");
        }

        var delay = Math.Max(0, delaySec ?? 0);
        var psi = new ProcessStartInfo("shutdown.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        psi.ArgumentList.Add("/r");
        psi.ArgumentList.Add("/t");
        psi.ArgumentList.Add(delay.ToString());
        psi.ArgumentList.Add("/f");

        ResumeTaskScheduler.RunProcess(psi, "reboot machine");
    }
}
