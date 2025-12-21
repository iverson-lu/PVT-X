using System.Diagnostics;
using PcTest.Runner.Storage;

namespace PcTest.Runner.Process;

public class PowerShellRunner
{
    public async Task<PowerShellRunResult> RunAsync(
        string pwshPath,
        string scriptPath,
        IEnumerable<string> parameterArguments,
        string workingDirectory,
        TimeSpan timeout,
        string stdoutPath,
        string stderrPath,
        EventLogWriter events,
        CancellationToken cancellationToken)
    {
        var arguments = BuildArguments(scriptPath, parameterArguments);
        var startInfo = new ProcessStartInfo
        {
            FileName = pwshPath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new System.Diagnostics.Process { StartInfo = startInfo, EnableRaisingEvents = true };
        using var stdoutStream = File.Open(stdoutPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        using var stderrStream = File.Open(stderrPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start pwsh.exe");
        }

        events.WriteEvent("process.start", $"Started PowerShell with PID {process.Id}");

        var stdoutTask = PumpStreamAsync(process.StandardOutput, stdoutStream, cancellationToken);
        var stderrTask = PumpStreamAsync(process.StandardError, stderrStream, cancellationToken);

        var waitTask = process.WaitForExitAsync(cancellationToken);
        var delayTask = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(waitTask, delayTask);
        var timedOut = completed == delayTask;

        if (timedOut && !process.HasExited)
        {
            events.WriteEvent("process.timeout", "Timeout reached, terminating process tree.");
            TryKillProcessTree(process);
        }

        if (!process.HasExited)
        {
            await process.WaitForExitAsync(cancellationToken);
        }

        await Task.WhenAll(stdoutTask, stderrTask);

        return new PowerShellRunResult(process.ExitCode, timedOut);
    }

    private static string BuildArguments(string scriptPath, IEnumerable<string> parameterArguments)
    {
        var args = new List<string>
        {
            "-NoLogo",
            "-NoProfile",
            "-ExecutionPolicy Bypass",
            "-File",
            Quote(scriptPath)
        };

        args.AddRange(parameterArguments);
        return string.Join(' ', args);
    }

    private static async Task PumpStreamAsync(StreamReader reader, Stream output, CancellationToken token)
    {
        var buffer = new char[1024];
        using var writer = new StreamWriter(output, leaveOpen: true);
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
        {
            await writer.WriteAsync(buffer.AsMemory(0, read), token);
            await writer.FlushAsync(token);
        }
    }

    private static void TryKillProcessTree(System.Diagnostics.Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                System.Diagnostics.Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/PID {process.Id} /T /F",
                    CreateNoWindow = true,
                    UseShellExecute = false
                })?.WaitForExit(5000);
            }
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // ignored
            }
        }
    }

    private static string Quote(string value)
    {
        return value.Contains(' ')
            ? $"\"{value}\""
            : value;
    }
}

public record PowerShellRunResult(int ExitCode, bool TimedOut);
