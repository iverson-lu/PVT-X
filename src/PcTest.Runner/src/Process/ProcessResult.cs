namespace PcTest.Runner.Process;

/// <summary>
/// Represents the outcome of a process invocation.
/// </summary>
/// <param name="ExitCode">Process exit code.</param>
/// <param name="TimedOut">Whether the process exceeded the timeout.</param>
/// <param name="StandardOutput">Captured standard output.</param>
/// <param name="StandardError">Captured standard error.</param>
public record ProcessResult(int ExitCode, bool TimedOut, string StandardOutput, string StandardError);
