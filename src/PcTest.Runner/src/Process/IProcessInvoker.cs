namespace PcTest.Runner.Process;

/// <summary>
/// Provides a reusable abstraction for launching external processes with structured arguments.
/// </summary>
public interface IProcessInvoker
{
    /// <summary>
    /// Executes the provided process request.
    /// </summary>
    /// <param name="request">Process start request parameters.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the invocation.</param>
    /// <returns>Process execution result including exit code and captured output.</returns>
    Task<ProcessResult> RunAsync(ProcessStartRequest request, CancellationToken cancellationToken = default);
}
