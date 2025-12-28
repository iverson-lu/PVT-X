namespace PcTest.Runner;

public sealed class ProcessRunResult
{
    public ProcessRunResult(int exitCode, string stdout, string stderr, bool timedOut)
    {
        ExitCode = exitCode;
        Stdout = stdout;
        Stderr = stderr;
        TimedOut = timedOut;
    }

    public int ExitCode { get; }
    public string Stdout { get; }
    public string Stderr { get; }
    public bool TimedOut { get; }
}

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(ProcessStartInfo startInfo, TimeSpan? timeout, CancellationToken cancellationToken);
}
