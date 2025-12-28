using System.Diagnostics;
using System.Text;
using PcTest.Contracts;
using PcTest.Contracts.Models;

namespace PcTest.Runner;

public sealed class TestCaseRunner : ITestCaseRunner
{
    public async Task<TestCaseRunResult> RunAsync(RunnerRequest request, CancellationToken cancellationToken = default)
    {
        var runId = CreateRunId();
        var runFolder = Path.Combine(request.RunsRoot, runId);
        Directory.CreateDirectory(runFolder);

        var startTime = DateTimeOffset.UtcNow;
        var result = new TestCaseRunResult
        {
            RunId = runId,
            RunFolder = runFolder,
            StartTime = startTime
        };

        try
        {
            var workingDir = ResolveWorkingDir(runFolder, request.WorkingDir);
            var validationError = ValidateInputs(request.Manifest, request.EffectiveInputs, workingDir, runFolder);
            if (validationError is not null)
            {
                var endTime = DateTimeOffset.UtcNow;
                result.Result = CreateRunnerErrorResult(request, runId, validationError, startTime, endTime);
                WriteCaseArtifacts(request, runFolder, result.Result, request.RedactedInputs, request.RedactedEnvironment, startTime, endTime, validationError, skipScriptOutput: true);
                return result;
            }

            var processInfo = BuildProcessStartInfo(request, workingDir, out var secretWarnings);
            await WriteCaseArtifacts(request, runFolder, null, request.RedactedInputs, request.RedactedEnvironment, startTime, null, null, skipScriptOutput: true);
            RunnerEventWriter.AppendSecretWarnings(runFolder, secretWarnings, request.NodeId);

            using var process = new Process { StartInfo = processInfo };
            process.Start();

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);

            var timeout = request.Manifest.TimeoutSec.HasValue && request.Manifest.TimeoutSec.Value > 0
                ? TimeSpan.FromSeconds(request.Manifest.TimeoutSec.Value)
                : Timeout.InfiniteTimeSpan;

            if (timeout != Timeout.InfiniteTimeSpan)
            {
                using var timeoutCts = new CancellationTokenSource(timeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
                try
                {
                    await process.WaitForExitAsync(linked.Token);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        KillProcessTree(process);
                    }

                    result.Result = CreateTimeoutResult(request, runId, startTime);
                    await WriteCaseArtifacts(request, runFolder, result.Result, request.RedactedInputs, request.RedactedEnvironment, startTime, DateTimeOffset.UtcNow, null, stdout, stderr);
                    return result;
                }
            }
            else
            {
                await process.WaitForExitAsync(cancellationToken);
            }

            var endTime = DateTimeOffset.UtcNow;
            var baseResult = CreateBaseResult(request, runId, startTime, endTime);
            result.Result = RunnerResultMapper.MapExitCode(baseResult, process.ExitCode);
            await WriteCaseArtifacts(request, runFolder, result.Result, request.RedactedInputs, request.RedactedEnvironment, startTime, endTime, null, stdout, stderr);
        }
        catch (Exception ex)
        {
            var endTime = DateTimeOffset.UtcNow;
            result.Result = CreateRunnerErrorResult(request, runId, ex.Message, startTime, endTime, ex);
            await WriteCaseArtifacts(request, runFolder, result.Result, request.RedactedInputs, request.RedactedEnvironment, startTime, endTime, ex.Message, string.Empty, string.Empty);
        }

        return result;
    }

    private static string CreateRunId()
    {
        return $"R-{Guid.NewGuid():N}";
    }

    private static string ResolveWorkingDir(string runFolder, string? workingDir)
    {
        var baseDir = runFolder;
        if (string.IsNullOrWhiteSpace(workingDir))
        {
            return baseDir;
        }

        var combined = PathUtil.CombineAndNormalize(baseDir, workingDir);
        var resolved = PathUtil.ResolveLinkTargetPath(combined);
        if (!PathUtil.IsContained(baseDir, resolved))
        {
            throw new InvalidOperationException("Working directory escapes run folder.");
        }

        Directory.CreateDirectory(resolved);
        return resolved;
    }

    private static string? ValidateInputs(TestCaseManifest manifest, Dictionary<string, object?> inputs, string workingDir, string runFolder)
    {
        if (manifest.Parameters is null)
        {
            return null;
        }

        foreach (var parameter in manifest.Parameters)
        {
            if (!inputs.TryGetValue(parameter.Name, out var value) || value is null)
            {
                continue;
            }

            var type = parameter.Type.ToLowerInvariant();
            var values = type.EndsWith("[]") ? (value as IEnumerable<object?> ?? Array.Empty<object?>()) : new[] { value };
            if (type.EndsWith("[]"))
            {
                type = type[..^2];
            }

            if (type is "file" or "folder" or "path")
            {
                foreach (var item in values)
                {
                    if (item is not string path)
                    {
                        continue;
                    }

                    var resolved = Path.IsPathRooted(path)
                        ? PathUtil.NormalizePath(path)
                        : PathUtil.CombineAndNormalize(workingDir, path);

                    if (type == "path")
                    {
                        continue;
                    }

                    if (type == "file" && !File.Exists(resolved))
                    {
                        return $"File not found: {resolved}";
                    }

                    if (type == "folder" && !Directory.Exists(resolved))
                    {
                        return $"Folder not found: {resolved}";
                    }

                    if (!PathUtil.IsContained(runFolder, resolved) && !Path.IsPathRooted(path))
                    {
                        return $"Path escapes run folder: {resolved}";
                    }
                }
            }
        }

        return null;
    }

    private static ProcessStartInfo BuildProcessStartInfo(RunnerRequest request, string workingDir, out List<string> secretWarnings)
    {
        var pwsh = PathUtil.GetPlatformSensitiveExe("pwsh");
        var startInfo = new ProcessStartInfo
        {
            FileName = pwsh,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(Path.Combine(request.TestCasePath, "run.ps1"));
        secretWarnings = new List<string>();
        var arguments = PowerShellArgumentBuilder.BuildArgumentList(request.EffectiveInputs);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var input in request.EffectiveInputs.Keys)
        {
            if (request.SecretInputs.Contains(input))
            {
                secretWarnings.Add(input);
            }
        }

        foreach (var (key, value) in request.EffectiveEnvironment)
        {
            startInfo.Environment[key] = value;
        }

        return startInfo;
    }

    private static TestCaseResult CreateBaseResult(RunnerRequest request, string runId, DateTimeOffset start, DateTimeOffset end)
    {
        return new TestCaseResult
        {
            NodeId = request.NodeId,
            TestId = request.Manifest.Id,
            TestVersion = request.Manifest.Version,
            SuiteId = request.SuiteId,
            SuiteVersion = request.SuiteVersion,
            PlanId = request.PlanId,
            PlanVersion = request.PlanVersion,
            Status = RunStatus.Error,
            StartTime = start.ToString("O"),
            EndTime = end.ToString("O"),
            EffectiveInputs = request.RedactedInputs
        };
    }

    private static TestCaseResult CreateTimeoutResult(RunnerRequest request, string runId, DateTimeOffset start)
    {
        var end = DateTimeOffset.UtcNow;
        var result = CreateBaseResult(request, runId, start, end);
        result.Status = RunStatus.Timeout;
        result.Error = new RunError
        {
            Type = "Timeout",
            Source = "Runner",
            Message = "Execution timed out."
        };
        return result;
    }

    private static TestCaseResult CreateRunnerErrorResult(RunnerRequest request, string runId, string message, DateTimeOffset start, DateTimeOffset end, Exception? ex = null)
    {
        var result = CreateBaseResult(request, runId, start, end);
        result.Status = RunStatus.Error;
        result.Error = new RunError
        {
            Type = "RunnerError",
            Source = "Runner",
            Message = message,
            Stack = ex?.ToString()
        };
        return result;
    }

    private static async Task WriteCaseArtifacts(RunnerRequest request, string runFolder, TestCaseResult? result, Dictionary<string, object?> redactedInputs, Dictionary<string, string> redactedEnv, DateTimeOffset start, DateTimeOffset? end, string? runnerError, string? stdout = null, string? stderr = null, bool skipScriptOutput = false)
    {
        var manifestSnapshot = new
        {
            sourceManifest = request.Manifest,
            resolvedRef = request.ResolvedRef,
            resolvedIdentity = new { id = request.Manifest.Id, version = request.Manifest.Version },
            effectiveEnvironment = redactedEnv,
            effectiveInputs = redactedInputs,
            resolvedAt = start.ToString("O"),
            engineVersion = request.EngineVersion
        };

        JsonUtil.WriteJsonFile(Path.Combine(runFolder, "manifest.json"), manifestSnapshot);
        JsonUtil.WriteJsonFile(Path.Combine(runFolder, "params.json"), redactedInputs);
        JsonUtil.WriteJsonFile(Path.Combine(runFolder, "env.json"), new
        {
            osVersion = Environment.OSVersion.VersionString,
            runnerVersion = request.EngineVersion,
            powerShellVersion = "unknown",
            elevation = "unknown"
        });

        if (result is not null)
        {
            JsonUtil.WriteJsonFile(Path.Combine(runFolder, "result.json"), result);
        }

        var scrubbedStdout = skipScriptOutput ? string.Empty : Redact(stdout ?? string.Empty, request.SecretInputs, request.EffectiveInputs);
        var scrubbedStderr = skipScriptOutput ? string.Empty : Redact(stderr ?? string.Empty, request.SecretInputs, request.EffectiveInputs);
        await File.WriteAllTextAsync(Path.Combine(runFolder, "stdout.log"), scrubbedStdout, new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(runFolder, "stderr.log"), scrubbedStderr, new UTF8Encoding(false));

        if (!string.IsNullOrWhiteSpace(runnerError))
        {
            await File.WriteAllTextAsync(Path.Combine(runFolder, "events.jsonl"), runnerError + Environment.NewLine, new UTF8Encoding(false));
        }
    }

    private static string Redact(string content, HashSet<string> secretInputs, Dictionary<string, object?> effectiveInputs)
    {
        var result = content;
        foreach (var secret in secretInputs)
        {
            if (!effectiveInputs.TryGetValue(secret, out var value) || value is null)
            {
                continue;
            }

            if (value is string stringValue)
            {
                result = result.Replace(stringValue, "***", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (value is Array array)
            {
                foreach (var item in array)
                {
                    if (item is null)
                    {
                        continue;
                    }

                    result = result.Replace(item.ToString() ?? string.Empty, "***", StringComparison.OrdinalIgnoreCase);
                }

                continue;
            }

            result = result.Replace(value.ToString() ?? string.Empty, "***", StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // best effort
        }
    }
}

public sealed class TestCaseRunResult
{
    public required string RunId { get; init; }
    public required string RunFolder { get; init; }
    public required DateTimeOffset StartTime { get; init; }
    public TestCaseResult? Result { get; set; }
}
