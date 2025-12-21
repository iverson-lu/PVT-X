namespace PcTest.Runner.Process;

/// <summary>
/// Handles invocation of PowerShell scripts and collection of output.
/// </summary>
public class PowerShellRunner
{
    private readonly IProcessInvoker _processInvoker;

    /// <summary>
    /// Initializes a new instance of the <see cref="PowerShellRunner"/> class.
    /// </summary>
    /// <param name="processInvoker">Process invoker used to launch PowerShell.</param>
    public PowerShellRunner(IProcessInvoker? processInvoker = null)
    {
        _processInvoker = processInvoker ?? new ProcessInvoker();
    }

    /// <summary>
    /// Executes a PowerShell script with the provided arguments and captures output streams.
    /// </summary>
    /// <param name="pwshPath">Path to the PowerShell executable.</param>
    /// <param name="scriptPath">Path to the script to run.</param>
    /// <param name="parameterArguments">Formatted parameter arguments.</param>
    /// <param name="workingDirectory">Working directory for the process.</param>
    /// <param name="timeout">Maximum allowed execution time.</param>
    /// <param name="stdout">Stream to capture standard output.</param>
    /// <param name="stderr">Stream to capture standard error.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Exit code and timeout information for the run.</returns>
    public async Task<PowerShellRunResult> RunAsync(
        string pwshPath,
        string scriptPath,
        IEnumerable<string> parameterArguments,
        string workingDirectory,
        TimeSpan timeout,
        Stream stdout,
        Stream stderr,
        CancellationToken cancellationToken)
    {
        var arguments = BuildArguments(scriptPath, parameterArguments).ToArray();
        var request = new ProcessStartRequest
        {
            FileName = pwshPath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            Timeout = timeout,
            Stdout = stdout,
            Stderr = stderr
        };

        var result = await _processInvoker.RunAsync(request, cancellationToken);
        return new PowerShellRunResult(result.ExitCode, result.TimedOut);
    }

    private static IEnumerable<string> BuildArguments(string scriptPath, IEnumerable<string> parameterArguments)
    {
        yield return "-NoLogo";
        yield return "-NoProfile";
        yield return "-ExecutionPolicy";
        yield return "Bypass";
        yield return "-File";
        yield return scriptPath;

        foreach (var argument in parameterArguments)
        {
            yield return argument;
        }
    }
}

/// <summary>
/// Represents the outcome of a PowerShell invocation.
/// </summary>
/// <param name="ExitCode">Process exit code.</param>
/// <param name="TimedOut">Indicates whether the process exceeded the timeout.</param>
public record PowerShellRunResult(int ExitCode, bool TimedOut);
