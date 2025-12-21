using System.Text.Json;
using PcTest.Contracts.Manifest;
using PcTest.Contracts.Result;
using PcTest.Contracts.Serialization;
using PcTest.Contracts.Schema;
using PcTest.Runner.Diagnostics;
using PcTest.Runner.Process;
using PcTest.Runner.Security;
using PcTest.Runner.Storage;

namespace PcTest.Runner.Execution;

/// <summary>
/// Executes PowerShell-based tests and collects results.
/// </summary>
public class TestRunner
{
    private readonly PowerShellLocator _powerShellLocator = new();
    private readonly RunFolderManager _runFolderManager = new();

    /// <summary>
    /// Runs the provided test request and returns the result and run folder path.
    /// </summary>
    /// <param name="request">Execution request containing manifest, script path, and parameters.</param>
    /// <param name="cancellationToken">Optional cancellation token for the run.</param>
    /// <returns>The run folder and structured test result.</returns>
    public async Task<TestRunResponse> RunAsync(TestRunRequest request, CancellationToken cancellationToken = default)
    {
        var manifest = request.Manifest;
        var powerShell = _powerShellLocator.Locate();
        var timeout = TimeSpan.FromSeconds(manifest.TimeoutSec ?? 300);

        var runContext = _runFolderManager.Create(manifest, request.Parameters, request.RunsRoot);
        using IEventSink events = new EventLogSink(runContext.EventsPath, echoToConsole: true);
        var processInvoker = new ProcessInvoker(events);
        var powerShellRunner = new PowerShellRunner(processInvoker);

        var startTime = DateTimeOffset.UtcNow;
        events.Info("run.start", $"Starting test {manifest.Id}");

        var environment = BuildEnvironmentSnapshot(powerShell);
        File.WriteAllText(runContext.EnvPath, JsonSerializer.Serialize(environment, JsonDefaults.Options));

        PrivilegeEnforcer.EnsureAllowed(manifest.Privilege, events);

        var parameterArguments = BuildParameterArguments(request.Parameters);
        PowerShellRunResult processResult;
        try
        {
            using var stdout = File.Open(runContext.StdoutPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            using var stderr = File.Open(runContext.StderrPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);

            processResult = await powerShellRunner.RunAsync(
                powerShell.Path,
                request.ScriptPath,
                parameterArguments,
                runContext.RunFolder,
                timeout,
                stdout,
                stderr,
                cancellationToken);
        }
        catch (Exception ex)
        {
            events.Error("run.error", "Runner encountered an exception.", new { ex.Message, ex.StackTrace });
            var errorResult = BuildRunnerError(manifest, startTime, ex, powerShell);
            WriteResult(runContext, request.RunsRoot, manifest, startTime, DateTimeOffset.UtcNow, errorResult);
            return new TestRunResponse(runContext.RunFolder, errorResult);
        }

        var endTime = DateTimeOffset.UtcNow;
        var result = BuildResult(manifest, processResult, startTime, endTime, powerShell);
        events.Info("run.complete", $"Completed with status {result.Status}");

        WriteResult(runContext, request.RunsRoot, manifest, startTime, endTime, result);

        return new TestRunResponse(runContext.RunFolder, result);
    }

    private static IEnumerable<string> BuildParameterArguments(IReadOnlyDictionary<string, BoundParameterValue> parameters)
    {
        foreach (var kvp in parameters)
        {
            var bound = kvp.Value;
            if (!bound.IsSupplied)
            {
                continue;
            }

            var value = bound.Value;
            var def = bound.Definition;
            if (value is null)
            {
                continue;
            }

            yield return $"-{def.Name}";
            yield return FormatValue(value);
        }
    }

    private static string FormatValue(object value)
    {
        return value switch
        {
            bool b => b ? "$true" : "$false",
            string s => QuoteString(s),
            IEnumerable<string> arr => "@(" + string.Join(",", arr.Select(QuoteString)) + ")",
            IEnumerable<int> ints => "@(" + string.Join(",", ints) + ")",
            Array array => "@(" + string.Join(",", array.Cast<object>().Select(FormatValue)) + ")",
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string QuoteString(string value)
    {
        var escaped = value.Replace("'", "''");
        return $"'{escaped}'";
    }

    private EnvironmentSnapshot BuildEnvironmentSnapshot(PowerShellInfo powerShell)
    {
        return new EnvironmentSnapshot
        {
            OsVersion = Environment.OSVersion.ToString(),
            RunnerVersion = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            PowerShellVersion = powerShell.Version,
            IsElevated = PrivilegeEnforcer.IsElevated()
        };
    }

    private TestResult BuildRunnerError(TestManifest manifest, DateTimeOffset startTime, Exception ex, PowerShellInfo powerShell)
    {
        var end = DateTimeOffset.UtcNow;
        return new TestResult
        {
            SchemaVersion = SchemaVersionPolicy.ResultSchemaVersion(),
            TestId = manifest.Id,
            Status = TestStatus.Error,
            StartTime = startTime,
            EndTime = end,
            Message = "Runner failed before executing script.",
            Error = new ResultError
            {
                Message = ex.Message,
                Stack = ex.StackTrace,
                Source = "Runner",
                Type = "RunnerError"
            },
            Runner = new RunnerMetadata
            {
                RunnerVersion = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
                PowerShellVersion = powerShell.Version,
                MachineName = Environment.MachineName
            }
        };
    }

    private TestResult BuildResult(TestManifest manifest, PowerShellRunResult runResult, DateTimeOffset start, DateTimeOffset end, PowerShellInfo powerShell)
    {
        var status = ResolveStatus(runResult);
        var error = status switch
        {
            TestStatus.Timeout => new ResultError { Type = "Timeout", Source = "Runner", Message = "Execution timed out." },
            TestStatus.Error when runResult.ExitCode == 2 => new ResultError { Type = "ScriptError", Source = "Script", Message = "Script reported error." },
            TestStatus.Error => new ResultError { Type = "RunnerError", Source = "Runner", Message = "Unexpected error." },
            _ => null
        };

        return new TestResult
        {
            SchemaVersion = SchemaVersionPolicy.ResultSchemaVersion(),
            TestId = manifest.Id,
            Status = status,
            StartTime = start,
            EndTime = end,
            ExitCode = runResult.ExitCode,
            Error = error,
            Runner = new RunnerMetadata
            {
                RunnerVersion = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
                PowerShellVersion = powerShell.Version,
                MachineName = Environment.MachineName
            }
        };
    }

    private static TestStatus ResolveStatus(PowerShellRunResult result)
    {
        if (result.TimedOut)
        {
            return TestStatus.Timeout;
        }

        return result.ExitCode switch
        {
            0 => TestStatus.Passed,
            1 => TestStatus.Failed,
            2 => TestStatus.Error,
            3 => TestStatus.Timeout,
            _ => TestStatus.Error
        };
    }

    private void WriteResult(RunContext context, string runsRoot, TestManifest manifest, DateTimeOffset start, DateTimeOffset end, TestResult result)
    {
        File.WriteAllText(context.ResultPath, JsonSerializer.Serialize(result, JsonDefaults.Options));
        _runFolderManager.AppendIndex(runsRoot, context.RunId, manifest.Id, start, end, result.Status);
    }
}
