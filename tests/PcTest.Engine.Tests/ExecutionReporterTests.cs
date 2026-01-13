using PcTest.Contracts;
using PcTest.Engine.Execution;
using Xunit;

namespace PcTest.Engine.Tests;

/// <summary>
/// Unit tests for the IExecutionReporter implementation and event flow.
/// </summary>
public class ExecutionReporterTests
{
    private class TestReporter : IExecutionReporter
    {
        public List<string> Events { get; } = new();
        public List<PlannedNode> PlannedNodes { get; private set; } = new();
        public string? LastRunId { get; private set; }
        public RunStatus? LastFinalStatus { get; private set; }

        public void OnRunPlanned(string runId, RunType runType, IReadOnlyList<PlannedNode> plannedNodes)
        {
            LastRunId = runId;
            PlannedNodes = plannedNodes.ToList();
            Events.Add($"OnRunPlanned:{runId}:{runType}:{plannedNodes.Count}");
        }

        public void OnNodeStarted(string runId, string nodeId)
        {
            Events.Add($"OnNodeStarted:{runId}:{nodeId}");
        }

        public void OnNodeFinished(string runId, NodeFinishedState nodeState)
        {
            Events.Add($"OnNodeFinished:{runId}:{nodeState.NodeId}:{nodeState.Status}");
        }

        public void OnRunFinished(string runId, RunStatus finalStatus)
        {
            LastFinalStatus = finalStatus;
            Events.Add($"OnRunFinished:{runId}:{finalStatus}");
        }
    }

    [Fact]
    public void NullExecutionReporter_DoesNotThrow()
    {
        // Arrange
        var reporter = NullExecutionReporter.Instance;

        // Act & Assert - should not throw
        reporter.OnRunPlanned("run-1", RunType.TestCase, new List<PlannedNode>());
        reporter.OnNodeStarted("run-1", "node-1");
        reporter.OnNodeFinished("run-1", new NodeFinishedState
        {
            NodeId = "node-1",
            Status = RunStatus.Passed,
            StartTime = DateTime.Now,
            EndTime = DateTime.Now
        });
        reporter.OnRunFinished("run-1", RunStatus.Passed);
    }

    [Fact]
    public void PlannedNode_Properties_AreSetCorrectly()
    {
        // Arrange & Act
        var node = new PlannedNode
        {
            NodeId = "node-1",
            TestId = "test-id",
            TestVersion = "1.0.0",
            NodeType = RunType.TestCase
        };

        // Assert
        Assert.Equal("node-1", node.NodeId);
        Assert.Equal("test-id", node.TestId);
        Assert.Equal("1.0.0", node.TestVersion);
        Assert.Equal(RunType.TestCase, node.NodeType);
    }

    [Fact]
    public void NodeFinishedState_Duration_IsCalculatedCorrectly()
    {
        // Arrange
        var startTime = new DateTime(2025, 1, 1, 10, 0, 0);
        var endTime = new DateTime(2025, 1, 1, 10, 0, 5);

        // Act
        var state = new NodeFinishedState
        {
            NodeId = "node-1",
            Status = RunStatus.Passed,
            StartTime = startTime,
            EndTime = endTime
        };

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(5), state.Duration);
    }

    [Fact]
    public void TestReporter_RecordsEventOrder()
    {
        // Arrange
        var reporter = new TestReporter();
        var runId = "test-run-1";
        var plannedNodes = new List<PlannedNode>
        {
            new PlannedNode { NodeId = "node-1", TestId = "test-1", TestVersion = "1.0" },
            new PlannedNode { NodeId = "node-2", TestId = "test-2", TestVersion = "1.0" }
        };

        // Act - simulate execution flow
        reporter.OnRunPlanned(runId, RunType.TestSuite, plannedNodes);
        reporter.OnNodeStarted(runId, "node-1");
        reporter.OnNodeFinished(runId, new NodeFinishedState
        {
            NodeId = "node-1",
            Status = RunStatus.Passed,
            StartTime = DateTime.Now,
            EndTime = DateTime.Now
        });
        reporter.OnNodeStarted(runId, "node-2");
        reporter.OnNodeFinished(runId, new NodeFinishedState
        {
            NodeId = "node-2",
            Status = RunStatus.Failed,
            StartTime = DateTime.Now,
            EndTime = DateTime.Now
        });
        reporter.OnRunFinished(runId, RunStatus.Failed);

        // Assert
        Assert.Equal(6, reporter.Events.Count);
        Assert.StartsWith("OnRunPlanned:", reporter.Events[0]);
        Assert.Contains("OnNodeStarted:test-run-1:node-1", reporter.Events[1]);
        Assert.Contains("OnNodeFinished:test-run-1:node-1:Passed", reporter.Events[2]);
        Assert.Contains("OnNodeStarted:test-run-1:node-2", reporter.Events[3]);
        Assert.Contains("OnNodeFinished:test-run-1:node-2:Failed", reporter.Events[4]);
        Assert.Contains("OnRunFinished:test-run-1:Failed", reporter.Events[5]);

        Assert.Equal(runId, reporter.LastRunId);
        Assert.Equal(RunStatus.Failed, reporter.LastFinalStatus);
        Assert.Equal(2, reporter.PlannedNodes.Count);
    }

    [Fact]
    public void NodeFinishedState_WithMessage_RecordsMessage()
    {
        // Arrange
        var state = new NodeFinishedState
        {
            NodeId = "node-1",
            Status = RunStatus.Error,
            StartTime = DateTime.Now,
            EndTime = DateTime.Now,
            Message = "Test error message",
            RetryCount = 2
        };

        // Assert
        Assert.Equal("node-1", state.NodeId);
        Assert.Equal(RunStatus.Error, state.Status);
        Assert.Equal("Test error message", state.Message);
        Assert.Equal(2, state.RetryCount);
    }
}
