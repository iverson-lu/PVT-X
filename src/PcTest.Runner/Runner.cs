using PcTest.Contracts;
using System.Diagnostics;

namespace PcTest.Runner;

public sealed class TestCaseRunContext
{
    public required string RunId { get; init; }
    public required string RunsRoot { get; init; }
    public required string TestCaseManifestPath { get; init; }
    public required TestCaseManifest TestCaseManifest { get; init; }
    public required ManifestSnapshot ManifestSnapshot { get; init; }
    public required Dictionary<string, object?> EffectiveInputs { get; init; }
    public required HashSet<string> SecretInputs { get; init; }
    public required Dictionary<string, string> EffectiveEnvironment { get; init; }
    public string? NodeId { get; init; }
    public string? SuiteId { get; init; }
    public string? SuiteVersion { get; init; }
    public string? PlanId { get; init; }
    public string? PlanVersion { get; init; }
    public string? WorkingDir { get; init; }
}

public sealed class ProcessResult
{
    public required int? ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public required bool TimedOut { get; init; }
    public required bool Aborted { get; init; }
    public required DateTimeOffset StartTime { get; init; }
    public required DateTimeOffset EndTime { get; init; }
}

public interface IProcessRunner
{
    ProcessResult Run(ProcessStartInfo startInfo, TimeSpan? timeout);
}

public sealed class DefaultProcessRunner : IProcessRunner
{
    public ProcessResult Run(ProcessStartInfo startInfo, TimeSpan? timeout)
    {
        using var process = new Process { StartInfo = startInfo };
        var stdout = new List<string>();
        var stderr = new List<string>();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.Add(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.Add(e.Data); };
        var startTime = DateTimeOffset.UtcNow;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        bool exited;
        if (timeout.HasValue)
        {
            exited = process.WaitForExit((int)timeout.Value.TotalMilliseconds);
            if (!exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
            }
        }
        else
        {
            process.WaitForExit();
            exited = true;
        }

        var endTime = DateTimeOffset.UtcNow;
        return new ProcessResult
        {
            ExitCode = exited ? process.ExitCode : null,
            StandardOutput = string.Join(Environment.NewLine, stdout),
            StandardError = string.Join(Environment.NewLine, stderr),
            TimedOut = timeout.HasValue && !exited,
            Aborted = false,
            StartTime = startTime,
            EndTime = endTime
        };
    }
}

public sealed class TestCaseRunner
{
    private readonly IProcessRunner _processRunner;

    public TestCaseRunner(IProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new DefaultProcessRunner();
    }

    public RunnerResult Run(TestCaseRunContext context)
    {
        var runFolder = EnsureRunFolder(context.RunsRoot, context.RunId);
        var manifestPath = Path.Combine(runFolder, "manifest.json");
        var paramsPath = Path.Combine(runFolder, "params.json");
        var stdoutPath = Path.Combine(runFolder, "stdout.log");
        var stderrPath = Path.Combine(runFolder, "stderr.log");
        var envPath = Path.Combine(runFolder, "env.json");
        var resultPath = Path.Combine(runFolder, "result.json");
        var eventsPath = Path.Combine(runFolder, "events.jsonl");

        JsonUtilities.WriteFile(manifestPath, RedactManifest(context.ManifestSnapshot, context.SecretInputs));
        JsonUtilities.WriteFile(paramsPath, RedactInputs(context.EffectiveInputs, context.SecretInputs));

        var events = new List<ErrorEvent>();
        if (context.SecretInputs.Count > 0)
        {
            foreach (var secret in context.SecretInputs)
            {
                events.Add(new ErrorEvent(DateTimeOffset.UtcNow, "Warning", "EnvRef.SecretOnCommandLine", new { parameter = secret, nodeId = context.NodeId }));
            }
        }

        var (workingDir, workingDirError) = ResolveWorkingDir(runFolder, context.WorkingDir);
        if (workingDirError is not null)
        {
            WriteEvents(eventsPath, events);
            WriteStdLogs(stdoutPath, stderrPath, string.Empty, string.Empty);
            WriteEnv(envPath);
            var result = BuildResult(context, RunStatus.Error, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null,
                new ErrorDetail { Type = ErrorType.RunnerError, Source = "Runner", Message = workingDirError });
            JsonUtilities.WriteFile(resultPath, result);
            return new RunnerResult { RunId = context.RunId, Status = result.Status, StartTime = result.StartTime, EndTime = result.EndTime, ExitCode = null, Error = result.Error, WorkingDirectory = workingDir };
        }

        var validationError = ValidateInputs(context, runFolder, workingDir);
        if (validationError is not null)
        {
            WriteEvents(eventsPath, events);
            WriteStdLogs(stdoutPath, stderrPath, string.Empty, string.Empty);
            WriteEnv(envPath);
            var result = BuildResult(context, RunStatus.Error, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null,
                new ErrorDetail { Type = ErrorType.RunnerError, Source = "Runner", Message = validationError });
            JsonUtilities.WriteFile(resultPath, result);
            return new RunnerResult { RunId = context.RunId, Status = result.Status, StartTime = result.StartTime, EndTime = result.EndTime, ExitCode = null, Error = result.Error, WorkingDirectory = workingDir };
        }

        var startInfo = BuildProcessStartInfo(context, runFolder, workingDir, out var arguments);
        ProcessResult processResult;
        try
        {
            processResult = _processRunner.Run(startInfo, context.TestCaseManifest.TimeoutSec is { } timeout ? TimeSpan.FromSeconds(timeout) : null);
        }
        catch (Exception ex)
        {
            WriteEvents(eventsPath, events);
            WriteStdLogs(stdoutPath, stderrPath, string.Empty, string.Empty);
            WriteEnv(envPath);
            var error = new ErrorDetail { Type = ErrorType.RunnerError, Source = "Runner", Message = ex.Message, Stack = ex.StackTrace };
            var result = BuildResult(context, RunStatus.Error, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, error);
            JsonUtilities.WriteFile(resultPath, result);
            return new RunnerResult { RunId = context.RunId, Status = result.Status, StartTime = result.StartTime, EndTime = result.EndTime, ExitCode = null, Error = error, WorkingDirectory = workingDir };
        }

        var status = MapStatus(processResult);
        var errorDetail = BuildErrorDetail(processResult, status);
        var stdout = RedactText(processResult.StandardOutput, context.SecretInputs, context.EffectiveInputs);
        var stderr = RedactText(processResult.StandardError, context.SecretInputs, context.EffectiveInputs);
        WriteStdLogs(stdoutPath, stderrPath, stdout, stderr);
        WriteEnv(envPath);
        WriteEvents(eventsPath, events);

        var resultPayload = BuildResult(context, status, processResult.StartTime, processResult.EndTime, processResult.ExitCode, errorDetail);
        JsonUtilities.WriteFile(resultPath, resultPayload);

        return new RunnerResult
        {
            RunId = context.RunId,
            Status = status,
            StartTime = processResult.StartTime,
            EndTime = processResult.EndTime,
            ExitCode = processResult.ExitCode,
            Error = errorDetail,
            WorkingDirectory = workingDir
        };
    }

    private static RunStatus MapStatus(ProcessResult result)
    {
        if (result.TimedOut)
        {
            return RunStatus.Timeout;
        }

        if (result.Aborted)
        {
            return RunStatus.Aborted;
        }

        if (result.ExitCode is null)
        {
            return RunStatus.Error;
        }

        return result.ExitCode switch
        {
            0 => RunStatus.Passed,
            1 => RunStatus.Failed,
            _ => RunStatus.Error
        };
    }

    private static ErrorDetail? BuildErrorDetail(ProcessResult result, RunStatus status)
    {
        if (status == RunStatus.Timeout)
        {
            return new ErrorDetail { Type = ErrorType.Timeout, Source = "Runner", Message = "Execution timed out." };
        }

        if (status == RunStatus.Aborted)
        {
            return new ErrorDetail { Type = ErrorType.Aborted, Source = "Runner", Message = "Execution aborted." };
        }

        if (status == RunStatus.Error)
        {
            if (result.ExitCode is null)
            {
                return new ErrorDetail { Type = ErrorType.RunnerError, Source = "Runner", Message = "Runner failed before execution." };
            }

            return new ErrorDetail { Type = ErrorType.ScriptError, Source = "Script", Message = "Script returned error exit code." };
        }

        return null;
    }

    private static TestCaseResult BuildResult(TestCaseRunContext context, RunStatus status, DateTimeOffset start, DateTimeOffset end, int? exitCode, ErrorDetail? error)
    {
        var effectiveInputs = RedactInputs(context.EffectiveInputs, context.SecretInputs);
        return new TestCaseResult
        {
            SchemaVersion = "1.5.0",
            NodeId = context.NodeId,
            TestId = context.TestCaseManifest.Id,
            TestVersion = context.TestCaseManifest.Version,
            SuiteId = context.SuiteId,
            SuiteVersion = context.SuiteVersion,
            PlanId = context.PlanId,
            PlanVersion = context.PlanVersion,
            Status = status,
            StartTime = start,
            EndTime = end,
            ExitCode = exitCode,
            EffectiveInputs = effectiveInputs,
            Error = error
        };
    }

    private static void WriteStdLogs(string stdoutPath, string stderrPath, string stdout, string stderr)
    {
        File.WriteAllText(stdoutPath, stdout, JsonUtilities.Utf8NoBom);
        File.WriteAllText(stderrPath, stderr, JsonUtilities.Utf8NoBom);
    }

    private static void WriteEnv(string envPath)
    {
        JsonUtilities.WriteFile(envPath, new
        {
            osVersion = Environment.OSVersion.VersionString,
            runnerVersion = "1.0.0",
            powerShellVersion = Environment.GetEnvironmentVariable("POWERSHELL_VERSION") ?? "unknown",
            isElevated = false
        });
    }

    private static void WriteEvents(string path, List<ErrorEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        using var stream = new StreamWriter(path, append: false, JsonUtilities.Utf8NoBom);
        foreach (var ev in events)
        {
            stream.WriteLine(ev.ToJson());
        }
    }

    private static object RedactManifest(ManifestSnapshot snapshot, HashSet<string> secretInputs)
    {
        return new
        {
            sourceManifest = snapshot.SourceManifest,
            resolvedRef = snapshot.ResolvedRef,
            resolvedIdentity = snapshot.ResolvedIdentity,
            effectiveEnvironment = snapshot.EffectiveEnvironment,
            effectiveInputs = RedactInputs(snapshot.EffectiveInputs, secretInputs),
            inputTemplates = snapshot.InputTemplates,
            resolvedAt = snapshot.ResolvedAt,
            engineVersion = snapshot.EngineVersion
        };
    }

    private static Dictionary<string, object?> RedactInputs(Dictionary<string, object?> inputs, HashSet<string> secretInputs)
    {
        var redacted = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in inputs)
        {
            redacted[key] = secretInputs.Contains(key) ? "***" : value;
        }

        return redacted;
    }

    private static string RedactText(string text, HashSet<string> secretInputs, Dictionary<string, object?> inputs)
    {
        var redacted = text;
        foreach (var name in secretInputs)
        {
            if (inputs.TryGetValue(name, out var value) && value is string str && !string.IsNullOrEmpty(str))
            {
                redacted = redacted.Replace(str, "***", StringComparison.Ordinal);
            }
        }

        return redacted;
    }

    private static string EnsureRunFolder(string runsRoot, string runId)
    {
        Directory.CreateDirectory(runsRoot);
        var folder = Path.Combine(runsRoot, runId);
        var attempt = 0;
        while (Directory.Exists(folder))
        {
            attempt++;
            folder = Path.Combine(runsRoot, $"{runId}-{attempt}");
        }

        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "artifacts"));
        return folder;
    }

    private static (string WorkingDir, string? Error) ResolveWorkingDir(string runFolder, string? workingDir)
    {
        var target = string.IsNullOrWhiteSpace(workingDir) ? runFolder : Path.Combine(runFolder, workingDir);
        var normalized = PathUtilities.NormalizePath(target);
        var resolved = PathUtilities.ResolveLinkTarget(normalized);
        if (!PathUtilities.IsContained(runFolder, resolved))
        {
            return (normalized, "workingDir escaped Run Folder.");
        }

        Directory.CreateDirectory(resolved);
        return (resolved, null);
    }

    private static string? ValidateInputs(TestCaseRunContext context, string runFolder, string workingDir)
    {
        var parameters = context.TestCaseManifest.Parameters ?? Array.Empty<ParameterDefinition>();
        foreach (var parameter in parameters)
        {
            if (!context.EffectiveInputs.TryGetValue(parameter.Name, out var value) || value is null)
            {
                continue;
            }

            if (!parameter.Type.StartsWith("path", StringComparison.OrdinalIgnoreCase)
                && !parameter.Type.StartsWith("file", StringComparison.OrdinalIgnoreCase)
                && !parameter.Type.StartsWith("folder", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values = parameter.Type.EndsWith("[]", StringComparison.Ordinal) && value is System.Collections.IEnumerable enumerable and not string
                ? enumerable.Cast<object?>().Select(v => v?.ToString()).Where(v => v is not null).ToArray()
                : [value.ToString()];

            foreach (var raw in values)
            {
                if (raw is null)
                {
                    continue;
                }

                var resolved = Path.IsPathRooted(raw) ? raw : Path.Combine(workingDir, raw);
                var normalized = PathUtilities.NormalizePath(resolved);
                var real = PathUtilities.ResolveLinkTarget(normalized);
                if (!PathUtilities.IsContained(runFolder, real))
                {
                    return $"Input {parameter.Name} path escaped Run Folder.";
                }

                if (parameter.Type.StartsWith("file", StringComparison.OrdinalIgnoreCase) && !File.Exists(real))
                {
                    return $"Input {parameter.Name} file does not exist.";
                }

                if (parameter.Type.StartsWith("folder", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(real))
                {
                    return $"Input {parameter.Name} folder does not exist.";
                }
            }
        }

        return null;
    }

    private static ProcessStartInfo BuildProcessStartInfo(TestCaseRunContext context, string runFolder, string workingDir, out List<string> arguments)
    {
        var scriptPath = Path.Combine(Path.GetDirectoryName(context.TestCaseManifestPath) ?? string.Empty, "run.ps1");
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        arguments = [];
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        arguments.Add("-File");
        arguments.Add(scriptPath);

        foreach (var (name, value) in context.EffectiveInputs)
        {
            if (value is null)
            {
                continue;
            }

            startInfo.ArgumentList.Add($"-{name}");
            arguments.Add($"-{name}");

            if (value is System.Collections.IEnumerable enumerable and not string)
            {
                foreach (var item in enumerable.Cast<object?>())
                {
                    var serialized = SerializeArgument(item);
                    startInfo.ArgumentList.Add(serialized);
                    arguments.Add(serialized);
                }

                continue;
            }

            var argumentValue = SerializeArgument(value);
            startInfo.ArgumentList.Add(argumentValue);
            arguments.Add(argumentValue);
        }

        foreach (var (key, value) in context.EffectiveEnvironment)
        {
            startInfo.Environment[key] = value;
        }

        return startInfo;
    }

    private static string SerializeArgument(object? value)
    {
        return value switch
        {
            bool boolean => boolean ? "$true" : "$false",
            _ => value?.ToString() ?? string.Empty
        };
    }
}
