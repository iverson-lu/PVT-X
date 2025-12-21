using System.Diagnostics;
using PcTest.Runner.Diagnostics;

namespace PcTest.Runner.Process;

/// <summary>
/// Default implementation for launching external processes with timeout and output capture.
/// </summary>
public class ProcessInvoker : IProcessInvoker
{
    private readonly IEventSink _events;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessInvoker"/> class.
    /// </summary>
    /// <param name="events">Event sink for lifecycle diagnostics.</param>
    public ProcessInvoker(IEventSink? events = null)
    {
        _events = events ?? new NullEventSink();
    }

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(ProcessStartRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        using var process = CreateProcess(request);
        using var stdoutWriter = CreateMirrorWriter(request.Stdout);
        using var stderrWriter = CreateMirrorWriter(request.Stderr);

        var stdoutBuilder = request.CaptureOutput ? new StringWriter() : null;
        var stderrBuilder = request.CaptureOutput ? new StringWriter() : null;

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{request.FileName}'.");
        }

        _events.Info("process.start", $"Started '{request.FileName}' (PID {process.Id})", new { request.Arguments, request.WorkingDirectory });

        var stdoutTask = PumpAsync(process.StandardOutput, stdoutWriter, stdoutBuilder, cancellationToken);
        var stderrTask = PumpAsync(process.StandardError, stderrWriter, stderrBuilder, cancellationToken);

        var exitTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(request.Timeout, cancellationToken);

        var completed = await Task.WhenAny(exitTask, timeoutTask);
        var timedOut = completed == timeoutTask;

        if (timedOut && !process.HasExited)
        {
            _events.Warn("process.timeout", "Timeout reached, terminating process tree.");
            TryKillProcessTree(process);
        }

        if (!process.HasExited)
        {
            await process.WaitForExitAsync(cancellationToken);
        }

        await Task.WhenAll(stdoutTask, stderrTask);

        return new ProcessResult(
            process.ExitCode,
            timedOut,
            stdoutBuilder?.ToString() ?? string.Empty,
            stderrBuilder?.ToString() ?? string.Empty);
    }

    private static void ValidateRequest(ProcessStartRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ArgumentException("FileName must be provided", nameof(request));
        }

        if (request.Arguments is null)
        {
            throw new ArgumentException("Arguments must be provided", nameof(request));
        }

        if (request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Timeout must be positive.");
        }
    }

    private static System.Diagnostics.Process CreateProcess(ProcessStartRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = Path.GetFullPath(request.WorkingDirectory);
        }

        foreach (var arg in request.Arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (request.Environment is not null)
        {
            foreach (var kvp in request.Environment)
            {
                startInfo.Environment[kvp.Key] = kvp.Value;
            }
        }

        return new System.Diagnostics.Process { StartInfo = startInfo, EnableRaisingEvents = true };
    }

    private static async Task PumpAsync(StreamReader reader, StreamWriter? mirror, StringWriter? capture, CancellationToken token)
    {
        var buffer = new char[1024];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
        {
            if (mirror != null)
            {
                await mirror.WriteAsync(buffer.AsMemory(0, read));
                await mirror.FlushAsync();
            }

            if (capture != null)
            {
                await capture.WriteAsync(buffer.AsMemory(0, read));
            }
        }
    }

    private static StreamWriter? CreateMirrorWriter(Stream? target)
    {
        return target == null ? null : new StreamWriter(target, leaveOpen: true) { AutoFlush = true };
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
                    ArgumentList = { "/PID", process.Id.ToString(), "/T", "/F" },
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
}
