using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Contracts.Requests;
using PcTest.Engine.Discovery;
using PcTest.Engine.Execution;
using Xunit;

namespace PcTest.Engine.Tests;

/// <summary>
/// Tests for Suite-level controls: repeat, continueOnFailure, retryOnError per spec section 10.
/// </summary>
public class SuiteControlsTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _testCasesRoot;
    private readonly string _testSuitesRoot;
    private readonly string _testPlansRoot;
    private readonly string _runsRoot;

    public SuiteControlsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"PcTestSuiteControls_{Guid.NewGuid():N}");
        _testCasesRoot = Path.Combine(_tempRoot, "TestCases");
        _testSuitesRoot = Path.Combine(_tempRoot, "TestSuites");
        _testPlansRoot = Path.Combine(_tempRoot, "TestPlans");
        _runsRoot = Path.Combine(_tempRoot, "Runs");
        Directory.CreateDirectory(_testCasesRoot);
        Directory.CreateDirectory(_testSuitesRoot);
        Directory.CreateDirectory(_testPlansRoot);
        Directory.CreateDirectory(_runsRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, true); } catch { /* Ignore */ }
        }
    }

    private class TestReporter : IExecutionReporter
    {
        public Dictionary<string, int> NodeExecutionCounts { get; } = new();
        public List<string> Errors { get; } = new();

        public void OnRunPlanned(string runId, RunType runType, IReadOnlyList<PlannedNode> plannedNodes) { }

        public void OnNodeStarted(string runId, string nodeId)
        {
            NodeExecutionCounts[nodeId] = NodeExecutionCounts.GetValueOrDefault(nodeId) + 1;
        }

        public void OnNodeFinished(string runId, NodeFinishedState nodeState)
        {
            if (!string.IsNullOrEmpty(nodeState.Message))
            {
                Errors.Add($"{nodeState.NodeId}: {nodeState.Message}");
            }
        }

        public void OnRunFinished(string runId, RunStatus finalStatus) { }

        public int GetExecutionCount(string nodeId) => NodeExecutionCounts.GetValueOrDefault(nodeId, 0);
    }

    private void CreatePassingTestCase(string id)
    {
        var dir = Path.Combine(_testCasesRoot, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test.manifest.json"),
            JsonSerializer.Serialize(new TestCaseManifest
            {
                Id = id,
                Version = "1.0",
                Name = $"{id} Test",
                Category = "Test"
            }, JsonDefaults.WriteOptions));
        File.WriteAllText(Path.Combine(dir, "run.ps1"),
            @"$artifactsDir = ""$PSScriptRoot\artifacts""
New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
$report = @{
    schemaVersion = '1.5.0'
    status = 'Passed'
    summary = 'Test passed'
    details = ''
}
$report | ConvertTo-Json -Depth 10 | Set-Content ""$artifactsDir\report.json"" -Encoding utf8
exit 0");
    }

    private void CreateFailingTestCase(string id)
    {
        var dir = Path.Combine(_testCasesRoot, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test.manifest.json"),
            JsonSerializer.Serialize(new TestCaseManifest
            {
                Id = id,
                Version = "1.0",
                Name = $"{id} Test",
                Category = "Test"
            }, JsonDefaults.WriteOptions));
        File.WriteAllText(Path.Combine(dir, "run.ps1"),
            @"$artifactsDir = ""$PSScriptRoot\artifacts""
New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
$report = @{
    schemaVersion = '1.5.0'
    status = 'Failed'
    summary = 'Test failed intentionally'
    details = 'This test case always fails for testing purposes'
}
$report | ConvertTo-Json -Depth 10 | Set-Content ""$artifactsDir\report.json"" -Encoding utf8
# Exit with non-zero code to indicate failure
exit 1");
    }

    /// <summary>
    /// Test: repeat=2 should execute all test cases twice.
    /// Spec: "repeat controls MUST re-run the entire ordered list in order"
    /// </summary>
    [Fact]
    public async Task Repeat_ExecutesAllTestCasesMultipleTimes()
    {
        // Arrange
        CreatePassingTestCase("Test1");
        CreatePassingTestCase("Test2");

        var suiteDir = Path.Combine(_testSuitesRoot, "Suite1");
        Directory.CreateDirectory(suiteDir);
        File.WriteAllText(Path.Combine(suiteDir, "suite.manifest.json"),
            JsonSerializer.Serialize(new TestSuiteManifest
            {
                Id = "Suite1",
                Version = "1.0",
                Name = "Suite1",
                Controls = new SuiteControls { Repeat = 2 },
                TestCases = new List<TestCaseNode>
                {
                    new TestCaseNode { NodeId = "node1", Ref = "Test1" },
                    new TestCaseNode { NodeId = "node2", Ref = "Test2" }
                }
            }, JsonDefaults.WriteOptions));

        var discovery = new DiscoveryService().Discover(_testCasesRoot, _testSuitesRoot, _testPlansRoot);
        
        // Verify discovery succeeded
        Assert.True(discovery.TestSuites.ContainsKey("Suite1@1.0"), 
            $"Suite not discovered. Found suites: {string.Join(", ", discovery.TestSuites.Keys)}");
        
        var suite = discovery.TestSuites["Suite1@1.0"];
        var reporter = new TestReporter();
        var orchestrator = new SuiteOrchestrator(discovery, _runsRoot, _tempRoot, reporter);

        // Act
        var result = await orchestrator.ExecuteAsync(suite, new RunRequest());

        // Assert
        var errorMsg = string.Join("; ", reporter.Errors);
        Assert.True(reporter.GetExecutionCount("node1") > 0, 
            $"node1 was not executed. Errors: {errorMsg}. Test cases discovered: {discovery.TestCases.Count}");
        Assert.Equal(2, reporter.GetExecutionCount("node1"));
        Assert.Equal(2, reporter.GetExecutionCount("node2"));
        Assert.Equal(RunStatus.Passed, result.Status);
    }

    /// <summary>
    /// CRITICAL TEST: continueOnFailure=false should stop ENTIRE pipeline on first failure.
    /// Spec: "continueOnFailure=false MUST stop the pipeline after the first non-Passed status"
    /// 
    /// BUG: Currently only breaks inner loop (test cases), not outer loop (repeat iterations).
    /// Expected: node1 (pass), node2 (fail), STOP
    /// Bug behavior: node1 (pass), node2 (fail), node1 (pass iteration 2), node2 (fail iteration 2)
    /// </summary>
    [Fact]
    public async Task ContinueOnFailure_False_StopsEntirePipeline()
    {
        // Arrange
        CreatePassingTestCase("Test1");
        CreateFailingTestCase("Test2");
        CreatePassingTestCase("Test3");

        var suiteDir = Path.Combine(_testSuitesRoot, "Suite1");
        Directory.CreateDirectory(suiteDir);
        File.WriteAllText(Path.Combine(suiteDir, "suite.manifest.json"),
            JsonSerializer.Serialize(new TestSuiteManifest
            {
                Id = "Suite1",
                Version = "1.0",
                Name = "Suite1",
                Controls = new SuiteControls
                {
                    Repeat = 2,
                    ContinueOnFailure = false
                },
                TestCases = new List<TestCaseNode>
                {
                    new TestCaseNode { NodeId = "node1", Ref = "Test1" },
                    new TestCaseNode { NodeId = "node2", Ref = "Test2" },
                    new TestCaseNode { NodeId = "node3", Ref = "Test3" }
                }
            }, JsonDefaults.WriteOptions));

        var discovery = new DiscoveryService().Discover(_testCasesRoot, _testSuitesRoot, _testPlansRoot);
        var suite = discovery.TestSuites["Suite1@1.0"];
        var reporter = new TestReporter();
        var orchestrator = new SuiteOrchestrator(discovery, _runsRoot, _tempRoot, reporter);

        // Act
        var result = await orchestrator.ExecuteAsync(suite, new RunRequest());

        // Assert
        // Should execute: node1 (pass), node2 (fail), then STOP
        // Should NOT execute: node3, and should NOT start iteration 2
        var errorMsg = string.Join("; ", reporter.Errors);
        
        // Debug output
        var debugInfo = $"node1: {reporter.GetExecutionCount("node1")}, " +
                       $"node2: {reporter.GetExecutionCount("node2")}, " +
                       $"node3: {reporter.GetExecutionCount("node3")}, " +
                       $"Suite Status: {result.Status}, " +
                       $"Errors: {errorMsg}";
        
        Assert.True(reporter.GetExecutionCount("node1") == 1, 
            $"node1 should execute once, but executed {reporter.GetExecutionCount("node1")} times. {debugInfo}");
        Assert.Equal(1, reporter.GetExecutionCount("node2"));
        Assert.Equal(0, reporter.GetExecutionCount("node3"));
        
        // Suite status should be Failed (because node2 failed)
        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(1, reporter.GetExecutionCount("node2"));
        Assert.Equal(0, reporter.GetExecutionCount("node3"));
        Assert.Equal(RunStatus.Failed, result.Status);
    }

    /// <summary>
    /// Test: continueOnFailure=true should continue executing remaining nodes.
    /// Spec: "continueOnFailure=true MUST continue to the next node after recording the failed status"
    /// </summary>
    [Fact]
    public async Task ContinueOnFailure_True_ContinuesExecution()
    {
        // Arrange
        CreatePassingTestCase("Test1");
        CreateFailingTestCase("Test2");
        CreatePassingTestCase("Test3");

        var suiteDir = Path.Combine(_testSuitesRoot, "Suite1");
        Directory.CreateDirectory(suiteDir);
        File.WriteAllText(Path.Combine(suiteDir, "suite.manifest.json"),
            JsonSerializer.Serialize(new TestSuiteManifest
            {
                Id = "Suite1",
                Version = "1.0",
                Name = "Suite1",
                Controls = new SuiteControls
                {
                    Repeat = 2,
                    ContinueOnFailure = true
                },
                TestCases = new List<TestCaseNode>
                {
                    new TestCaseNode { NodeId = "node1", Ref = "Test1" },
                    new TestCaseNode { NodeId = "node2", Ref = "Test2" },
                    new TestCaseNode { NodeId = "node3", Ref = "Test3" }
                }
            }, JsonDefaults.WriteOptions));

        var discovery = new DiscoveryService().Discover(_testCasesRoot, _testSuitesRoot, _testPlansRoot);
        var suite = discovery.TestSuites["Suite1@1.0"];
        var reporter = new TestReporter();
        var orchestrator = new SuiteOrchestrator(discovery, _runsRoot, _tempRoot, reporter);

        // Act
        var result = await orchestrator.ExecuteAsync(suite, new RunRequest());

        // Assert
        // With continueOnFailure=true and repeat=2, all nodes should execute in both iterations
        var errorMsg = string.Join("; ", reporter.Errors);
        Assert.Equal(2, reporter.GetExecutionCount("node1"));
        Assert.Equal(2, reporter.GetExecutionCount("node2"));
        Assert.Equal(2, reporter.GetExecutionCount("node3"));
        Assert.Equal(RunStatus.Failed, result.Status);
    }
}
