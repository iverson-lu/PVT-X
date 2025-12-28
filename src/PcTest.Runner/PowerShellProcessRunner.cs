using System.Diagnostics;
using System.Text;

namespace PcTest.Runner;

public sealed class PowerShellProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(ProcessStartInfo startInfo, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        process.Exited += (_, _) => tcs.TrySetResult(process.ExitCode);

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts?.Token ?? CancellationToken.None);

        Task completed = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.InfiniteTimeSpan, linked.Token)).ConfigureAwait(false);
        bool timedOut = timeoutCts is not null && timeoutCts.IsCancellationRequested;
        if (completed != tcs.Task)
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
                // ignore kill exceptions
            }

            return new ProcessRunResult(-1, stdout.ToString(), stderr.ToString(), timedOut);
        }

        int exitCode = await tcs.Task.ConfigureAwait(false);
        return new ProcessRunResult(exitCode, stdout.ToString(), stderr.ToString(), timedOut);
    }
}
