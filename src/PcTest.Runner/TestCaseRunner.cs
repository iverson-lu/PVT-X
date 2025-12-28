using System.Diagnostics;
using System.Globalization;
using System.Collections;
using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Runner;

public sealed class TestCaseRunner
{
    private readonly IProcessRunner _processRunner;
    private readonly IRunIdGenerator _runIdGenerator;

    public TestCaseRunner(IProcessRunner processRunner, IRunIdGenerator runIdGenerator)
    {
        _processRunner = processRunner;
        _runIdGenerator = runIdGenerator;
    }

    public async Task<CaseResult> RunAsync(CaseRunContext context, CancellationToken cancellationToken)
    {
        var runId = _runIdGenerator.NewRunId();
        var runFolder = Path.Combine(context.RunsRoot, runId);
        Directory.CreateDirectory(runFolder);

        var eventsPath = Path.Combine(runFolder, "events.jsonl");
        var stdoutPath = Path.Combine(runFolder, "stdout.txt");
        var stderrPath = Path.Combine(runFolder, "stderr.txt");
        var manifestPath = Path.Combine(runFolder, "manifest.json");
        var paramsPath = Path.Combine(runFolder, "params.json");
        var envPath = Path.Combine(runFolder, "env.json");
        var resultPath = Path.Combine(runFolder, "result.json");

        var started = DateTimeOffset.UtcNow;
        var events = new List<EngineEvent>();

        var secretValues = context.SecretInputValues.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
        var redactedInputs = RedactInputs(context.EffectiveInputs, secretValues);
        var redactedEnv = RedactEnvironment(context.EffectiveEnvironment, context.SecretEnvironmentKeys);

        // Assumption: runner emits events.jsonl as EngineEvent lines for warnings/errors.
        var warningEvents = new List<EngineEvent>();
        foreach (var secretKey in context.SecretInputValues.Keys)
        {
            warningEvents.Add(new EngineEvent
            {
                TimestampUtc = started,
                Level = "Warning",
                Code = "EnvRef.SecretOnCommandLine",
                Message = "Secret value passed on command line.",
                Data = new Dictionary<string, string>
                {
                    ["parameter"] = secretKey,
                    ["nodeId"] = context.NodeId ?? string.Empty
                }
            });
        }

        events.AddRange(warningEvents);

        var workingDir = ResolveWorkingDirectory(runFolder, context.WorkingDir, out var workingDirError);
        if (workingDirError is not null)
        {
            var errorResult = CreateErrorResult(runId, started, context, workingDirError, ErrorType.RunnerError);
            JsonParsing.WriteDeterministic(resultPath, errorResult);
            WriteEvents(eventsPath, events);
            JsonParsing.WriteDeterministic(manifestPath, new CaseManifestSnapshot
            {
                SourceManifest = context.TestCaseManifest,
                ResolvedRef = context.ResolvedRef,
                ResolvedIdentity = context.TestCaseManifest.Identity.ToString(),
                EffectiveEnvironment = redactedEnv,
                EffectiveInputs = redactedInputs,
                ResolvedAtUtc = started
            });
            JsonParsing.WriteDeterministic(paramsPath, new ParameterSnapshot { Inputs = redactedInputs });
            JsonParsing.WriteDeterministic(envPath, redactedEnv);
            File.WriteAllText(stdoutPath, string.Empty);
            File.WriteAllText(stderrPath, string.Empty);
            return errorResult;
        }

        var validationError = ValidatePathInputs(context, runFolder, workingDir);
        if (validationError is not null)
        {
            var errorResult = CreateErrorResult(runId, started, context, validationError, ErrorType.RunnerError);
            JsonParsing.WriteDeterministic(resultPath, errorResult);
            WriteEvents(eventsPath, events);
            JsonParsing.WriteDeterministic(manifestPath, new CaseManifestSnapshot
            {
                SourceManifest = context.TestCaseManifest,
                ResolvedRef = context.ResolvedRef,
                ResolvedIdentity = context.TestCaseManifest.Identity.ToString(),
                EffectiveEnvironment = redactedEnv,
                EffectiveInputs = redactedInputs,
                ResolvedAtUtc = started
            });
            JsonParsing.WriteDeterministic(paramsPath, new ParameterSnapshot { Inputs = redactedInputs });
            JsonParsing.WriteDeterministic(envPath, redactedEnv);
            File.WriteAllText(stdoutPath, string.Empty);
            File.WriteAllText(stderrPath, string.Empty);
            return errorResult;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = context.PowerShellPath,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(context.ScriptPath);

        foreach (var parameter in context.EffectiveInputs)
        {
            if (!context.ParameterDefinitions.TryGetValue(parameter.Key, out var definition))
            {
                continue;
            }

            if (!ParameterTypeHelper.TryParse(definition.Type, out var paramType))
            {
                paramType = ParameterType.String;
            }

            if (parameter.Value is null)
            {
                continue;
            }

            startInfo.ArgumentList.Add($"-{parameter.Key}");
            foreach (var arg in FlattenParameter(paramType, parameter.Value))
            {
                startInfo.ArgumentList.Add(arg);
            }
        }

        foreach (var kvp in context.EffectiveEnvironment)
        {
            startInfo.Environment[kvp.Key] = kvp.Value;
        }

        ProcessRunResult processResult;
        try
        {
            processResult = await _processRunner.RunAsync(startInfo, context.Timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var errorResult = CreateErrorResult(runId, started, context, ex.Message, ErrorType.RunnerError);
            JsonParsing.WriteDeterministic(resultPath, errorResult);
            WriteEvents(eventsPath, events);
            JsonParsing.WriteDeterministic(manifestPath, new CaseManifestSnapshot
            {
                SourceManifest = context.TestCaseManifest,
                ResolvedRef = context.ResolvedRef,
                ResolvedIdentity = context.TestCaseManifest.Identity.ToString(),
                EffectiveEnvironment = redactedEnv,
                EffectiveInputs = redactedInputs,
                ResolvedAtUtc = started
            });
            JsonParsing.WriteDeterministic(paramsPath, new ParameterSnapshot { Inputs = redactedInputs });
            JsonParsing.WriteDeterministic(envPath, redactedEnv);
            File.WriteAllText(stdoutPath, string.Empty);
            File.WriteAllText(stderrPath, string.Empty);
            return errorResult;
        }

        var finished = DateTimeOffset.UtcNow;
        var status = MapStatus(processResult);
        var error = status is ResultStatus.Error
            ? new RunnerError { Type = processResult.TimedOut ? ErrorType.RunnerError : ErrorType.ScriptError, Message = processResult.ErrorMessage ?? "Error" }
            : null;

        var result = new CaseResult
        {
            RunId = runId,
            Status = status,
            StartTimeUtc = started,
            EndTimeUtc = finished,
            DurationSec = (finished - started).TotalSeconds,
            NodeId = context.NodeId,
            SuiteId = context.SuiteId,
            PlanId = context.PlanId,
            Error = error
        };

        JsonParsing.WriteDeterministic(manifestPath, new CaseManifestSnapshot
        {
            SourceManifest = context.TestCaseManifest,
            ResolvedRef = context.ResolvedRef,
            ResolvedIdentity = context.TestCaseManifest.Identity.ToString(),
            EffectiveEnvironment = redactedEnv,
            EffectiveInputs = redactedInputs,
            ResolvedAtUtc = started
        });
        JsonParsing.WriteDeterministic(paramsPath, new ParameterSnapshot { Inputs = redactedInputs });
        JsonParsing.WriteDeterministic(envPath, redactedEnv);
        File.WriteAllText(stdoutPath, RedactText(processResult.StandardOutput, secretValues.Values.SelectMany(values => values)));
        File.WriteAllText(stderrPath, RedactText(processResult.StandardError, secretValues.Values.SelectMany(values => values)));
        WriteEvents(eventsPath, events);
        JsonParsing.WriteDeterministic(resultPath, result);

        return result;
    }

    private static string ResolveWorkingDirectory(string caseRunFolder, string? workingDir, out string? errorMessage)
    {
        errorMessage = null;
        var baseDir = caseRunFolder;
        var candidate = workingDir is null ? baseDir : Path.Combine(baseDir, workingDir);
        var resolved = PathUtils.NormalizePath(candidate);
        resolved = PathUtils.ResolveFinalDirectory(resolved);
        if (!PathUtils.IsContained(baseDir, resolved))
        {
            errorMessage = "Working directory escapes run folder.";
            return baseDir;
        }

        Directory.CreateDirectory(resolved);
        return resolved;
    }

    private static string? ValidatePathInputs(CaseRunContext context, string runFolder, string workingDir)
    {
        foreach (var parameter in context.EffectiveInputs)
        {
            if (!context.ParameterDefinitions.TryGetValue(parameter.Key, out var definition))
            {
                continue;
            }

            if (!ParameterTypeHelper.TryParse(definition.Type, out var paramType))
            {
                continue;
            }

            if (!ParameterTypeHelper.IsPath(paramType))
            {
                continue;
            }

            if (parameter.Value is null)
            {
                continue;
            }

            IEnumerable<string> values = paramType switch
            {
                ParameterType.Path or ParameterType.File or ParameterType.Folder => new[] { Convert.ToString(parameter.Value, CultureInfo.InvariantCulture) ?? string.Empty },
                ParameterType.PathArray or ParameterType.FileArray or ParameterType.FolderArray => (IEnumerable<string>)parameter.Value,
                _ => Array.Empty<string>()
            };

            foreach (var value in values)
            {
                var resolved = Path.IsPathRooted(value) ? value : Path.Combine(workingDir, value);
                var normalized = PathUtils.NormalizePath(resolved);
                if (!PathUtils.IsContained(runFolder, normalized))
                {
                    return "Path input escapes run folder.";
                }

                if (paramType is ParameterType.File or ParameterType.FileArray && !File.Exists(normalized))
                {
                    return "File input does not exist.";
                }

                if (paramType is ParameterType.Folder or ParameterType.FolderArray && !Directory.Exists(normalized))
                {
                    return "Folder input does not exist.";
                }
            }
        }

        return null;
    }

    private static CaseResult CreateErrorResult(string runId, DateTimeOffset started, CaseRunContext context, string message, ErrorType errorType)
    {
        var finished = DateTimeOffset.UtcNow;
        return new CaseResult
        {
            RunId = runId,
            Status = ResultStatus.Error,
            StartTimeUtc = started,
            EndTimeUtc = finished,
            DurationSec = (finished - started).TotalSeconds,
            NodeId = context.NodeId,
            SuiteId = context.SuiteId,
            PlanId = context.PlanId,
            Error = new RunnerError { Type = errorType, Message = message }
        };
    }

    private static ResultStatus MapStatus(ProcessRunResult result)
    {
        if (result.Aborted)
        {
            return ResultStatus.Aborted;
        }

        if (result.TimedOut)
        {
            return ResultStatus.Timeout;
        }

        return result.ExitCode switch
        {
            0 => ResultStatus.Passed,
            1 => ResultStatus.Failed,
            _ => ResultStatus.Error
        };
    }

    private static IEnumerable<string> FlattenParameter(ParameterType type, object value)
    {
        if (type == ParameterType.Boolean)
        {
            return new[] { ((bool)value) ? "$true" : "$false" };
        }

        if (ParameterTypeHelper.IsArray(type))
        {
            if (value is IEnumerable enumerable)
            {
                var values = new List<string>();
                foreach (var item in enumerable)
                {
                    values.Add(Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty);
                }

                return values;
            }
        }

        return new[] { Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty };
    }

    private static Dictionary<string, object> RedactInputs(Dictionary<string, object> inputs, Dictionary<string, IReadOnlyList<string>> secretValues)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var kvp in inputs)
        {
            if (secretValues.ContainsKey(kvp.Key))
            {
                result[kvp.Key] = "***";
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }

        return result;
    }

    private static Dictionary<string, string> RedactEnvironment(Dictionary<string, string> environment, HashSet<string> secretKeys)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kvp in environment)
        {
            result[kvp.Key] = secretKeys.Contains(kvp.Key) ? "***" : kvp.Value;
        }

        return result;
    }

    private static string RedactText(string value, IEnumerable<string> secrets)
    {
        var result = value;
        foreach (var secret in secrets)
        {
            if (!string.IsNullOrEmpty(secret))
            {
                result = result.Replace(secret, "***", StringComparison.Ordinal);
            }
        }

        return result;
    }

    private static void WriteEvents(string path, IEnumerable<EngineEvent> events)
    {
        using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream);
        foreach (var evt in events)
        {
            var json = JsonSerializer.Serialize(evt, JsonParsing.SerializerOptions);
            writer.WriteLine(json);
        }
    }
}

public sealed record CaseRunContext
{
    public required string RunsRoot { get; init; }

    public required string PowerShellPath { get; init; }

    public required string ScriptPath { get; init; }

    public required TestCaseManifest TestCaseManifest { get; init; }

    public required string ResolvedRef { get; init; }

    public required Dictionary<string, object> EffectiveInputs { get; init; }

    public required Dictionary<string, string> EffectiveEnvironment { get; init; }

    public required Dictionary<string, IReadOnlyList<string>> SecretInputValues { get; init; }

    public required HashSet<string> SecretEnvironmentKeys { get; init; }

    public required Dictionary<string, ParameterDefinition> ParameterDefinitions { get; init; }

    public string? WorkingDir { get; init; }

    public string? NodeId { get; init; }

    public string? SuiteId { get; init; }

    public string? PlanId { get; init; }

    public TimeSpan? Timeout { get; init; }
}

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(ProcessStartInfo startInfo, TimeSpan? timeout, CancellationToken cancellationToken);
}

public sealed record ProcessRunResult
{
    public required int ExitCode { get; init; }

    public required bool TimedOut { get; init; }

    public required bool Aborted { get; init; }

    public required string StandardOutput { get; init; }

    public required string StandardError { get; init; }

    public string? ErrorMessage { get; init; }
}

public interface IRunIdGenerator
{
    string NewRunId();
}

public sealed class GuidRunIdGenerator : IRunIdGenerator
{
    public string NewRunId() => Guid.NewGuid().ToString("N");
}

public sealed record CaseManifestSnapshot
{
    public required TestCaseManifest SourceManifest { get; init; }

    public required string ResolvedRef { get; init; }

    public required string ResolvedIdentity { get; init; }

    public required Dictionary<string, string> EffectiveEnvironment { get; init; }

    public required Dictionary<string, object> EffectiveInputs { get; init; }

    public required DateTimeOffset ResolvedAtUtc { get; init; }
}

public sealed record ParameterSnapshot
{
    public required Dictionary<string, object> Inputs { get; init; }
}
