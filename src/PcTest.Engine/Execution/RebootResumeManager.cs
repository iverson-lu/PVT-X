using System.Diagnostics;
using System.Text.Json;
using PcTest.Contracts;
using PcTest.Runner;

namespace PcTest.Engine.Execution;

public sealed class RebootResumeManager
{
    private const int MaxResumeCount = 1;

    public async Task HandleRebootAsync(RebootRequiredException rebootException)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("Reboot resume is only supported on Windows.");
        }

        var session = CreateSession(rebootException);
        await RebootSessionStore.SaveAsync(rebootException.CaseRunFolder, session);

        CreateResumeTask(session, rebootException.Context.RunsRoot);

        InvokeReboot(rebootException.Request.DelaySec);
    }

    public async Task<RebootSession> LoadAndValidateAsync(string runsRoot, string runId, string token)
    {
        var caseRunFolder = PathUtils.NormalizePath(Path.Combine(runsRoot, runId));
        var session = RebootSessionStore.Load(caseRunFolder);

        if (!string.Equals(session.ResumeToken, token, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Resume token validation failed.");
        }

        session.ResumeCount++;
        if (session.ResumeCount > MaxResumeCount)
        {
            session.State = "Aborted";
            await RebootSessionStore.SaveAsync(caseRunFolder, session);
            DeleteResumeTask(session.RunId);
            throw new InvalidOperationException("Resume loop detected. Aborting run.");
        }

        session.State = "Resuming";
        await RebootSessionStore.SaveAsync(caseRunFolder, session);
        return session;
    }

    public async Task FinalizeAsync(RebootSession session)
    {
        session.State = "Finalized";
        await RebootSessionStore.SaveAsync(session.CaseRunFolder, session);

        DeleteResumeTask(session.RunId);

        var rebootPath = Path.Combine(session.CaseRunFolder, "control", "reboot.json");
        if (File.Exists(rebootPath))
        {
            File.Delete(rebootPath);
        }
    }

    private static RebootSession CreateSession(RebootRequiredException rebootException)
    {
        var context = rebootException.Context;
        var session = new RebootSession
        {
            RunId = context.RunId,
            EntityType = "TestCase",
            EntityId = context.Manifest.Identity,
            CurrentCaseId = context.Manifest.Id,
            NextPhase = rebootException.Request.NextPhase,
            ResumeToken = Guid.NewGuid().ToString("N"),
            ResumeCount = 0,
            State = "PendingResume",
            CaseRunFolder = rebootException.CaseRunFolder,
            RunsRoot = context.RunsRoot,
            TestCasePath = context.TestCasePath,
            AssetsRoot = context.AssetsRoot,
            WorkingDir = context.WorkingDir,
            TimeoutSec = context.TimeoutSec,
            EffectiveEnvironment = new Dictionary<string, string>(context.EffectiveEnvironment),
            EffectiveInputs = ToJsonElements(context.EffectiveInputs),
            SecretInputs = new Dictionary<string, bool>(context.SecretInputs),
            SecretEnvVars = new HashSet<string>(context.SecretEnvVars),
            InputTemplates = context.InputTemplates
        };

        return session;
    }

    private static Dictionary<string, JsonElement> ToJsonElements(Dictionary<string, object?> inputs)
    {
        var result = new Dictionary<string, JsonElement>(inputs.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in inputs)
        {
            if (value is JsonElement jsonElement)
            {
                result[key] = jsonElement;
                continue;
            }

            var element = JsonSerializer.SerializeToElement(value, JsonDefaults.WriteOptions);
            result[key] = element;
        }

        return result;
    }

    private static void CreateResumeTask(RebootSession session, string runsRoot)
    {
        var runnerPath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(runnerPath))
        {
            throw new InvalidOperationException("Unable to determine runner executable path.");
        }

        var taskName = GetTaskName(session.RunId);
        var arguments = $"--resume --runId \"{session.RunId}\" --token \"{session.ResumeToken}\" --runsRoot \"{runsRoot}\"";
        var command = $"\"{runnerPath}\" {arguments}";

        var startInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/Create /SC ONSTART /TN \"{taskName}\" /TR \"{command}\" /RL HIGHEST /RU SYSTEM /F",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Failed to create resume task.");
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"Failed to create resume task: {error}");
        }
    }

    private static void DeleteResumeTask(string runId)
    {
        var taskName = GetTaskName(runId);
        var startInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/Delete /TN \"{taskName}\" /F",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return;
        }

        process.WaitForExit();
    }

    private static void InvokeReboot(int? delaySec)
    {
        var delay = Math.Max(0, delaySec ?? 10);
        var startInfo = new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = $"/r /t {delay}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        Process.Start(startInfo);
    }

    private static string GetTaskName(string runId)
    {
        return $"PVTX-Resume-{runId}";
    }
}
