using PcTest.Contracts;

namespace PcTest.Engine.Execution;

/// <summary>
/// Interface for reporting execution progress events.
/// Enables real-time UI updates during test execution.
/// </summary>
public interface IExecutionReporter
{
    /// <summary>
    /// Called when the run is planned and nodes are known.
    /// </summary>
    /// <param name="runId">The ID of the run.</param>
    /// <param name="runType">The type of run (TestCase, TestSuite, TestPlan).</param>
    /// <param name="plannedNodes">The list of planned nodes to be executed.</param>
    void OnRunPlanned(string runId, RunType runType, IReadOnlyList<PlannedNode> plannedNodes);

    /// <summary>
    /// Called when a node starts executing.
    /// </summary>
    /// <param name="runId">The ID of the run.</param>
    /// <param name="nodeId">The ID of the node starting execution.</param>
    void OnNodeStarted(string runId, string nodeId);

    /// <summary>
    /// Called when a node finishes executing.
    /// </summary>
    /// <param name="runId">The ID of the run.</param>
    /// <param name="nodeState">The final state of the node.</param>
    void OnNodeFinished(string runId, NodeFinishedState nodeState);

    /// <summary>
    /// Called when the entire run finishes.
    /// </summary>
    /// <param name="runId">The ID of the run.</param>
    /// <param name="finalStatus">The final status of the run.</param>
    void OnRunFinished(string runId, RunStatus finalStatus);
}

/// <summary>
/// Represents a planned node in the execution pipeline.
/// </summary>
public sealed class PlannedNode
{
    /// <summary>
    /// Unique node identifier within the run.
    /// </summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>
    /// Test case or suite identifier.
    /// </summary>
    public string TestId { get; init; } = string.Empty;

    /// <summary>
    /// Test case or suite version.
    /// </summary>
    public string TestVersion { get; init; } = string.Empty;

    /// <summary>
    /// Type of the node (TestCase for suite nodes, TestSuite for plan nodes).
    /// </summary>
    public RunType NodeType { get; init; }

    /// <summary>
    /// Parent node ID if this is a nested node (e.g., test case under suite in plan).
    /// </summary>
    public string? ParentNodeId { get; init; }

    /// <summary>
    /// Reference name for the test case (from suite manifest's "ref" field).
    /// Used for display instead of the raw test name.
    /// </summary>
    public string? ReferenceName { get; init; }
}

/// <summary>
/// Represents the finished state of a node.
/// </summary>
public sealed class NodeFinishedState
{
    /// <summary>
    /// The node identifier.
    /// </summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>
    /// The final status of the node.
    /// </summary>
    public RunStatus Status { get; init; }

    /// <summary>
    /// Start time of execution.
    /// </summary>
    public DateTime StartTime { get; init; }

    /// <summary>
    /// End time of execution.
    /// </summary>
    public DateTime EndTime { get; init; }

    /// <summary>
    /// Duration of execution.
    /// </summary>
    public TimeSpan Duration => EndTime - StartTime;

    /// <summary>
    /// Optional message (error message if failed).
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Retry attempt number (0 for first attempt).
    /// </summary>
    public int RetryCount { get; init; }

    /// <summary>
    /// Parent node ID if this is a nested node (e.g., test case under suite in plan).
    /// </summary>
    public string? ParentNodeId { get; init; }
}

/// <summary>
/// Null implementation of IExecutionReporter for backward compatibility.
/// </summary>
public sealed class NullExecutionReporter : IExecutionReporter
{
    public static readonly NullExecutionReporter Instance = new();

    private NullExecutionReporter() { }

    public void OnRunPlanned(string runId, RunType runType, IReadOnlyList<PlannedNode> plannedNodes) { }
    public void OnNodeStarted(string runId, string nodeId) { }
    public void OnNodeFinished(string runId, NodeFinishedState nodeState) { }
    public void OnRunFinished(string runId, RunStatus finalStatus) { }
}
