using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Runner;

public sealed class TestCaseRunner
{
    private readonly IPowerShellExecutor _executor;

    public TestCaseRunner(IPowerShellExecutor executor)
    {
        _executor = executor;
    }

    public async Task<TestCaseRunResult> ExecuteAsync(TestCaseRunRequest request, CancellationToken cancellationToken)
    {
        var runId = GenerateRunId(request.RunsRoot);
        var caseFolder = Path.Combine(request.RunsRoot, runId);
        Directory.CreateDirectory(caseFolder);

        var startTime = DateTime.UtcNow;
        var workingDir = ResolveWorkingDirectory(caseFolder, request.WorkingDir);

        try
        {
            EnsureWorkingDirContained(caseFolder, workingDir);
        }
        catch (Exception ex)
        {
            var errorResult = BuildErrorResult(request, runId, caseFolder, startTime, ex, "RunnerError", RunStatus.Error);
            WriteArtifacts(request, errorResult, caseFolder, new PowerShellExecutionResult(null, string.Empty, ex.Message, false, false), workingDir);
            return errorResult;
        }

        Directory.CreateDirectory(workingDir);
        Dictionary<string, object?> effectiveInputs;
        try
        {
            effectiveInputs = ResolvePathInputs(request, workingDir, caseFolder);
        }
        catch (Exception ex)
        {
            var errorResult = BuildErrorResult(request, runId, caseFolder, startTime, ex, "RunnerError", RunStatus.Error);
            WriteArtifacts(request, errorResult, caseFolder, new PowerShellExecutionResult(null, string.Empty, ex.Message, false, false), workingDir);
            return errorResult;
        }

        var argumentList = BuildArgumentList(request.Manifest, effectiveInputs, Path.Combine(request.ResolvedRef, "run.ps1"));
        var pwshPath = ResolvePwshPath();

        var executionRequest = new PowerShellExecutionRequest(
            pwshPath,
            Path.Combine(request.ResolvedRef, "run.ps1"),
            workingDir,
            argumentList,
            request.EffectiveEnvironment,
            request.TimeoutSec.HasValue ? TimeSpan.FromSeconds(request.TimeoutSec.Value) : null);

        PowerShellExecutionResult executionResult;
        try
        {
            executionResult = await _executor.ExecuteAsync(executionRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            var errorResult = BuildErrorResult(request, runId, caseFolder, startTime, ex, "RunnerError", RunStatus.Error);
            WriteArtifacts(request, errorResult, caseFolder, new PowerShellExecutionResult(null, string.Empty, ex.ToString(), false, false), workingDir);
            return errorResult;
        }

        var status = MapStatus(executionResult);
        var endTime = DateTime.UtcNow;
        var result = new TestCaseRunResult
        {
            RunId = runId,
            Status = status.status,
            StartTime = startTime.ToString("O"),
            EndTime = endTime.ToString("O"),
            ExitCode = executionResult.ExitCode,
            Error = status.error,
            CaseRunFolder = caseFolder,
            ResultPayload = BuildResultPayload(request, status.status, status.error, executionResult.ExitCode, startTime, endTime, effectiveInputs)
        };

        WriteArtifacts(request, result, caseFolder, executionResult, workingDir);
        return result;
    }

    private static string GenerateRunId(string runsRoot)
    {
        string runId;
        do
        {
            runId = $"R-{Guid.NewGuid():N}";
        } while (Directory.Exists(Path.Combine(runsRoot, runId)));

        return runId;
    }

    private static string ResolveWorkingDirectory(string caseFolder, string? workingDir)
    {
        if (string.IsNullOrWhiteSpace(workingDir))
        {
            return caseFolder;
        }
        if (Path.IsPathRooted(workingDir))
        {
            if (PathUtilities.IsPathContained(caseFolder, workingDir))
            {
                var relative = Path.GetRelativePath(caseFolder, workingDir);
                return PathUtilities.ResolvePathWithReparsePoints(caseFolder, relative);
            }

            return PathUtilities.NormalizePath(workingDir);
        }

        return PathUtilities.ResolvePathWithReparsePoints(caseFolder, workingDir);
    }

    private static void EnsureWorkingDirContained(string caseFolder, string workingDir)
    {
        if (!PathUtilities.IsPathContained(caseFolder, workingDir))
        {
            throw new InvalidOperationException("Working directory escapes case run folder.");
        }
    }

    private static Dictionary<string, object?> ResolvePathInputs(TestCaseRunRequest request, string workingDir, string caseFolder)
    {
        var inputs = new Dictionary<string, object?>();
        var parameters = request.Manifest.Parameters ?? Array.Empty<ParameterDefinition>();
        var parameterMap = parameters.ToDictionary(p => p.Name, p => p, StringComparer.Ordinal);

        foreach (var (key, value) in request.EffectiveInputs)
        {
            if (!parameterMap.TryGetValue(key, out var parameter))
            {
                inputs[key] = value;
                continue;
            }

            inputs[key] = ResolvePathInput(parameter, value, workingDir, caseFolder);
        }

        return inputs;
    }

    private static object? ResolvePathInput(ParameterDefinition parameter, object? value, string workingDir, string caseFolder)
    {
        if (value == null)
        {
            return null;
        }

        if (parameter.ParsedType.IsArray() && value is IEnumerable<object?> items)
        {
            return items.Select(item => ResolvePathInput(parameter with { Type = parameter.Type.Replace("[]", "") }, item, workingDir, caseFolder)).ToArray();
        }

        if (value is not string stringValue)
        {
            return value;
        }

        if (parameter.ParsedType is ParameterType.Path or ParameterType.File or ParameterType.Folder)
        {
            var baseDir = workingDir ?? caseFolder;
            var resolved = Path.IsPathRooted(stringValue) ? PathUtilities.NormalizePath(stringValue) : PathUtilities.NormalizePath(Path.Combine(baseDir, stringValue));
            if (parameter.ParsedType == ParameterType.File && !File.Exists(resolved))
            {
                throw new InvalidOperationException($"Required file '{resolved}' not found.");
            }

            if (parameter.ParsedType == ParameterType.Folder && !Directory.Exists(resolved))
            {
                throw new InvalidOperationException($"Required folder '{resolved}' not found.");
            }

            return resolved;
        }

        return value;
    }

    public static IReadOnlyList<string> BuildArgumentList(TestCaseManifest manifest, Dictionary<string, object?> effectiveInputs, string scriptPath)
    {
        var args = new List<string>
        {
            scriptPath
        };

        var parameters = manifest.Parameters ?? Array.Empty<ParameterDefinition>();
        var parameterMap = parameters.ToDictionary(p => p.Name, p => p, StringComparer.Ordinal);

        foreach (var (name, value) in effectiveInputs)
        {
            if (!parameterMap.TryGetValue(name, out var parameter))
            {
                continue;
            }

            if (value == null)
            {
                continue;
            }

            args.Add($"-{name}");
            if (parameter.ParsedType.IsArray() && value is IEnumerable<object?> arrayValue)
            {
                var scalarParameter = GetScalarParameter(parameter);
                foreach (var item in arrayValue)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    args.Add(SerializeArgumentValue(scalarParameter, item));
                }
            }
            else
            {
                args.Add(SerializeArgumentValue(parameter, value));
            }
        }

        return args;
    }

    private static string SerializeArgumentValue(ParameterDefinition parameter, object value)
    {
        if (parameter.ParsedType == ParameterType.Boolean)
        {
            var boolValue = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            return boolValue ? "$true" : "$false";
        }

        if (parameter.ParsedType == ParameterType.Int)
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }

        if (parameter.ParsedType == ParameterType.Double)
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString() ?? string.Empty;
    }

    private static ParameterDefinition GetScalarParameter(ParameterDefinition parameter)
    {
        if (!parameter.ParsedType.IsArray())
        {
            return parameter;
        }

        var scalarType = parameter.Type.Replace("[]", string.Empty, StringComparison.Ordinal);
        return parameter with { Type = scalarType };
    }

    private static (RunStatus status, ErrorInfo? error) MapStatus(PowerShellExecutionResult execution)
    {
        if (execution.TimedOut)
        {
            return (RunStatus.Timeout, new ErrorInfo { Type = "Timeout", Source = "Runner", Message = "Execution timed out." });
        }

        if (execution.Aborted)
        {
            return (RunStatus.Aborted, new ErrorInfo { Type = "Aborted", Source = "Runner", Message = "Execution aborted." });
        }

        if (!execution.ExitCode.HasValue)
        {
            return (RunStatus.Error, new ErrorInfo { Type = "RunnerError", Source = "Runner", Message = "Runner error." });
        }

        return execution.ExitCode.Value switch
        {
            0 => (RunStatus.Passed, null),
            1 => (RunStatus.Failed, null),
            _ => (RunStatus.Error, new ErrorInfo { Type = "ScriptError", Source = "Script", Message = "Script error." })
        };
    }

    private static TestCaseRunResult BuildErrorResult(TestCaseRunRequest request, string runId, string caseFolder, DateTime startTime, Exception ex, string errorType, RunStatus status)
    {
        var endTime = DateTime.UtcNow;
        var error = new ErrorInfo
        {
            Type = errorType,
            Source = "Runner",
            Message = ex.Message,
            Stack = ex.ToString()
        };

        var resultPayload = BuildResultPayload(request, status, error, null, startTime, endTime, request.EffectiveInputs);

        return new TestCaseRunResult
        {
            RunId = runId,
            Status = status,
            StartTime = startTime.ToString("O"),
            EndTime = endTime.ToString("O"),
            ExitCode = null,
            Error = error,
            CaseRunFolder = caseFolder,
            ResultPayload = resultPayload
        };
    }

    private static TestCaseResult BuildResultPayload(TestCaseRunRequest request, RunStatus status, ErrorInfo? error, int? exitCode, DateTime startTime, DateTime endTime, Dictionary<string, object?> effectiveInputs)
    {
        return new TestCaseResult
        {
            SchemaVersion = SchemaConstants.SchemaVersion,
            RunType = RunType.TestCase,
            NodeId = request.NodeId,
            TestId = request.Identity.Id,
            TestVersion = request.Identity.Version,
            SuiteId = request.SuiteId,
            SuiteVersion = request.SuiteVersion,
            PlanId = request.PlanId,
            PlanVersion = request.PlanVersion,
            Status = status,
            StartTime = startTime.ToString("O"),
            EndTime = endTime.ToString("O"),
            ExitCode = exitCode,
            EffectiveInputs = request.RedactedInputs,
            Error = error,
            Runner = new Dictionary<string, object?>
            {
                ["os"] = RuntimeInformation.OSDescription,
                ["pwsh"] = ResolvePwshPath()
            }
        };
    }

    private static void WriteArtifacts(TestCaseRunRequest request, TestCaseRunResult result, string caseFolder, PowerShellExecutionResult execution, string workingDir)
    {
        var manifestSnapshot = new Dictionary<string, object?>
        {
            ["sourceManifest"] = request.Manifest,
            ["resolvedRef"] = request.ResolvedRef,
            ["resolvedIdentity"] = new Dictionary<string, string>
            {
                ["id"] = request.Identity.Id,
                ["version"] = request.Identity.Version
            },
            ["effectiveEnvironment"] = request.EffectiveEnvironment,
            ["effectiveInputs"] = request.RedactedInputs,
            ["resolvedAt"] = DateTime.UtcNow.ToString("O")
        };

        WriteJson(Path.Combine(caseFolder, "manifest.json"), manifestSnapshot);
        WriteJson(Path.Combine(caseFolder, "params.json"), request.RedactedInputs);
        WriteJson(Path.Combine(caseFolder, "env.json"), new Dictionary<string, object?>
        {
            ["os"] = RuntimeInformation.OSDescription,
            ["runnerVersion"] = "1.0.0",
            ["powerShell"] = ResolvePwshPath(),
            ["elevated"] = false
        });

        File.WriteAllText(Path.Combine(caseFolder, "stdout.log"), RedactOutput(execution.Stdout, request.SecretInputs, request.EffectiveInputs));
        File.WriteAllText(Path.Combine(caseFolder, "stderr.log"), RedactOutput(execution.Stderr, request.SecretInputs, request.EffectiveInputs));

        var eventsPath = Path.Combine(caseFolder, "events.jsonl");
        using (var writer = new StreamWriter(eventsPath))
        {
            foreach (var ev in request.Events)
            {
                writer.WriteLine(JsonSerializer.Serialize(new { ts = ev.Timestamp.ToString("O"), code = ev.Code, data = ev.Data }, JsonUtilities.SerializerOptions));
            }
        }

        WriteJson(Path.Combine(caseFolder, "result.json"), result.ResultPayload);
    }

    private static string RedactOutput(string output, HashSet<string> secretInputs, Dictionary<string, object?> effectiveInputs)
    {
        var redacted = output;
        foreach (var secretName in secretInputs)
        {
            if (!effectiveInputs.TryGetValue(secretName, out var value) || value == null)
            {
                continue;
            }

            redacted = redacted.Replace(value.ToString() ?? string.Empty, "***", StringComparison.Ordinal);
        }

        return redacted;
    }

    private static void WriteJson(string path, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonUtilities.SerializerOptions);
        File.WriteAllText(path, json);
    }

    private static string ResolvePwshPath()
    {
        var path = Environment.GetEnvironmentVariable("PWSH_PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "pwsh.exe" : "pwsh";
    }
}
