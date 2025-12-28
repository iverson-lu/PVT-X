using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Runner;

public sealed class PwshRunner : ICaseRunner
{
    private readonly IProcessRunner _processRunner;

    public PwshRunner(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<CaseRunResult> RunCaseAsync(CaseRunRequest request, CancellationToken cancellationToken)
    {
        var runId = RunIdGenerator.NextRunId(request.RunsRoot, "R-");
        var runFolder = Path.Combine(request.RunsRoot, runId);
        Directory.CreateDirectory(runFolder);
        Directory.CreateDirectory(Path.Combine(runFolder, "artifacts"));

        var startTime = DateTimeOffset.UtcNow;
        var manifestPath = Path.Combine(runFolder, "manifest.json");
        var paramsPath = Path.Combine(runFolder, "params.json");
        var resultPath = Path.Combine(runFolder, "result.json");
        var stdoutPath = Path.Combine(runFolder, "stdout.log");
        var stderrPath = Path.Combine(runFolder, "stderr.log");
        var eventsPath = Path.Combine(runFolder, "events.jsonl");
        var envPath = Path.Combine(runFolder, "env.json");

        var redactor = new Redactor(request.SecretInputNames, request.EffectiveInputs);
        var redactedInputs = redactor.RedactDictionary(request.EffectiveInputs);
        WriteManifestSnapshot(manifestPath, request, redactor, redactedInputs);
        JsonUtils.WriteFile(paramsPath, redactedInputs);
        WriteEnvSnapshot(envPath);

        var validationError = ValidateWorkingDirectory(request, runFolder, out var workingDir);
        if (validationError != null)
        {
            var result = BuildErrorResult(request, runId, startTime, validationError);
            JsonUtils.WriteFile(resultPath, result);
            File.WriteAllText(stdoutPath, string.Empty, Encoding.UTF8);
            File.WriteAllText(stderrPath, string.Empty, Encoding.UTF8);
            return BuildCaseRunResult(request, redactedInputs, runId, startTime, DateTimeOffset.UtcNow, null, RunStatus.Error, validationError);
        }

        var inputError = ValidateInputPaths(request, workingDir, runFolder);
        if (inputError != null)
        {
            var result = BuildErrorResult(request, runId, startTime, inputError);
            JsonUtils.WriteFile(resultPath, result);
            File.WriteAllText(stdoutPath, string.Empty, Encoding.UTF8);
            File.WriteAllText(stderrPath, string.Empty, Encoding.UTF8);
            return BuildCaseRunResult(request, redactedInputs, runId, startTime, DateTimeOffset.UtcNow, null, RunStatus.Error, inputError);
        }

        var (args, secretWarnings) = BuildArgumentList(request);
        foreach (var warning in secretWarnings)
        {
            AppendEvent(eventsPath, warning);
        }

        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(Path.Combine(request.TestCaseFolder, "run.ps1"));
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        ProcessResult processResult;
        try
        {
            processResult = await _processRunner.RunAsync(psi, request.TimeoutSec, cancellationToken);
        }
        catch (Exception ex)
        {
            var error = new CaseRunError("RunnerError", "Runner", ex.Message, ex.StackTrace);
            var result = BuildErrorResult(request, runId, startTime, error);
            JsonUtils.WriteFile(resultPath, result);
            return BuildCaseRunResult(request, redactedInputs, runId, startTime, DateTimeOffset.UtcNow, null, RunStatus.Error, error);
        }

        var endTime = DateTimeOffset.UtcNow;
        var stdout = redactor.Redact(processResult.StandardOutput);
        var stderr = redactor.Redact(processResult.StandardError);
        File.WriteAllText(stdoutPath, stdout, Encoding.UTF8);
        File.WriteAllText(stderrPath, stderr, Encoding.UTF8);

        var (status, errorDetails) = MapStatus(processResult);
        var resultJson = BuildResultJson(request, redactedInputs, runId, startTime, endTime, processResult.ExitCode, status, errorDetails);
        JsonUtils.WriteFile(resultPath, resultJson);

        return BuildCaseRunResult(request, redactedInputs, runId, startTime, endTime, processResult.ExitCode, status, errorDetails);
    }

    internal static (List<string> Args, List<Dictionary<string, object?>> Warnings) BuildArgumentList(CaseRunRequest request)
    {
        var args = new List<string>();
        var warnings = new List<Dictionary<string, object?>>();
        foreach (var (name, value) in request.EffectiveInputs)
        {
            if (value == null)
            {
                continue;
            }

            args.Add($"-{name}");
            AppendValue(args, value);

            if (request.SecretInputNames.Contains(name))
            {
                warnings.Add(new Dictionary<string, object?>
                {
                    ["code"] = SpecConstants.EnvRefSecretOnCommandLine,
                    ["parameter"] = name,
                    ["nodeId"] = request.NodeId
                });
            }
        }

        return (args, warnings);
    }

    internal static (RunStatus Status, CaseRunError? Error) MapStatus(ProcessResult result)
    {
        if (result.TimedOut)
        {
            return (RunStatus.Timeout, new CaseRunError("Timeout", "Runner", "Execution timed out.", null));
        }

        if (result.Aborted)
        {
            return (RunStatus.Aborted, new CaseRunError("Aborted", "Runner", "Execution aborted.", null));
        }

        if (result.StartFailure != null)
        {
            return (RunStatus.Error, new CaseRunError("RunnerError", "Runner", result.StartFailure.Message, result.StartFailure.StackTrace));
        }

        if (result.ExitCode == 0)
        {
            return (RunStatus.Passed, null);
        }

        if (result.ExitCode == 1)
        {
            return (RunStatus.Failed, null);
        }

        return (RunStatus.Error, new CaseRunError("ScriptError", "Script", $"Exit code {result.ExitCode}", null));
    }

    private static void AppendValue(List<string> args, object value)
    {
        switch (value)
        {
            case bool boolValue:
                args.Add(boolValue ? "$true" : "$false");
                break;
            case int or double:
                args.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
                break;
            case IEnumerable<object?> list:
                foreach (var item in list)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    args.Add(item is bool boolItem
                        ? boolItem ? "$true" : "$false"
                        : Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty);
                }
                break;
            default:
                args.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
                break;
        }
    }

    private static CaseRunError? ValidateWorkingDirectory(CaseRunRequest request, string runFolder, out string workingDir)
    {
        var resolved = runFolder;
        if (!string.IsNullOrEmpty(request.WorkingDirectory))
        {
            var candidate = request.WorkingDirectory!;
            resolved = Path.IsPathRooted(candidate) ? candidate : Path.Combine(runFolder, candidate);
            var normalized = PathUtils.ResolvePathWithLinks(Path.GetFullPath(resolved));
            var rootNormalized = PathUtils.ResolvePathWithLinks(Path.GetFullPath(runFolder));
            if (!PathUtils.IsContainedBy(rootNormalized, normalized))
            {
                workingDir = runFolder;
                return new CaseRunError("RunnerError", "Runner", "workingDir escapes CaseRunFolder.", null);
            }

            Directory.CreateDirectory(normalized);
            workingDir = normalized;
            return null;
        }

        workingDir = runFolder;
        return null;
    }

    private static CaseRunError? ValidateInputPaths(CaseRunRequest request, string workingDir, string runFolder)
    {
        foreach (var (name, value) in request.EffectiveInputs)
        {
            if (!request.ParameterTypes.TryGetValue(name, out var type) || value == null)
            {
                continue;
            }

            var normalizedType = type.Trim().ToLowerInvariant();
            if (normalizedType is not ("file" or "folder" or "file[]" or "folder[]"))
            {
                continue;
            }

            if (normalizedType.EndsWith("[]", StringComparison.Ordinal))
            {
                if (value is not IEnumerable<object?> list)
                {
                    continue;
                }

                foreach (var item in list)
                {
                    if (item is string text)
                    {
                        var error = ValidatePathValue(normalizedType[..^2], text, workingDir, runFolder);
                        if (error != null)
                        {
                            return error;
                        }
                    }
                }

                continue;
            }

            if (value is string stringValue)
            {
                var error = ValidatePathValue(normalizedType, stringValue, workingDir, runFolder);
                if (error != null)
                {
                    return error;
                }
            }
        }

        return null;
    }

    private static CaseRunError? ValidatePathValue(string type, string value, string workingDir, string runFolder)
    {
        var resolved = Path.IsPathRooted(value) ? value : Path.Combine(workingDir, value);
        var normalized = PathUtils.ResolvePathWithLinks(Path.GetFullPath(resolved));
        if (!PathUtils.IsContainedBy(runFolder, normalized) && !Path.IsPathRooted(value))
        {
            return new CaseRunError("RunnerError", "Runner", "Input path escapes CaseRunFolder.", null);
        }

        if (type == "file" && !File.Exists(normalized))
        {
            return new CaseRunError("RunnerError", "Runner", $"File not found: {value}", null);
        }

        if (type == "folder" && !Directory.Exists(normalized))
        {
            return new CaseRunError("RunnerError", "Runner", $"Folder not found: {value}", null);
        }

        return null;
    }

    private static void WriteManifestSnapshot(string path, CaseRunRequest request, Redactor redactor, IReadOnlyDictionary<string, object?> redactedInputs)
    {
        var snapshot = new Dictionary<string, object?>
        {
            ["schemaVersion"] = SpecConstants.SchemaVersion,
            ["sourceManifest"] = request.SourceManifest,
            ["resolvedRef"] = request.ResolvedRef,
            ["resolvedIdentity"] = new Dictionary<string, object?>
            {
                ["id"] = request.TestCaseIdentity.Id,
                ["version"] = request.TestCaseIdentity.Version
            },
            ["effectiveEnvironment"] = request.EffectiveEnvironment,
            ["effectiveInputs"] = redactedInputs,
            ["inputTemplates"] = request.InputTemplates,
            ["resolvedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["engineVersion"] = request.EngineVersion
        };

        JsonUtils.WriteFile(path, snapshot);
    }

    private static void WriteEnvSnapshot(string path)
    {
        var snapshot = new Dictionary<string, object?>
        {
            ["osVersion"] = Environment.OSVersion.VersionString,
            ["runnerVersion"] = EngineVersion.Current,
            ["dotnetVersion"] = Environment.Version.ToString(),
            ["isElevated"] = false
        };

        JsonUtils.WriteFile(path, snapshot);
    }

    private static Dictionary<string, object?> BuildResultJson(
        CaseRunRequest request,
        IReadOnlyDictionary<string, object?> redactedInputs,
        string runId,
        DateTimeOffset start,
        DateTimeOffset end,
        int? exitCode,
        RunStatus status,
        CaseRunError? error)
    {
        var payload = new Dictionary<string, object?>
        {
            ["schemaVersion"] = SpecConstants.SchemaVersion,
            ["runType"] = "TestCase",
            ["testId"] = request.TestCaseIdentity.Id,
            ["testVersion"] = request.TestCaseIdentity.Version,
            ["status"] = status.ToString(),
            ["startTime"] = start.ToString("O", CultureInfo.InvariantCulture),
            ["endTime"] = end.ToString("O", CultureInfo.InvariantCulture),
            ["exitCode"] = exitCode,
            ["effectiveInputs"] = redactedInputs
        };

        if (!string.IsNullOrEmpty(request.NodeId))
        {
            payload["nodeId"] = request.NodeId;
            payload["suiteId"] = request.SuiteIdentity?.Id;
            payload["suiteVersion"] = request.SuiteIdentity?.Version;
        }

        if (request.PlanIdentity != null)
        {
            payload["planId"] = request.PlanIdentity.Id;
            payload["planVersion"] = request.PlanIdentity.Version;
        }

        if (error != null)
        {
            payload["error"] = new Dictionary<string, object?>
            {
                ["type"] = error.Type,
                ["source"] = error.Source,
                ["message"] = error.Message,
                ["stack"] = error.Stack
            };
        }

        return payload;
    }

    private static CaseRunResult BuildCaseRunResult(
        CaseRunRequest request,
        IReadOnlyDictionary<string, object?> redactedInputs,
        string runId,
        DateTimeOffset start,
        DateTimeOffset end,
        int? exitCode,
        RunStatus status,
        CaseRunError? error)
    {
        return new CaseRunResult(
            runId,
            request.TestCaseIdentity,
            status,
            start,
            end,
            exitCode,
            error,
            request.NodeId,
            request.SuiteIdentity,
            request.PlanIdentity,
            request.EffectiveInputs,
            redactedInputs);
    }

    private static Dictionary<string, object?> BuildErrorResult(CaseRunRequest request, string runId, DateTimeOffset start, CaseRunError error)
    {
        var redactedInputs = new Redactor(request.SecretInputNames, request.EffectiveInputs).RedactDictionary(request.EffectiveInputs);
        return BuildResultJson(request, redactedInputs, runId, start, DateTimeOffset.UtcNow, null, RunStatus.Error, error);
    }

    private static void AppendEvent(string path, Dictionary<string, object?> entry)
    {
        var json = JsonSerializer.Serialize(entry, JsonUtils.SerializerOptions);
        File.AppendAllText(path, json + Environment.NewLine);
    }
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, int? timeoutSec, CancellationToken cancellationToken);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, int? timeoutSec, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        try
        {
            process.OutputDataReceived += (_, args) => { if (args.Data != null) stdout.AppendLine(args.Data); };
            process.ErrorDataReceived += (_, args) => { if (args.Data != null) stderr.AppendLine(args.Data); };
            if (!process.Start())
            {
                return new ProcessResult(null, stdout.ToString(), stderr.ToString(), false, false, new InvalidOperationException("Failed to start process."));
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = timeoutSec.HasValue
                ? new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec.Value))
                : null;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts?.Token ?? CancellationToken.None);

            await process.WaitForExitAsync(linked.Token);
            return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), timeoutCts?.IsCancellationRequested ?? false, cancellationToken.IsCancellationRequested, null);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }

            var timedOut = timeoutCts?.IsCancellationRequested ?? false;
            var aborted = cancellationToken.IsCancellationRequested;
            return new ProcessResult(process.HasExited ? process.ExitCode : null, stdout.ToString(), stderr.ToString(), timedOut, aborted, null);
        }
    }
}

public sealed record ProcessResult(
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    bool Aborted,
    Exception? StartFailure
);

internal sealed class Redactor
{
    private readonly IReadOnlyCollection<string> _secretNames;
    private readonly IReadOnlyDictionary<string, object?> _effectiveInputs;

    public Redactor(IReadOnlyCollection<string> secretNames, IReadOnlyDictionary<string, object?> effectiveInputs)
    {
        _secretNames = secretNames;
        _effectiveInputs = effectiveInputs;
    }

    public string Redact(string value)
    {
        var result = value;
        foreach (var secretName in _secretNames)
        {
            if (_effectiveInputs.TryGetValue(secretName, out var secretValue) && secretValue != null)
            {
                var text = Convert.ToString(secretValue, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(text))
                {
                    result = result.Replace(text, "***", StringComparison.Ordinal);
                }
            }
        }

        return result;
    }

    public IReadOnlyDictionary<string, object?> RedactDictionary(IReadOnlyDictionary<string, object?> values)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (_secretNames.Contains(key))
            {
                result[key] = "***";
            }
            else
            {
                result[key] = value;
            }
        }

        return result;
    }
}

internal static class RunIdGenerator
{
    public static string NextRunId(string root, string prefix)
    {
        while (true)
        {
            var id = $"{prefix}{Guid.NewGuid():N}";
            var path = Path.Combine(root, id);
            if (!Directory.Exists(path))
            {
                return id;
            }
        }
    }
}

internal static class EngineVersion
{
    public const string Current = "MVP";
}
