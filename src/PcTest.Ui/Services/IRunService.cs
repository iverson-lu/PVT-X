using PcTest.Contracts;
using PcTest.Contracts.Requests;
using PcTest.Contracts.Results;

namespace PcTest.Ui.Services;

/// <summary>
/// Service for executing test runs.
/// </summary>
public interface IRunService
{
    /// <summary>
    /// Executes a run request.
    /// </summary>
    Task<RunExecutionContext> ExecuteAsync(RunRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stops an ongoing run.
    /// </summary>
    Task StopAsync(string runId);
    
    /// <summary>
    /// Aborts an ongoing run immediately.
    /// </summary>
    Task AbortAsync(string runId);
    
    /// <summary>
    /// Gets the current execution state.
    /// </summary>
    RunExecutionState? CurrentState { get; }
    
    /// <summary>
    /// Event raised when execution state changes.
    /// </summary>
    event EventHandler<RunExecutionState>? StateChanged;
    
    /// <summary>
    /// Event raised when console output is received.
    /// </summary>
    event EventHandler<string>? ConsoleOutput;
}

/// <summary>
/// Execution context for a run.
/// </summary>
public sealed class RunExecutionContext
{
    public string RunId { get; set; } = string.Empty;
    public RunType RunType { get; set; }
    public string TargetIdentity { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public CancellationTokenSource CancellationSource { get; set; } = new();
}

/// <summary>
/// State of execution for UI updates.
/// </summary>
public sealed class RunExecutionState
{
    public string RunId { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
    public string CurrentNodeId { get; set; } = string.Empty;
    public List<NodeExecutionState> Nodes { get; set; } = new();
    public RunStatus? FinalStatus { get; set; }
}

/// <summary>
/// Execution state for a single node.
/// </summary>
public sealed class NodeExecutionState
{
    public string NodeId { get; set; } = string.Empty;
    public RunType NodeType { get; set; }
    public string TestId { get; set; } = string.Empty;
    public string TestVersion { get; set; } = string.Empty;
    public string? TestName { get; set; }
    public string? SuiteName { get; set; }
    public string? PlanName { get; set; }
    public string? ReferenceName { get; set; }
    public RunStatus? Status { get; set; }
    public TimeSpan? Duration { get; set; }
    public int RetryCount { get; set; }
    public bool IsRunning { get; set; }
    public string? ParentNodeId { get; set; } // For test cases under a suite in plan execution
    public int IterationIndex { get; set; }
    public int SequenceIndex { get; set; }
}
