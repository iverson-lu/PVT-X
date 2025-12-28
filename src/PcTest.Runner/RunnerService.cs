using System.Diagnostics;
using System.Globalization;
using PcTest.Contracts;

namespace PcTest.Runner;

public sealed class RunnerService : IRunner
{
    private readonly IProcessRunner _processRunner;
    private readonly bool _validatePowerShell;
    private bool _powerShellValidated;

    public RunnerService(IProcessRunner processRunner, bool validatePowerShell = true)
    {
        _processRunner = processRunner;
        _validatePowerShell = validatePowerShell;
    }

    public async Task<RunnerResult> RunTestCaseAsync(RunnerRequest request, CancellationToken cancellationToken)
    {
        string runId = CreateRunId(request.RunsRoot);
        string runFolder = Path.Combine(request.RunsRoot, runId);
        Directory.CreateDirectory(runFolder);

        string scriptPath = Path.Combine(Path.GetDirectoryName(request.ManifestPath) ?? string.Empty, "run.ps1");
        DateTimeOffset start = DateTimeOffset.UtcNow;
        string startTime = start.ToString("O");

        if (_validatePowerShell && !_powerShellValidated)
        {
            await EnsurePowerShellVersionAsync(cancellationToken).ConfigureAwait(false);
            _powerShellValidated = true;
        }

        PreNodeValidationResult validation = PreNodeValidator.Validate(request, runFolder);
        if (!validation.IsValid)
        {
            var result = BuildResult(request, startTime, DateTimeOffset.UtcNow.ToString("O"), "Error", null, new RunError
            {
                Type = "RunnerError",
                Source = "Runner",
                Message = validation.Message
            });

            WriteArtifacts(runFolder, request, result, validation.RedactedInputs);
            return new RunnerResult(runId, result.Status, result.StartTime, result.EndTime, request.NodeId, request.ParentRunId);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = validation.WorkingDirectory
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var arg in BuildArguments(request.EffectiveInputs))
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var kvp in request.EffectiveEnvironment)
        {
            startInfo.Environment[kvp.Key] = kvp.Value;
        }

        if (request.SecretInputs.Values.Any(secret => secret))
        {
            JsonUtilities.AppendJsonLine(Path.Combine(runFolder, "events.jsonl"), new
            {
                code = "EnvRef.SecretOnCommandLine",
                message = "Secret value passed on command line."
            });
        }

        ProcessRunResult processResult;
        try
        {
            TimeSpan? timeout = request.TestCase.TimeoutSec is not null ? TimeSpan.FromSeconds(request.TestCase.TimeoutSec.Value) : null;
            processResult = await _processRunner.RunAsync(startInfo, timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var errorResult = BuildResult(request, startTime, DateTimeOffset.UtcNow.ToString("O"), "Error", null, new RunError
            {
                Type = "RunnerError",
                Source = "Runner",
                Message = ex.Message,
                Stack = ex.StackTrace
            });

            WriteArtifacts(runFolder, request, errorResult, request.RedactedInputs);
            return new RunnerResult(runId, errorResult.Status, errorResult.StartTime, errorResult.EndTime, request.NodeId, request.ParentRunId);
        }

        string status = MapStatus(processResult, cancellationToken.IsCancellationRequested);
        RunError? error = null;
        if (status == "Timeout")
        {
            error = new RunError
            {
                Type = "Timeout",
                Source = "Runner",
                Message = "Execution timed out."
            };
        }
        else if (status == "Aborted")
        {
            error = new RunError
            {
                Type = "Aborted",
                Source = "Runner",
                Message = "Execution aborted."
            };
        }
        else if (status == "Error")
        {
            error = new RunError
            {
                Type = "ScriptError",
                Source = "Script",
                Message = "Script error."
            };
        }

        string endTime = DateTimeOffset.UtcNow.ToString("O");
        var result = BuildResult(request, startTime, endTime, status, processResult.ExitCode, error);
        WriteArtifacts(runFolder, request, result, request.RedactedInputs, processResult);
        return new RunnerResult(runId, status, startTime, endTime, request.NodeId, request.ParentRunId);
    }

    private static IEnumerable<string> BuildArguments(IReadOnlyDictionary<string, object> inputs)
    {
        foreach (var kvp in inputs)
        {
            yield return $"-{kvp.Key}";
            foreach (var value in ExpandValue(kvp.Value))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> ExpandValue(object value)
    {
        return value switch
        {
            bool b => new[] { b ? "$true" : "$false" },
            string s => new[] { s },
            int i => new[] { i.ToString(CultureInfo.InvariantCulture) },
            double d => new[] { d.ToString(CultureInfo.InvariantCulture) },
            IEnumerable<string> strings => strings,
            IEnumerable<int> ints => ints.Select(i => i.ToString(CultureInfo.InvariantCulture)),
            IEnumerable<double> doubles => doubles.Select(d => d.ToString(CultureInfo.InvariantCulture)),
            IEnumerable<bool> bools => bools.Select(b => b ? "$true" : "$false"),
            _ => new[] { value.ToString() ?? string.Empty }
        };
    }

    private static TestCaseResult BuildResult(
        RunnerRequest request,
        string startTime,
        string endTime,
        string status,
        int? exitCode,
        RunError? error)
    {
        return new TestCaseResult
        {
            SchemaVersion = "1.5.0",
            RunType = "TestCase",
            NodeId = request.NodeId,
            TestId = request.TestCase.Id,
            TestVersion = request.TestCase.Version,
            SuiteId = request.SuiteIdentity?.Id,
            SuiteVersion = request.SuiteIdentity?.Version,
            PlanId = request.PlanIdentity?.Id,
            PlanVersion = request.PlanIdentity?.Version,
            Status = status,
            StartTime = startTime,
            EndTime = endTime,
            ExitCode = exitCode,
            EffectiveInputs = request.RedactedInputs,
            Error = error
        };
    }

    private static void WriteArtifacts(
        string runFolder,
        RunnerRequest request,
        TestCaseResult result,
        IReadOnlyDictionary<string, object> redactedInputs,
        ProcessRunResult? processResult = null)
    {
        JsonUtilities.WriteJsonFile(Path.Combine(runFolder, "manifest.json"), request.ManifestSnapshot);
        JsonUtilities.WriteJsonFile(Path.Combine(runFolder, "params.json"), redactedInputs);
        JsonUtilities.WriteJsonFile(Path.Combine(runFolder, "env.json"), new
        {
            os = Environment.OSVersion.ToString(),
            runnerVersion = "Runner/1.0.0",
            powerShellVersion = "Unknown",
            elevated = false
        });
        if (processResult is not null)
        {
            File.WriteAllText(Path.Combine(runFolder, "stdout.log"), processResult.Stdout);
            File.WriteAllText(Path.Combine(runFolder, "stderr.log"), processResult.Stderr);
        }

        JsonUtilities.WriteJsonFile(Path.Combine(runFolder, "result.json"), result);
    }

    private static string MapStatus(ProcessRunResult result, bool aborted)
    {
        if (aborted)
        {
            return "Aborted";
        }

        if (result.TimedOut)
        {
            return "Timeout";
        }

        return result.ExitCode switch
        {
            0 => "Passed",
            1 => "Failed",
            _ => "Error"
        };
    }

    private static string CreateRunId(string runsRoot)
    {
        while (true)
        {
            string id = $"R-{Guid.NewGuid():N}";
            if (!Directory.Exists(Path.Combine(runsRoot, id)))
            {
                return id;
            }
        }
    }

    private async Task EnsurePowerShellVersionAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("$PSVersionTable.PSVersion.Major");

        ProcessRunResult result = await _processRunner.RunAsync(startInfo, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        if (!int.TryParse(result.Stdout.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int major) || major < 7)
        {
            throw new PcTestException("Runner.PowerShell.Invalid", "PowerShell 7+ is required.");
        }
    }
}

internal sealed class PreNodeValidationResult
{
    public PreNodeValidationResult(bool isValid, string message, string workingDirectory, IReadOnlyDictionary<string, object> redactedInputs)
    {
        IsValid = isValid;
        Message = message;
        WorkingDirectory = workingDirectory;
        RedactedInputs = redactedInputs;
    }

    public bool IsValid { get; }
    public string Message { get; }
    public string WorkingDirectory { get; }
    public IReadOnlyDictionary<string, object> RedactedInputs { get; }
}

internal static class PreNodeValidator
{
    public static PreNodeValidationResult Validate(RunnerRequest request, string runFolder)
    {
        string workingDir = runFolder;
        string? requestedWorkingDir = request.WorkingDirectory;
        if (!string.IsNullOrWhiteSpace(requestedWorkingDir))
        {
            string combined = PathUtilities.GetCanonicalPath(Path.Combine(runFolder, requestedWorkingDir));
            if (!PathUtilities.IsContained(runFolder, combined))
            {
                return new PreNodeValidationResult(false, "workingDir escapes run folder.", runFolder, request.RedactedInputs);
            }

            workingDir = combined;
            Directory.CreateDirectory(workingDir);
        }

        var parameters = request.TestCase.Parameters ?? Array.Empty<ParameterDefinition>();
        foreach (var parameter in parameters)
        {
            if (!request.EffectiveInputs.TryGetValue(parameter.Name, out var value))
            {
                continue;
            }

            if (parameter.Type is ParameterType.Path or ParameterType.File or ParameterType.Folder)
            {
                foreach (string pathValue in ExpandPaths(value))
                {
                    string resolved = ResolvePath(workingDir, pathValue);
                    if (!PathUtilities.IsContained(runFolder, resolved))
                    {
                        return new PreNodeValidationResult(false, "Path escapes run folder.", workingDir, request.RedactedInputs, null);
                    }

                    if (parameter.Type == ParameterType.File && !File.Exists(resolved))
                    {
                        return new PreNodeValidationResult(false, "File does not exist.", workingDir, request.RedactedInputs, null);
                    }

                    if (parameter.Type == ParameterType.Folder && !Directory.Exists(resolved))
                    {
                        return new PreNodeValidationResult(false, "Folder does not exist.", workingDir, request.RedactedInputs, null);
                    }
                }
            }
        }

        return new PreNodeValidationResult(true, string.Empty, workingDir, request.RedactedInputs);
    }

    private static string ResolvePath(string baseDir, string value)
    {
        if (Path.IsPathRooted(value))
        {
            return PathUtilities.GetCanonicalPath(value);
        }

        return PathUtilities.GetCanonicalPath(Path.Combine(baseDir, value));
    }

    private static IEnumerable<string> ExpandPaths(object value)
    {
        return value switch
        {
            string s => new[] { s },
            IEnumerable<string> strings => strings,
            _ => Array.Empty<string>()
        };
    }
}
