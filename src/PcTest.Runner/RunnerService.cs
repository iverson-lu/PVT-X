using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Runner;

public sealed class RunnerService
{
    private readonly IProcessRunner _processRunner;

    public RunnerService(IProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new ProcessRunner();
    }

    public async Task<CaseRunResult> RunAsync(CaseRunRequest request, CancellationToken cancellationToken)
    {
        string runId = RunIdGenerator.CreateRunId("R");
        string runFolder = Path.Combine(request.RunsRoot, runId);
        while (Directory.Exists(runFolder))
        {
            runId = RunIdGenerator.CreateRunId("R");
            runFolder = Path.Combine(request.RunsRoot, runId);
        }

        Directory.CreateDirectory(runFolder);

        DateTimeOffset start = DateTimeOffset.UtcNow;
        string resolvedWorkingDir = runFolder;
        string? errorType = null;
        string? errorMessage = null;
        int? exitCode = null;
        string status = "Error";

        string stdout = string.Empty;
        string stderr = string.Empty;
        bool wroteEvents = false;

        try
        {
            if (!TryResolveWorkingDirectory(runFolder, request.WorkingDir, out string workingDir, out string? workingDirError))
            {
                errorType = "RunnerError";
                errorMessage = workingDirError;
                status = "Error";
                await WriteArtifactsAsync(request, runFolder, start, DateTimeOffset.UtcNow, status, exitCode, errorType, errorMessage, stdout, stderr, wroteEvents);
                return new CaseRunResult
                {
                    RunId = runId,
                    RunFolder = runFolder,
                    StartTime = start,
                    EndTime = DateTimeOffset.UtcNow,
                    Status = status,
                    ExitCode = exitCode,
                    ErrorType = errorType,
                    ErrorMessage = errorMessage
                };
            }

            resolvedWorkingDir = workingDir;
            ValidationResult preNodeValidation = ValidateFilesystemInputs(request, workingDir);
            if (!preNodeValidation.IsValid)
            {
                errorType = "RunnerError";
                errorMessage = string.Join("; ", preNodeValidation.Errors.Select(e => e.Message));
                status = "Error";
                await WriteArtifactsAsync(request, runFolder, start, DateTimeOffset.UtcNow, status, exitCode, errorType, errorMessage, stdout, stderr, wroteEvents);
                return new CaseRunResult
                {
                    RunId = runId,
                    RunFolder = runFolder,
                    StartTime = start,
                    EndTime = DateTimeOffset.UtcNow,
                    Status = status,
                    ExitCode = exitCode,
                    ErrorType = errorType,
                    ErrorMessage = errorMessage
                };
            }

            ProcessStartInfo startInfo = BuildStartInfo(request, workingDir);
            if (request.SecretKeys.Count > 0)
            {
                await WriteEventAsync(runFolder, new
                {
                    time = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    code = "EnvRef.SecretOnCommandLine",
                    message = "Secret values were passed on the command line."
                });
                wroteEvents = true;
            }

            ProcessResult processResult = await _processRunner.RunAsync(startInfo, request.TimeoutSec is null ? null : TimeSpan.FromSeconds(request.TimeoutSec.Value), cancellationToken);
            stdout = Redactor.Redact(processResult.Stdout, request.SecretKeys, request.EffectiveInputs);
            stderr = Redactor.Redact(processResult.Stderr, request.SecretKeys, request.EffectiveInputs);

            if (processResult.TimedOut)
            {
                status = "Timeout";
                errorType = "Timeout";
                errorMessage = "Execution timed out.";
            }
            else if (processResult.Aborted)
            {
                status = "Aborted";
                errorType = "Aborted";
                errorMessage = "Execution aborted.";
            }
            else if (processResult.ExitCode is null)
            {
                status = "Error";
                errorType = "RunnerError";
                errorMessage = "Runner failed to capture exit code.";
            }
            else
            {
                exitCode = processResult.ExitCode;
                if (exitCode == 0)
                {
                    status = "Passed";
                }
                else if (exitCode == 1)
                {
                    status = "Failed";
                }
                else
                {
                    status = "Error";
                    errorType = "ScriptError";
                    errorMessage = "Script returned non-standard exit code.";
                }
            }
        }
        catch (OperationCanceledException)
        {
            status = "Aborted";
            errorType = "Aborted";
            errorMessage = "Execution aborted.";
        }
        catch (Exception ex)
        {
            status = "Error";
            errorType = "RunnerError";
            errorMessage = ex.Message;
        }

        DateTimeOffset end = DateTimeOffset.UtcNow;
        await WriteArtifactsAsync(request, runFolder, start, end, status, exitCode, errorType, errorMessage, stdout, stderr, wroteEvents);

        return new CaseRunResult
        {
            RunId = runId,
            RunFolder = runFolder,
            StartTime = start,
            EndTime = end,
            Status = status,
            ExitCode = exitCode,
            ErrorType = errorType,
            ErrorMessage = errorMessage
        };
    }

    private static ValidationResult ValidateFilesystemInputs(CaseRunRequest request, string workingDir)
    {
        ValidationResult validation = new();
        foreach (ParameterDefinitionSnapshot parameter in request.Parameters)
        {
            if (!request.EffectiveInputs.TryGetValue(parameter.Name, out object? value))
            {
                continue;
            }

            if (parameter.Type is "file" or "folder" or "path" or "file[]" or "folder[]" or "path[]")
            {
                IEnumerable<string> paths = ExtractPaths(value);
                foreach (string raw in paths)
                {
                    string resolved = ResolvePath(workingDir, raw);
                    if ((parameter.Type is "file" or "file[]") && !File.Exists(resolved))
                    {
                        validation.Add("Inputs.MissingFile", $"File '{raw}' does not exist.", new Dictionary<string, object?>
                        {
                            ["name"] = parameter.Name,
                            ["path"] = raw
                        });
                    }

                    if ((parameter.Type is "folder" or "folder[]") && !Directory.Exists(resolved))
                    {
                        validation.Add("Inputs.MissingFolder", $"Folder '{raw}' does not exist.", new Dictionary<string, object?>
                        {
                            ["name"] = parameter.Name,
                            ["path"] = raw
                        });
                    }
                }
            }
        }

        return validation;
    }

    private static IEnumerable<string> ExtractPaths(object value)
    {
        if (value is string str)
        {
            return new[] { str };
        }

        if (value is IEnumerable<string> list)
        {
            return list;
        }

        return Array.Empty<string>();
    }

    private static string ResolvePath(string workingDir, string value)
    {
        if (Path.IsPathRooted(value))
        {
            return value;
        }

        return PathUtils.CombineNormalized(workingDir, value);
    }

    private static bool TryResolveWorkingDirectory(string runFolder, string? workingDir, out string resolved, out string? error)
    {
        resolved = runFolder;
        error = null;

        if (string.IsNullOrWhiteSpace(workingDir))
        {
            return true;
        }

        string combined = PathUtils.CombineNormalized(runFolder, workingDir);
        string resolvedPath = PathUtils.ResolvePathWithLinks(combined);
        if (!PathUtils.IsContainedBy(runFolder, resolvedPath))
        {
            error = "Working directory escapes Case Run Folder.";
            return false;
        }

        Directory.CreateDirectory(resolvedPath);
        resolved = resolvedPath;
        return true;
    }

    private static ProcessStartInfo BuildStartInfo(CaseRunRequest request, string workingDir)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = request.PwshPath,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(request.ScriptPath);
        foreach (ParameterDefinitionSnapshot parameter in request.Parameters)
        {
            if (!request.EffectiveInputs.TryGetValue(parameter.Name, out object? value))
            {
                continue;
            }

            foreach (string arg in ArgumentSerializer.Serialize(parameter.Name, parameter.Type, value))
            {
                startInfo.ArgumentList.Add(arg);
            }
        }

        foreach (KeyValuePair<string, string> pair in request.EffectiveEnvironment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        return startInfo;
    }

    private static async Task WriteArtifactsAsync(
        CaseRunRequest request,
        string runFolder,
        DateTimeOffset start,
        DateTimeOffset end,
        string status,
        int? exitCode,
        string? errorType,
        string? errorMessage,
        string stdout,
        string stderr,
        bool wroteEvents)
    {
        object manifestSnapshot = new
        {
            sourceManifest = request.SourceManifest,
            resolvedRef = request.ManifestPath,
            resolvedIdentity = new { id = request.TestId, version = request.TestVersion },
            effectiveEnvironment = request.EffectiveEnvironment,
            effectiveInputs = request.RedactedInputs,
            inputTemplates = request.InputTemplates,
            resolvedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        object result = new
        {
            schemaVersion = "1.5.0",
            runType = "TestCase",
            nodeId = request.NodeId,
            testId = request.TestId,
            testVersion = request.TestVersion,
            suiteId = request.SuiteId,
            suiteVersion = request.SuiteVersion,
            planId = request.PlanId,
            planVersion = request.PlanVersion,
            status,
            startTime = start.ToString("O", CultureInfo.InvariantCulture),
            endTime = end.ToString("O", CultureInfo.InvariantCulture),
            exitCode,
            effectiveInputs = request.RedactedInputs,
            error = errorType is null ? null : new
            {
                type = errorType,
                source = errorType == "ScriptError" ? "Script" : "Runner",
                message = errorMessage
            }
        };

        object envSnapshot = new
        {
            osVersion = Environment.OSVersion.VersionString,
            runnerVersion = "1.0.0",
            powerShellVersion = "unknown",
            elevated = false
        };

        JsonFile.Write(Path.Combine(runFolder, "manifest.json"), manifestSnapshot);
        JsonFile.Write(Path.Combine(runFolder, "params.json"), request.RedactedInputs);
        JsonFile.Write(Path.Combine(runFolder, "result.json"), result);
        JsonFile.Write(Path.Combine(runFolder, "env.json"), envSnapshot);

        await File.WriteAllTextAsync(Path.Combine(runFolder, "stdout.log"), stdout, Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(runFolder, "stderr.log"), stderr, Encoding.UTF8);

        if (!wroteEvents && request.SecretKeys.Count == 0)
        {
            return;
        }
    }

    private static async Task WriteEventAsync(string runFolder, object entry)
    {
        string path = Path.Combine(runFolder, "events.jsonl");
        string line = JsonSerializer.Serialize(entry, JsonDefaults.Options);
        await File.AppendAllTextAsync(path, line + Environment.NewLine, Encoding.UTF8);
    }
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan? timeout, CancellationToken cancellationToken);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        using Process process = new() { StartInfo = startInfo };
        StringBuilder stdout = new();
        StringBuilder stderr = new();

        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken linkedToken = linkedCts.Token;

        process.Start();

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(linkedToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(linkedToken);

        Task exitTask = process.WaitForExitAsync(linkedToken);
        Task timeoutTask = timeout is null ? Task.Delay(Timeout.InfiniteTimeSpan, linkedToken) : Task.Delay(timeout.Value, linkedToken);

        Task completed = await Task.WhenAny(exitTask, timeoutTask);
        bool timedOut = completed == timeoutTask && !linkedToken.IsCancellationRequested;

        if (timedOut)
        {
            linkedCts.Cancel();
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        }

        string stdoutResult = string.Empty;
        string stderrResult = string.Empty;

        try
        {
            stdoutResult = await stdoutTask;
            stderrResult = await stderrTask;
        }
        catch (OperationCanceledException)
        {
        }

        return new ProcessResult
        {
            ExitCode = timedOut || cancellationToken.IsCancellationRequested ? null : process.ExitCode,
            TimedOut = timedOut,
            Aborted = cancellationToken.IsCancellationRequested,
            Stdout = stdoutResult,
            Stderr = stderrResult
        };
    }
}

public sealed record ProcessResult
{
    public int? ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public bool Aborted { get; init; }
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
}

public static class ArgumentSerializer
{
    public static IReadOnlyList<string> Serialize(string name, string type, object value)
    {
        List<string> args = new();
        args.Add($"-{name}");

        switch (type)
        {
            case "boolean":
                args.Add((bool)value ? "$true" : "$false");
                break;
            case "boolean[]":
                args.AddRange(((IEnumerable<bool>)value).Select(v => v ? "$true" : "$false"));
                break;
            case "int":
                args.Add(((int)value).ToString(CultureInfo.InvariantCulture));
                break;
            case "double":
                args.Add(((double)value).ToString(CultureInfo.InvariantCulture));
                break;
            case "int[]":
                args.AddRange(((IEnumerable<int>)value).Select(v => v.ToString(CultureInfo.InvariantCulture)));
                break;
            case "double[]":
                args.AddRange(((IEnumerable<double>)value).Select(v => v.ToString(CultureInfo.InvariantCulture)));
                break;
            case "enum":
            case "string":
            case "path":
            case "file":
            case "folder":
                args.Add(value.ToString() ?? string.Empty);
                break;
            case "enum[]":
            case "string[]":
            case "path[]":
            case "file[]":
            case "folder[]":
                args.AddRange(((IEnumerable<string>)value).Select(v => v));
                break;
            default:
                args.Add(value.ToString() ?? string.Empty);
                break;
        }

        return args;
    }
}

public static class RunIdGenerator
{
    public static string CreateRunId(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }
}

public static class Redactor
{
    public static string Redact(string content, IReadOnlyCollection<string> secretKeys, IReadOnlyDictionary<string, object> inputs)
    {
        if (secretKeys.Count == 0)
        {
            return content;
        }

        string redacted = content;
        foreach (string key in secretKeys)
        {
            if (inputs.TryGetValue(key, out object? value) && value is not null)
            {
                redacted = redacted.Replace(value.ToString() ?? string.Empty, "***", StringComparison.Ordinal);
            }
        }

        return redacted;
    }
}
