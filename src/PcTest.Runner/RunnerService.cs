using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Runner;

public sealed class RunnerService
{
    public async Task<RunnerResult> RunTestCaseAsync(RunnerRequest request, CancellationToken cancellationToken)
    {
        string runId = GenerateRunId(request.RunsRoot);
        string runFolder = Path.Combine(request.RunsRoot, runId);
        Directory.CreateDirectory(runFolder);

        DateTimeOffset start = DateTimeOffset.UtcNow;

        string workingDir = ResolveWorkingDirectory(runFolder, request.WorkingDir, out ValidationError? workingDirError);
        if (workingDirError is not null)
        {
            RunnerResult errorResult = new()
            {
                RunId = runId,
                Status = RunStatus.Error,
                StartTime = start,
                EndTime = DateTimeOffset.UtcNow,
                ErrorType = "RunnerError",
                ErrorMessage = workingDirError.Message
            };
            await WriteFailureArtifactsAsync(request, runFolder, errorResult, cancellationToken).ConfigureAwait(false);
            return errorResult;
        }

        if (!TryValidateFileInputs(request, workingDir, out ValidationError? inputError))
        {
            RunnerResult errorResult = new()
            {
                RunId = runId,
                Status = RunStatus.Error,
                StartTime = start,
                EndTime = DateTimeOffset.UtcNow,
                ErrorType = "RunnerError",
                ErrorMessage = inputError?.Message
            };
            await WriteFailureArtifactsAsync(request, runFolder, errorResult, cancellationToken).ConfigureAwait(false);
            return errorResult;
        }

        string scriptPath = Path.Combine(request.TestCasePath, "run.ps1");
        if (!File.Exists(scriptPath))
        {
            RunnerResult errorResult = new()
            {
                RunId = runId,
                Status = RunStatus.Error,
                StartTime = start,
                EndTime = DateTimeOffset.UtcNow,
                ErrorType = "RunnerError",
                ErrorMessage = "run.ps1 not found."
            };
            await WriteFailureArtifactsAsync(request, runFolder, errorResult, cancellationToken).ConfigureAwait(false);
            return errorResult;
        }

        List<string> arguments = BuildArguments(request.EffectiveInputs, request.Manifest.Parameters);
        bool secretOnCommandLine = request.SecretInputs.Any(name => request.EffectiveInputs.ContainsKey(name));

        ProcessStartInfo startInfo = new()
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDir
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (string arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (KeyValuePair<string, string> pair in request.EffectiveEnvironment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        StringBuilder stdoutBuffer = new();
        StringBuilder stderrBuffer = new();

        int? exitCode = null;
        RunStatus status = RunStatus.Error;
        string? errorType = null;
        string? errorMessage = null;

        try
        {
            using Process process = new();
            process.StartInfo = startInfo;
            process.Start();

            Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            if (request.TimeoutSec is int timeoutSec && timeoutSec > 0)
            {
                using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task delayTask = Task.Delay(TimeSpan.FromSeconds(timeoutSec), timeoutCts.Token);
                Task finishedTask = await Task.WhenAny(process.WaitForExitAsync(cancellationToken), delayTask).ConfigureAwait(false);
                if (finishedTask == delayTask)
                {
                    TryKillProcessTree(process);
                    status = RunStatus.Timeout;
                    errorType = "Timeout";
                }
                else
                {
                    timeoutCts.Cancel();
                    exitCode = process.ExitCode;
                }
            }
            else
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                exitCode = process.ExitCode;
            }

            stdoutBuffer.Append(await stdOutTask.ConfigureAwait(false));
            stderrBuffer.Append(await stdErrTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            status = RunStatus.Aborted;
            errorType = "Aborted";
        }
        catch (Exception ex)
        {
            status = RunStatus.Error;
            errorType = "RunnerError";
            errorMessage = ex.Message;
        }

        if (exitCode.HasValue)
        {
            if (exitCode.Value == 0)
            {
                status = RunStatus.Passed;
            }
            else if (exitCode.Value == 1)
            {
                status = RunStatus.Failed;
            }
            else
            {
                status = RunStatus.Error;
                errorType = "ScriptError";
            }
        }

        RunnerResult result = new()
        {
            RunId = runId,
            Status = status,
            StartTime = start,
            EndTime = DateTimeOffset.UtcNow,
            ExitCode = exitCode,
            ErrorType = errorType,
            ErrorMessage = errorMessage
        };

        await WriteRunArtifactsAsync(request, runFolder, result, stdoutBuffer.ToString(), stderrBuffer.ToString(), secretOnCommandLine, cancellationToken)
            .ConfigureAwait(false);

        return result;
    }

    private static string GenerateRunId(string runsRoot)
    {
        string id;
        do
        {
            id = $"R-{Guid.NewGuid():N}";
        }
        while (Directory.Exists(Path.Combine(runsRoot, id)));
        return id;
    }

    private static string ResolveWorkingDirectory(string runFolder, string? workingDir, out ValidationError? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(workingDir))
        {
            return runFolder;
        }

        string combined = Path.GetFullPath(Path.Combine(runFolder, workingDir));
        string resolved = ResolveLinkTargetIfExists(combined);
        if (!IsContained(runFolder, resolved))
        {
            error = new ValidationError("Runner.WorkingDir.Invalid", "Working directory escapes run folder.");
            return runFolder;
        }

        Directory.CreateDirectory(resolved);
        return resolved;
    }

    private static bool TryValidateFileInputs(RunnerRequest request, string workingDir, out ValidationError? error)
    {
        error = null;
        if (request.Manifest.Parameters is null)
        {
            return true;
        }

        foreach (ParameterDefinition param in request.Manifest.Parameters)
        {
            if (!request.EffectiveInputs.TryGetValue(param.Name, out object? value) || value is null)
            {
                continue;
            }

            if (!ParameterTypeParser.TryParse(param.Type, out ParameterType parameterType))
            {
                continue;
            }

            if (parameterType is ParameterType.File or ParameterType.Folder or ParameterType.Path)
            {
                string? pathValue = value.ToString();
                if (string.IsNullOrWhiteSpace(pathValue))
                {
                    continue;
                }

                string resolvedPath = Path.GetFullPath(Path.Combine(workingDir, pathValue));
                if (!IsContained(workingDir, resolvedPath))
                {
                    error = new ValidationError("Runner.Input.Path.Invalid", $"Input {param.Name} escapes working directory.");
                    return false;
                }

                if (parameterType == ParameterType.File && !File.Exists(resolvedPath))
                {
                    error = new ValidationError("Runner.Input.File.Missing", $"Input file {param.Name} not found.");
                    return false;
                }

                if (parameterType == ParameterType.Folder && !Directory.Exists(resolvedPath))
                {
                    error = new ValidationError("Runner.Input.Folder.Missing", $"Input folder {param.Name} not found.");
                    return false;
                }

                request.EffectiveInputs[param.Name] = resolvedPath;
            }
        }

        return true;
    }

    private static List<string> BuildArguments(Dictionary<string, object?> inputs, ParameterDefinition[]? parameters)
    {
        List<string> args = new();
        if (parameters is null)
        {
            return args;
        }

        foreach (ParameterDefinition param in parameters)
        {
            if (!inputs.TryGetValue(param.Name, out object? value) || value is null)
            {
                continue;
            }

            args.Add($"-{param.Name}");
            if (value is IEnumerable<string> stringValues && value is not string)
            {
                foreach (string item in stringValues)
                {
                    args.Add(item);
                }

                continue;
            }

            if (value is IEnumerable<int> intValues)
            {
                foreach (int item in intValues)
                {
                    args.Add(item.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                continue;
            }

            if (value is IEnumerable<double> doubleValues)
            {
                foreach (double item in doubleValues)
                {
                    args.Add(item.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                continue;
            }

            if (value is IEnumerable<bool> boolValues && value is not bool)
            {
                foreach (bool item in boolValues)
                {
                    args.Add(item ? "$true" : "$false");
                }

                continue;
            }

            if (value is bool boolValue)
            {
                args.Add(boolValue ? "$true" : "$false");
                continue;
            }

            args.Add(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return args;
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignore
        }
    }

    private static async Task WriteFailureArtifactsAsync(RunnerRequest request, string runFolder, RunnerResult result, CancellationToken cancellationToken)
    {
        await WriteRunArtifactsAsync(request, runFolder, result, string.Empty, string.Empty, secretOnCommandLine: false, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteRunArtifactsAsync(
        RunnerRequest request,
        string runFolder,
        RunnerResult result,
        string stdout,
        string stderr,
        bool secretOnCommandLine,
        CancellationToken cancellationToken)
    {
        ManifestSnapshot snapshot = new()
        {
            SourceManifest = request.Manifest,
            ResolvedRef = request.ResolvedRef,
            ResolvedIdentity = request.Identity,
            EffectiveEnvironment = request.EffectiveEnvironment,
            EffectiveInputs = RedactInputs(request.EffectiveInputsJson, request.SecretInputs),
            InputTemplates = RedactInputs(request.InputTemplates, request.SecretInputs),
            ResolvedAt = DateTimeOffset.UtcNow.ToString("O"),
            EngineVersion = typeof(RunnerService).Assembly.GetName().Version?.ToString()
        };

        await JsonHelpers.WriteJsonFileAsync(Path.Combine(runFolder, "manifest.json"), snapshot, cancellationToken).ConfigureAwait(false);
        await JsonHelpers.WriteJsonFileAsync(Path.Combine(runFolder, "params.json"), RedactInputs(request.EffectiveInputsJson, request.SecretInputs), cancellationToken).ConfigureAwait(false);

        string stdoutRedacted = RedactText(stdout, request.SecretInputs, request.EffectiveInputs);
        string stderrRedacted = RedactText(stderr, request.SecretInputs, request.EffectiveInputs);

        await File.WriteAllTextAsync(Path.Combine(runFolder, "stdout.log"), stdoutRedacted, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runFolder, "stderr.log"), stderrRedacted, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

        List<object> events = new();
        if (secretOnCommandLine)
        {
            foreach (string name in request.SecretInputs)
            {
                if (request.EffectiveInputs.ContainsKey(name))
                {
                    events.Add(new { code = "EnvRef.SecretOnCommandLine", parameter = name, nodeId = request.NodeId, message = "Secret input passed via command line." });
                }
            }
        }

        if (events.Count > 0)
        {
            string eventsPath = Path.Combine(runFolder, "events.jsonl");
            await using FileStream stream = new(eventsPath, FileMode.Create, FileAccess.Write, FileShare.None);
            foreach (object evt in events)
            {
                string json = JsonSerializer.Serialize(evt, JsonHelpers.SerializerOptions);
                byte[] line = Encoding.UTF8.GetBytes(json + "\n");
                await stream.WriteAsync(line, cancellationToken).ConfigureAwait(false);
            }
        }

        await JsonHelpers.WriteJsonFileAsync(Path.Combine(runFolder, "env.json"), await BuildEnvSnapshotAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

        TestCaseResult resultPayload = new()
        {
            SchemaVersion = "1.5.0",
            RunType = "TestCase",
            NodeId = request.NodeId,
            TestId = request.Identity.Id,
            TestVersion = request.Identity.Version,
            SuiteId = request.SuiteId,
            SuiteVersion = request.SuiteVersion,
            PlanId = request.PlanId,
            PlanVersion = request.PlanVersion,
            Status = result.Status.ToString(),
            StartTime = result.StartTime.ToString("O"),
            EndTime = result.EndTime.ToString("O"),
            ExitCode = result.ExitCode,
            EffectiveInputs = RedactInputs(request.EffectiveInputsJson, request.SecretInputs),
            Error = BuildError(result)
        };

        await JsonHelpers.WriteJsonFileAsync(Path.Combine(runFolder, "result.json"), resultPayload, cancellationToken).ConfigureAwait(false);
    }

    private static ErrorDetail? BuildError(RunnerResult result)
    {
        if (string.IsNullOrEmpty(result.ErrorType))
        {
            return null;
        }

        return new ErrorDetail
        {
            Type = result.ErrorType,
            Source = result.ErrorType == "ScriptError" ? "Script" : "Runner",
            Message = result.ErrorMessage
        };
    }

    private static Dictionary<string, JsonElement> RedactInputs(Dictionary<string, JsonElement> inputs, HashSet<string> secretInputs)
    {
        Dictionary<string, JsonElement> redacted = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonElement> pair in inputs)
        {
            if (secretInputs.Contains(pair.Key))
            {
                redacted[pair.Key] = JsonSerializer.SerializeToElement("***");
            }
            else
            {
                redacted[pair.Key] = pair.Value;
            }
        }

        return redacted;
    }

    private static string RedactText(string text, HashSet<string> secretInputs, Dictionary<string, object?> values)
    {
        string redacted = text;
        foreach (string key in secretInputs)
        {
            if (values.TryGetValue(key, out object? value) && value is not null)
            {
                string valueText = value.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(valueText))
                {
                    redacted = redacted.Replace(valueText, "***", StringComparison.Ordinal);
                }
            }
        }

        return redacted;
    }

    private static async Task<object> BuildEnvSnapshotAsync(CancellationToken cancellationToken)
    {
        string psVersion = await GetPowerShellVersionAsync(cancellationToken).ConfigureAwait(false);
        return new
        {
            osVersion = Environment.OSVersion.VersionString,
            runnerVersion = typeof(RunnerService).Assembly.GetName().Version?.ToString() ?? "unknown",
            powerShellVersion = psVersion,
            isElevated = IsElevated()
        };
    }

    private static async Task<string> GetPowerShellVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "pwsh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add("$PSVersionTable.PSVersion.ToString()");

            using Process process = new();
            process.StartInfo = startInfo;
            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return output.Trim();
        }
        catch
        {
            return "unknown";
        }
    }

    private static bool IsElevated()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Environment.UserName == "root";
        }

        try
        {
            using System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            System.Security.Principal.WindowsPrincipal principal = new(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsContained(string rootPath, string candidatePath)
    {
        string root = EnsureTrailingSeparator(Path.GetFullPath(rootPath));
        string candidate = EnsureTrailingSeparator(Path.GetFullPath(candidatePath));
        StringComparison comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return candidate.StartsWith(root, comparison);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }

    private static string ResolveLinkTargetIfExists(string path)
    {
        if (!Directory.Exists(path))
        {
            return path;
        }

        DirectoryInfo info = new(path);
        DirectoryInfo? resolved = info.ResolveLinkTarget(true);
        return resolved?.FullName ?? info.FullName;
    }
}
