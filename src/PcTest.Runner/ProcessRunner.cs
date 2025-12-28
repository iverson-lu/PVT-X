using System.Diagnostics;

namespace PcTest.Runner;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(ProcessStartInfo startInfo, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout.HasValue)
        {
            cts.CancelAfter(timeout.Value);
        }

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdout.WriteLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stderr.WriteLine(args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeout.HasValue && !cancellationToken.IsCancellationRequested;
            try
            {
                process.Kill(true);
            }
            catch (Exception)
            {
            }
        }

        return new ProcessRunResult
        {
            ExitCode = process.HasExited ? process.ExitCode : -1,
            TimedOut = timedOut,
            Aborted = cancellationToken.IsCancellationRequested,
            StandardOutput = stdout.ToString(),
            StandardError = stderr.ToString(),
            ErrorMessage = timedOut ? "Timeout" : null
        };
    }
}
