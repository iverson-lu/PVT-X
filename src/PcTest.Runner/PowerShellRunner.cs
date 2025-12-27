using System.Diagnostics;
using System.Text;
using PcTest.Contracts;

namespace PcTest.Runner;

public sealed class PowerShellRunner : ICaseRunner
{
    public RunCaseResult Run(RunCaseRequest request)
    {
        var runId = GenerateRunId();
        var runFolder = CreateRunFolder(request.RunsRoot, runId);
        var start = DateTimeOffset.UtcNow;
        var eventsPath = Path.Combine(runFolder, "events.jsonl");
        var stdoutPath = Path.Combine(runFolder, "stdout.log");
        var stderrPath = Path.Combine(runFolder, "stderr.log");

        var snapshot = new
        {
            schemaVersion = SchemaVersions.Current,
            sourceManifest = request.Manifest,
            resolvedRef = request.ResolvedRef,
            resolvedIdentity = new { id = request.Manifest.Id, version = request.Manifest.Version },
            effectiveEnvironment = request.EffectiveEnvironment,
            effectiveInputs = RunnerUtilities.RedactInputs(request.EffectiveInputs, request.SecretInputs),
            inputTemplates = request.InputTemplates,
            resolvedAt = start.ToString("O"),
            engineVersion = "1.0.0"
        };

        JsonUtilities.WriteJson(Path.Combine(runFolder, "manifest.json"), snapshot);
        JsonUtilities.WriteJson(Path.Combine(runFolder, "params.json"), RunnerUtilities.RedactInputs(request.EffectiveInputs, request.SecretInputs));
        JsonUtilities.WriteJson(Path.Combine(runFolder, "env.json"), new
        {
            osVersion = Environment.OSVersion.VersionString,
            runnerVersion = "1.0.0",
            powerShellVersion = "unknown",
            isElevated = false
        });

        var (workingDir, workingDirError) = ResolveWorkingDir(runFolder, request.WorkingDir);
        if (workingDirError is not null)
        {
            WriteLogs(stdoutPath, stderrPath, string.Empty, string.Empty, request.SecretInputs, request.EffectiveInputs);
            WriteResult(runFolder, request, start, DateTimeOffset.UtcNow, null, "Error", "RunnerError", workingDirError, request.EffectiveInputs);
            return new RunCaseResult
            {
                RunId = runId,
                RunFolder = runFolder,
                Status = "Error",
                StartTime = start,
                EndTime = DateTimeOffset.UtcNow,
                ErrorType = "RunnerError",
                ErrorMessage = workingDirError
            };
        }

        var secretArgs = request.SecretInputs.Count > 0;
        if (secretArgs)
        {
            JsonUtilities.WriteJsonLine(eventsPath, new RunnerEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = "Warning",
                Code = "EnvRef.SecretOnCommandLine",
                Message = "Secret input passed via command line.",
                Data = new { inputs = request.SecretInputs.ToArray(), nodeId = request.NodeId }
            });
        }

        var processStart = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        processStart.ArgumentList.Add(request.ResolvedRef);
        RunnerUtilities.AppendArguments(processStart, request.EffectiveInputs);

        foreach (var kvp in request.EffectiveEnvironment)
        {
            processStart.Environment[kvp.Key] = kvp.Value;
        }

        using var process = new Process { StartInfo = processStart };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        process.OutputDataReceived += (_, args) => { if (args.Data is not null) stdoutBuilder.AppendLine(args.Data); };
        process.ErrorDataReceived += (_, args) => { if (args.Data is not null) stderrBuilder.AppendLine(args.Data); };

        string status;
        string? errorType = null;
        int? exitCode = null;
        string? errorMessage = null;
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start pwsh.");
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var timeout = TimeSpan.FromSeconds(request.Manifest.TimeoutSec ?? 0);
            if (timeout == TimeSpan.Zero)
            {
                process.WaitForExit();
            }
            else
            {
                if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                {
                    process.Kill(true);
                    status = "Timeout";
                    errorType = "Timeout";
                    errorMessage = "Process timed out.";
                    WriteLogs(stdoutPath, stderrPath, stdoutBuilder.ToString(), stderrBuilder.ToString(), request.SecretInputs, request.EffectiveInputs);
                    WriteResult(runFolder, request, start, DateTimeOffset.UtcNow, null, status, errorType, errorMessage, request.EffectiveInputs);
                    return new RunCaseResult
                    {
                        RunId = runId,
                        RunFolder = runFolder,
                        Status = status,
                        StartTime = start,
                        EndTime = DateTimeOffset.UtcNow,
                        ExitCode = null,
                        ErrorType = errorType,
                        ErrorMessage = errorMessage
                    };
                }
            }

            exitCode = process.ExitCode;
            var mapping = RunnerUtilities.MapExitCode(exitCode.Value);
            status = mapping.status;
            errorType = mapping.errorType;
            if (status == "Error")
            {
                errorMessage = $"Script exited with code {exitCode}.";
            }
        }
        catch (Exception ex)
        {
            status = "Error";
            errorType = "RunnerError";
            errorMessage = ex.Message;
        }

        WriteLogs(stdoutPath, stderrPath, stdoutBuilder.ToString(), stderrBuilder.ToString(), request.SecretInputs, request.EffectiveInputs);
        var end = DateTimeOffset.UtcNow;
        WriteResult(runFolder, request, start, end, exitCode, status, errorType, errorMessage, request.EffectiveInputs);
        return new RunCaseResult
        {
            RunId = runId,
            RunFolder = runFolder,
            Status = status,
            StartTime = start,
            EndTime = end,
            ExitCode = exitCode,
            ErrorType = errorType,
            ErrorMessage = errorMessage
        };
    }

    private static string GenerateRunId()
    {
        return $"R-{Guid.NewGuid():N}";
    }

    private static string CreateRunFolder(string runsRoot, string runId)
    {
        Directory.CreateDirectory(runsRoot);
        var folder = Path.Combine(runsRoot, runId);
        if (Directory.Exists(folder))
        {
            folder = Path.Combine(runsRoot, $"{runId}-{Guid.NewGuid():N}");
        }
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static (string path, string? error) ResolveWorkingDir(string runFolder, string? workingDir)
    {
        var error = RunnerUtilities.ValidateWorkingDir(runFolder, workingDir);
        if (error is not null)
        {
            return (runFolder, error);
        }
        var target = runFolder;
        if (!string.IsNullOrWhiteSpace(workingDir))
        {
            target = Path.GetFullPath(Path.Combine(runFolder, workingDir));
        }
        Directory.CreateDirectory(target);
        return (target, null);
    }

    private static void WriteResult(string runFolder, RunCaseRequest request, DateTimeOffset start, DateTimeOffset end, int? exitCode, string status, string? errorType, string? errorMessage, Dictionary<string, object?> inputs)
    {
        var result = new Dictionary<string, object?>
        {
            ["schemaVersion"] = SchemaVersions.Current,
            ["runType"] = "TestCase",
            ["status"] = status,
            ["startTime"] = start.ToString("O"),
            ["endTime"] = end.ToString("O"),
            ["testId"] = request.Manifest.Id,
            ["testVersion"] = request.Manifest.Version,
            ["effectiveInputs"] = RunnerUtilities.RedactInputs(inputs, request.SecretInputs)
        };
        if (!string.IsNullOrWhiteSpace(request.NodeId))
        {
            result["nodeId"] = request.NodeId;
        }
        if (request.SuiteIdentity is not null)
        {
            result["suiteId"] = request.SuiteIdentity.Id;
            result["suiteVersion"] = request.SuiteIdentity.Version;
        }
        if (request.PlanIdentity is not null)
        {
            result["planId"] = request.PlanIdentity.Id;
            result["planVersion"] = request.PlanIdentity.Version;
        }
        if (exitCode.HasValue)
        {
            result["exitCode"] = exitCode.Value;
        }
        if (errorType is not null)
        {
            result["error"] = new { type = errorType, source = errorType == "ScriptError" ? "Script" : "Runner", message = errorMessage };
        }
        JsonUtilities.WriteJson(Path.Combine(runFolder, "result.json"), result);
    }

    private static void WriteLogs(string stdoutPath, string stderrPath, string stdout, string stderr, HashSet<string> secrets, Dictionary<string, object?> inputs)
    {
        var redactedStdout = RunnerUtilities.RedactText(stdout, secrets, inputs);
        var redactedStderr = RunnerUtilities.RedactText(stderr, secrets, inputs);
        File.WriteAllText(stdoutPath, redactedStdout, new UTF8Encoding(false));
        File.WriteAllText(stderrPath, redactedStderr, new UTF8Encoding(false));
    }
}
