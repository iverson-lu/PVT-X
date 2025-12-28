using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Text;
using PcTest.Contracts;

namespace PcTest.Runner;

public sealed class RunnerService
{
    public string PwshPath { get; }

    public RunnerService(string? pwshPath = null)
    {
        PwshPath = string.IsNullOrWhiteSpace(pwshPath) ? "pwsh" : pwshPath;
    }

    public TestCaseExecutionResult RunTestCase(TestCaseExecutionRequest request)
    {
        var runFolder = Path.Combine(request.RunsRoot, request.RunId);
        Directory.CreateDirectory(runFolder);

        var startTime = DateTimeOffset.UtcNow;
        var manifestSnapshot = BuildManifestSnapshot(request, startTime);
        JsonUtils.WriteJsonFile(Path.Combine(runFolder, "manifest.json"), manifestSnapshot);
        JsonUtils.WriteJsonFile(Path.Combine(runFolder, "params.json"), new SortedDictionary<string, object?>(request.RedactedInputs, StringComparer.OrdinalIgnoreCase));

        var envSnapshot = BuildEnvSnapshot();
        JsonUtils.WriteJsonFile(Path.Combine(runFolder, "env.json"), envSnapshot);

        var workingDir = ResolveWorkingDir(request, runFolder);
        var validationError = ValidateInputs(request, workingDir, runFolder);
        if (validationError is not null)
        {
            var failureResult = BuildResult(request, startTime, DateTimeOffset.UtcNow, null, "Error", "RunnerError", validationError);
            JsonUtils.WriteJsonFile(Path.Combine(runFolder, "result.json"), failureResult);
            return new TestCaseExecutionResult
            {
                RunId = request.RunId,
                RunFolder = runFolder,
                Status = "Error",
                StartTime = startTime,
                EndTime = DateTimeOffset.UtcNow,
                ExitCode = null,
                ErrorType = "RunnerError",
                ErrorMessage = validationError
            };
        }

        var stdoutPath = Path.Combine(runFolder, "stdout.txt");
        var stderrPath = Path.Combine(runFolder, "stderr.txt");
        var eventsPath = Path.Combine(runFolder, "events.jsonl");
        var warnings = new List<object>();
        if (request.SecretInputs.Count > 0)
        {
            foreach (var secret in request.SecretInputs)
            {
                warnings.Add(new Dictionary<string, object?>
                {
                    ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["code"] = "EnvRef.SecretOnCommandLine",
                    ["message"] = "Secret input passed on command line.",
                    ["parameter"] = secret,
                    ["nodeId"] = request.NodeId
                });
            }
        }

        if (warnings.Count > 0)
        {
            JsonUtils.WriteJsonLines(eventsPath, warnings);
        }

        int? exitCode = null;
        string status;
        string? errorType = null;
        string? errorMessage = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = PwshPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = workingDir
            };
            psi.ArgumentList.Add("-NoLogo");
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(request.TestCasePath) ?? string.Empty, "run.ps1"));
            AddArguments(psi, request.EffectiveInputs);

            foreach (var (key, value) in request.EffectiveEnvironment)
            {
                psi.Environment[key] = value;
            }

            using var process = new Process { StartInfo = psi };
            process.Start();

            var timeout = request.TestCase.TimeoutSec.HasValue
                ? TimeSpan.FromSeconds(request.TestCase.TimeoutSec.Value)
                : Timeout.InfiniteTimeSpan;

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var exited = process.WaitForExit(timeout == Timeout.InfiniteTimeSpan ? -1 : (int)timeout.TotalMilliseconds);
            if (!exited)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // ignore
                }

                status = "Timeout";
                errorType = "Timeout";
                errorMessage = "Process exceeded timeout.";
            }
            else
            {
                exitCode = process.ExitCode;
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
                    errorMessage = $"Script exited with code {exitCode}.";
                }
            }

            var stdout = stdoutTask.Result;
            var stderr = stderrTask.Result;
            if (request.SecretInputs.Count > 0)
            {
                (stdout, stderr) = RedactOutput(stdout, stderr, request);
            }

            File.WriteAllText(stdoutPath, stdout, new UTF8Encoding(false));
            File.WriteAllText(stderrPath, stderr, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            status = "Error";
            errorType = "RunnerError";
            errorMessage = ex.Message;
        }

        var endTime = DateTimeOffset.UtcNow;
        var resultPayload = BuildResult(request, startTime, endTime, exitCode, status, errorType, errorMessage);
        JsonUtils.WriteJsonFile(Path.Combine(runFolder, "result.json"), resultPayload);

        return new TestCaseExecutionResult
        {
            RunId = request.RunId,
            RunFolder = runFolder,
            Status = status,
            StartTime = startTime,
            EndTime = endTime,
            ExitCode = exitCode,
            ErrorType = errorType,
            ErrorMessage = errorMessage
        };
    }

    private static void AddArguments(ProcessStartInfo psi, IReadOnlyDictionary<string, object?> inputs)
    {
        foreach (var (key, value) in inputs)
        {
            if (value is null)
            {
                continue;
            }

            if (value is IEnumerable<object?> list && value is not string)
            {
                foreach (var item in list)
                {
                    if (item is null)
                    {
                        continue;
                    }

                    psi.ArgumentList.Add($"-{key}");
                    psi.ArgumentList.Add(FormatValue(item));
                }
            }
            else
            {
                psi.ArgumentList.Add($"-{key}");
                psi.ArgumentList.Add(FormatValue(value));
            }
        }
    }

    private static string FormatValue(object value)
    {
        return value switch
        {
            bool boolValue => boolValue ? "$true" : "$false",
            int intValue => intValue.ToString(CultureInfo.InvariantCulture),
            double doubleValue => doubleValue.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static Dictionary<string, object?> BuildManifestSnapshot(TestCaseExecutionRequest request, DateTimeOffset resolvedAt)
    {
        return new Dictionary<string, object?>
        {
            ["schemaVersion"] = request.TestCase.SchemaVersion,
            ["sourceManifest"] = request.SourceManifest,
            ["resolvedRef"] = request.ResolvedRef,
            ["resolvedIdentity"] = new Dictionary<string, string>
            {
                ["id"] = request.Identity.Id,
                ["version"] = request.Identity.Version
            },
            ["effectiveEnvironment"] = new SortedDictionary<string, string>(request.EffectiveEnvironment, StringComparer.OrdinalIgnoreCase),
            ["effectiveInputs"] = new SortedDictionary<string, object?>(request.RedactedInputs, StringComparer.OrdinalIgnoreCase),
            ["inputTemplates"] = request.InputTemplates,
            ["resolvedAt"] = resolvedAt.ToString("O"),
            ["engineVersion"] = request.EngineVersion
        };
    }

    private static Dictionary<string, object?> BuildResult(
        TestCaseExecutionRequest request,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        int? exitCode,
        string status,
        string? errorType,
        string? errorMessage)
    {
        var payload = new Dictionary<string, object?>
        {
            ["schemaVersion"] = request.TestCase.SchemaVersion,
            ["runType"] = "TestCase",
            ["testId"] = request.Identity.Id,
            ["testVersion"] = request.Identity.Version,
            ["status"] = status,
            ["startTime"] = startTime.ToString("O"),
            ["endTime"] = endTime.ToString("O"),
            ["exitCode"] = exitCode,
            ["effectiveInputs"] = new SortedDictionary<string, object?>(request.RedactedInputs, StringComparer.OrdinalIgnoreCase)
        };

        if (!string.IsNullOrWhiteSpace(request.NodeId))
        {
            payload["nodeId"] = request.NodeId;
        }

        if (request.SuiteIdentity.HasValue)
        {
            payload["suiteId"] = request.SuiteIdentity.Value.Id;
            payload["suiteVersion"] = request.SuiteIdentity.Value.Version;
        }

        if (request.PlanIdentity.HasValue)
        {
            payload["planId"] = request.PlanIdentity.Value.Id;
            payload["planVersion"] = request.PlanIdentity.Value.Version;
        }

        if (!string.IsNullOrWhiteSpace(errorType))
        {
            payload["error"] = new Dictionary<string, object?>
            {
                ["type"] = errorType,
                ["source"] = errorType == "ScriptError" ? "Script" : "Runner",
                ["message"] = errorMessage
            };
        }

        return payload;
    }

    private static Dictionary<string, object?> BuildEnvSnapshot()
    {
        return new Dictionary<string, object?>
        {
            ["osVersion"] = Environment.OSVersion.ToString(),
            ["runnerVersion"] = typeof(RunnerService).Assembly.GetName().Version?.ToString(),
            ["powerShellVersion"] = GetPowerShellVersion(),
            ["isElevated"] = IsElevated()
        };
    }

    private static string GetPowerShellVersion()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pwsh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("-NoLogo");
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add("$PSVersionTable.PSVersion.ToString()");
            using var process = Process.Start(psi);
            if (process is null)
            {
                return "unknown";
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            return string.IsNullOrWhiteSpace(output) ? "unknown" : output.Trim();
        }
        catch
        {
            return "unknown";
        }
    }

    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string ResolveWorkingDir(TestCaseExecutionRequest request, string runFolder)
    {
        if (string.IsNullOrWhiteSpace(request.WorkingDir))
        {
            return runFolder;
        }

        var combined = Path.GetFullPath(Path.Combine(runFolder, request.WorkingDir));
        if (!PathUtils.IsContained(runFolder, combined))
        {
            return string.Empty;
        }

        Directory.CreateDirectory(combined);
        return combined;
    }

    private static string? ValidateInputs(TestCaseExecutionRequest request, string workingDir, string runFolder)
    {
        if (string.IsNullOrWhiteSpace(workingDir))
        {
            return "workingDir resolves outside of run folder.";
        }

        foreach (var parameter in request.TestCase.Parameters ?? Array.Empty<ParameterDefinition>())
        {
            if (!request.EffectiveInputs.TryGetValue(parameter.Name, out var value) || value is null)
            {
                continue;
            }

            if (!IsPathType(parameter.Type))
            {
                continue;
            }

            if (value is IEnumerable<object?> list && value is not string)
            {
                foreach (var item in list)
                {
                    var message = ValidatePathValue(parameter.Type, item, workingDir, runFolder);
                    if (message is not null)
                    {
                        return message;
                    }
                }
            }
            else
            {
                var message = ValidatePathValue(parameter.Type, value, workingDir, runFolder);
                if (message is not null)
                {
                    return message;
                }
            }
        }

        return null;
    }

    private static bool IsPathType(string type)
    {
        return type.StartsWith("path", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("file", StringComparison.OrdinalIgnoreCase)
            || type.StartsWith("folder", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ValidatePathValue(string type, object? value, string workingDir, string runFolder)
    {
        if (value is null)
        {
            return null;
        }

        var raw = value.ToString() ?? string.Empty;
        var resolved = Path.IsPathRooted(raw)
            ? Path.GetFullPath(raw)
            : Path.GetFullPath(Path.Combine(workingDir, raw));

        if (!PathUtils.IsContained(runFolder, resolved))
        {
            return "Path resolves outside of run folder.";
        }

        if (type.StartsWith("file", StringComparison.OrdinalIgnoreCase) && !File.Exists(resolved))
        {
            return $"File not found: {resolved}";
        }

        if (type.StartsWith("folder", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(resolved))
        {
            return $"Folder not found: {resolved}";
        }

        return null;
    }

    private static (string stdout, string stderr) RedactOutput(string stdout, string stderr, TestCaseExecutionRequest request)
    {
        foreach (var secretKey in request.SecretInputs)
        {
            if (!request.EffectiveInputs.TryGetValue(secretKey, out var value) || value is null)
            {
                continue;
            }

            var secretValue = value.ToString();
            if (!string.IsNullOrEmpty(secretValue))
            {
                stdout = stdout.Replace(secretValue, "***", StringComparison.OrdinalIgnoreCase);
                stderr = stderr.Replace(secretValue, "***", StringComparison.OrdinalIgnoreCase);
            }
        }

        return (stdout, stderr);
    }
}
