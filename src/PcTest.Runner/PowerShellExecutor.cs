using System.Diagnostics;

namespace PcTest.Runner;

public sealed record PowerShellExecutionResult(int? ExitCode, string Stdout, string Stderr, bool TimedOut, bool Aborted);

public interface IPowerShellExecutor
{
    Task<PowerShellExecutionResult> ExecuteAsync(PowerShellExecutionRequest request, CancellationToken cancellationToken);
}

public sealed record PowerShellExecutionRequest(
    string PwshPath,
    string ScriptPath,
    string WorkingDirectory,
    IReadOnlyList<string> ArgumentList,
    Dictionary<string, string> Environment,
    TimeSpan? Timeout);

public sealed class PowerShellExecutor : IPowerShellExecutor
{
    public async Task<PowerShellExecutionResult> ExecuteAsync(PowerShellExecutionRequest request, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = request.PwshPath,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var arg in request.ArgumentList)
        {
            psi.ArgumentList.Add(arg);
        }

        foreach (var (key, value) in request.Environment)
        {
            psi.Environment[key] = value;
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Start();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (request.Timeout.HasValue)
        {
            cts.CancelAfter(request.Timeout.Value);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore kill failures
            }

            var stdout = await SafeRead(stdoutTask);
            var stderr = await SafeRead(stderrTask);
            return new PowerShellExecutionResult(null, stdout, stderr, request.Timeout.HasValue, !request.Timeout.HasValue);
        }

        var stdoutResult = await stdoutTask;
        var stderrResult = await stderrTask;
        return new PowerShellExecutionResult(process.ExitCode, stdoutResult, stderrResult, false, false);
    }

    private static async Task<string> SafeRead(Task<string> task)
    {
        try
        {
            return await task;
        }
        catch
        {
            return string.Empty;
        }
    }
}
