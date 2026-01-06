using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using PcTest.Contracts;
using PcTest.Contracts.Requests;
using PcTest.Engine;
using PcTest.Engine.Execution;

namespace PcTest.Ui.Services;

/// <summary>
/// Service for executing test runs.
/// </summary>
public sealed class RunService : IRunService, IExecutionReporter, IDisposable
{
    private readonly TestEngine _engine;
    private readonly ISettingsService _settingsService;
    private readonly IFileSystemService _fileSystemService;
    private readonly Dispatcher _dispatcher;
    private RunExecutionContext? _currentContext;
    private RunExecutionState? _currentState;
    private const string RootIterationKey = "__root__";
    private readonly Dictionary<string, NodeExecutionState> _nodeInstances = new();
    private readonly Dictionary<string, NodeExecutionState> _activeNodes = new();
    private readonly Dictionary<string, PlannedNode> _plannedNodeLookup = new();
    private readonly Dictionary<string, List<PlannedNode>> _plannedNodesById = new();
    private readonly Dictionary<string, List<PlannedNode>> _plannedNodesByParent = new();
    private readonly Dictionary<string, int> _plannedCountsByParent = new();
    private readonly Dictionary<string, int> _executionIndexByParent = new();
    private readonly List<PlannedNode> _plannedNodes = new();
    private string? _currentSuiteNodeId;
    private int _plannedNodeCount;
    private int _executionIndex;
    private bool _supportsIterations;
    private readonly List<Task> _consoleLoadingTasks = new();
    
    // Real-time log tailing
    private LogTailService? _tailService;
    private readonly Dictionary<string, LogTailService> _nodeTailServices = new();

    public event EventHandler<RunExecutionState>? StateChanged;
    public event EventHandler<string>? ConsoleOutput;

    public RunExecutionState? CurrentState => _currentState;

    public RunService(TestEngine engine, ISettingsService settingsService, IFileSystemService fileSystemService)
    {
        _engine = engine;
        _settingsService = settingsService;
        _fileSystemService = fileSystemService;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }
    
    public void Dispose()
    {
        StopAllTailing();
    }

    private void StopAllTailing()
    {
        _tailService?.Dispose();
        _tailService = null;
        
        foreach (var service in _nodeTailServices.Values)
        {
            service.Dispose();
        }
        _nodeTailServices.Clear();
    }
    
    private async Task StopAllTailingAsync()
    {
        if (_tailService is not null)
        {
            await _tailService.StopTailingAsync();
            _tailService = null;
        }
        
        var tailServices = _nodeTailServices.Values.ToList();
        foreach (var service in tailServices)
        {
            try
            {
                await service.StopTailingAsync();
            }
            catch
            {
                // Best effort
            }
        }
        _nodeTailServices.Clear();
    }

    private void LogDebug(string message)
    {
        if (_settingsService.CurrentSettings.ShowDebugOutput)
        {
            ConsoleOutput?.Invoke(this, message);
        }
    }

    #region IExecutionReporter Implementation

    public void OnRunPlanned(string runId, RunType runType, IReadOnlyList<PlannedNode> plannedNodes)
    {
        if (_currentState is null) return;

        // Only clear if this is a new run (different runId)
        if (_currentState.RunId != runId)
        {
            _currentState.RunId = runId;
            _nodeInstances.Clear();
            _activeNodes.Clear();
            _plannedNodeLookup.Clear();
            _plannedNodesById.Clear();
            _plannedNodesByParent.Clear();
            _plannedCountsByParent.Clear();
            _executionIndexByParent.Clear();
            _plannedNodes.Clear();
            _plannedNodeCount = 0;
            _executionIndex = 0;
            _supportsIterations = false;
            _consoleLoadingTasks.Clear();
            _currentState.Nodes.Clear();
            _currentSuiteNodeId = null;
        }

        if (runType == RunType.TestSuite && plannedNodes.All(node => string.IsNullOrEmpty(node.ParentNodeId)))
        {
            _supportsIterations = true;
        }

        if (plannedNodes.Any(node => !string.IsNullOrEmpty(node.ParentNodeId)))
        {
            _supportsIterations = true;
        }

        if (_plannedNodes.Count == 0)
        {
            _plannedNodes.AddRange(plannedNodes);
            _plannedNodeCount = _plannedNodes.Count;
            _plannedNodeLookup.Clear();
            _plannedNodesById.Clear();
            foreach (var planned in _plannedNodes)
            {
                _plannedNodeLookup[BuildPlannedKey(planned.NodeId, planned.ParentNodeId)] = planned;
                AddPlannedNodeById(planned);
            }
        }

        foreach (var planned in plannedNodes)
        {
            var plannedKey = BuildPlannedKey(planned.NodeId, planned.ParentNodeId);
            var parentKey = GetIterationKey(planned.ParentNodeId);
            if (!_plannedNodesByParent.TryGetValue(parentKey, out var parentList))
            {
                parentList = new List<PlannedNode>();
                _plannedNodesByParent[parentKey] = parentList;
            }

            var parentSequenceIndex = parentList.FindIndex(node => node.NodeId == planned.NodeId);
            if (parentSequenceIndex < 0)
            {
                parentList.Add(planned);
                parentSequenceIndex = parentList.Count - 1;
                _plannedCountsByParent[parentKey] = parentList.Count;
            }

            var sequenceIndex = _plannedNodes.FindIndex(node => node.NodeId == planned.NodeId &&
                                                               node.ParentNodeId == planned.ParentNodeId);
            if (sequenceIndex < 0)
            {
                sequenceIndex = _plannedNodes.Count;
                _plannedNodes.Add(planned);
                _plannedNodeLookup[plannedKey] = planned;
                AddPlannedNodeById(planned);
                _plannedNodeCount = _plannedNodes.Count;
            }

            var nodeKey = BuildNodeKey(planned.NodeId, planned.ParentNodeId, 0, parentSequenceIndex);

            // Skip if node already exists (avoid duplicates)
            if (_nodeInstances.ContainsKey(nodeKey))
                continue;

            // Find name based on node type and ID
            string? testName = null, suiteName = null, planName = null;
            
            if (_engine.Discovery != null)
            {
                if (planned.NodeType == RunType.TestCase)
                {
                    var testCase = _engine.Discovery.TestCases.Values.FirstOrDefault(tc => 
                        tc.Manifest.Id.Equals(planned.TestId, StringComparison.OrdinalIgnoreCase));
                    testName = testCase?.Manifest.Name;
                }
                else if (planned.NodeType == RunType.TestSuite)
                {
                    var suite = _engine.Discovery.TestSuites.Values.FirstOrDefault(s => 
                        s.Manifest.Id.Equals(planned.TestId, StringComparison.OrdinalIgnoreCase));
                    suiteName = suite?.Manifest.Name;
                }
                else if (planned.NodeType == RunType.TestPlan)
                {
                    var plan = _engine.Discovery.TestPlans.Values.FirstOrDefault(p => 
                        p.Manifest.Id.Equals(planned.TestId, StringComparison.OrdinalIgnoreCase));
                    planName = plan?.Manifest.Name;
                }
            }

            var node = new NodeExecutionState
            {
                NodeId = planned.NodeId,
                NodeType = planned.NodeType,
                TestId = planned.TestId,
                TestVersion = planned.TestVersion,
                TestName = testName,
                SuiteName = suiteName,
                PlanName = planName,
                Status = null, // Pending
                IsRunning = false,
                ParentNodeId = planned.ParentNodeId,
                IterationIndex = 0,
                SequenceIndex = parentSequenceIndex
            };
            _nodeInstances[nodeKey] = node;
            
            // If this node has a parent, insert it after the parent (or after the last child of that parent)
            if (!string.IsNullOrEmpty(planned.ParentNodeId))
            {
                // Find the insertion index - after parent and all its existing children
                int insertIndex = -1;
                for (int i = _currentState.Nodes.Count - 1; i >= 0; i--)
                {
                    // Check if this is a sibling (same parent) or the parent itself
                    if (_currentState.Nodes[i].ParentNodeId == planned.ParentNodeId)
                    {
                        // Found a sibling, insert after it
                        insertIndex = i + 1;
                        break;
                    }
                    else if (_currentState.Nodes[i].NodeId == planned.ParentNodeId)
                    {
                        // Found the parent, insert after it
                        insertIndex = i + 1;
                        break;
                    }
                }
                
                if (insertIndex >= 0 && insertIndex <= _currentState.Nodes.Count)
                {
                    _currentState.Nodes.Insert(insertIndex, node);
                }
                else
                {
                    _currentState.Nodes.Add(node);
                }
            }
            else
            {
                _currentState.Nodes.Add(node);
            }
        }

        LogDebug($"[REPORTER] OnRunPlanned: runId={runId}, runType={runType}, nodes={plannedNodes.Count}");
        StateChanged?.Invoke(this, _currentState);
    }

    public void OnNodeStarted(string runId, string nodeId)
    {
        if (_currentState is null) return;

        _currentState.CurrentNodeId = nodeId;

        var (iterationIndex, sequenceIndex, parentNodeId, planned) = ResolveIterationContext(nodeId);
        var nodeKey = BuildNodeKey(nodeId, parentNodeId, iterationIndex, sequenceIndex);
        if (!_nodeInstances.TryGetValue(nodeKey, out var node))
        {
            node = CreateNodeInstance(nodeId, iterationIndex, sequenceIndex, planned);
            _nodeInstances[nodeKey] = node;
            _currentState.Nodes.Add(node);
        }

        node.IsRunning = true;
        _activeNodes[nodeId] = node;

        if (planned?.NodeType == RunType.TestSuite && string.IsNullOrEmpty(planned.ParentNodeId))
        {
            _currentSuiteNodeId = planned.NodeId;
        }

        // Start tailing for this node's run folder (child run)
        StartNodeTailingAsync(runId, nodeId).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);

        LogDebug($"[REPORTER] OnNodeStarted: runId={runId}, nodeId={nodeId}");
        StateChanged?.Invoke(this, _currentState);
    }

    public void OnNodeFinished(string runId, NodeFinishedState nodeState)
    {
        if (_currentState is null) return;

        if (!_activeNodes.TryGetValue(nodeState.NodeId, out var node))
        {
            node = FindLatestNodeInstance(nodeState.NodeId, nodeState.ParentNodeId);
        }

        if (node is not null)
        {
            node.IsRunning = false;
            node.Status = nodeState.Status;
            node.Duration = nodeState.Duration;
            node.RetryCount = nodeState.RetryCount;
            node.ParentNodeId = nodeState.ParentNodeId;
        }

        _activeNodes.Remove(nodeState.NodeId);
        if (!string.IsNullOrEmpty(_currentSuiteNodeId) &&
            nodeState.NodeId == _currentSuiteNodeId &&
            string.IsNullOrEmpty(nodeState.ParentNodeId))
        {
            _currentSuiteNodeId = null;
        }

        // Stop tailing for this node and emit final header
        var stopTailTask = StopNodeTailingAndEmitHeaderAsync(nodeState.NodeId, nodeState.Status);
        lock (_consoleLoadingTasks)
        {
            _consoleLoadingTasks.Add(stopTailTask);
        }

        LogDebug($"[REPORTER] OnNodeFinished: runId={runId}, nodeId={nodeState.NodeId}, status={nodeState.Status}");
        StateChanged?.Invoke(this, _currentState);
    }

    public void OnRunFinished(string runId, RunStatus finalStatus)
    {
        if (_currentState is null) return;

        _currentState.IsRunning = false;
        _currentState.FinalStatus = finalStatus;
        _currentState.CurrentNodeId = string.Empty;

        LogDebug($"[REPORTER] OnRunFinished: runId={runId}, finalStatus={finalStatus}");
        StateChanged?.Invoke(this, _currentState);
    }

    private (int iterationIndex, int sequenceIndex, string? parentNodeId, PlannedNode? planned)
        ResolveIterationContext(string nodeId)
    {
        var planned = FindPlannedNode(nodeId);
        if (!_supportsIterations)
        {
            return (0, ResolvePlannedSequenceIndex(nodeId), planned?.ParentNodeId, planned);
        }

        if (planned is not null)
        {
            var parentKey = GetIterationKey(planned.ParentNodeId);
            if (_plannedCountsByParent.TryGetValue(parentKey, out var parentCount) && parentCount > 0)
            {
                var execIndex = _executionIndexByParent.TryGetValue(parentKey, out var value) ? value : 0;
                var iterationIndex = execIndex / parentCount;
                _executionIndexByParent[parentKey] = execIndex + 1;
                return (iterationIndex, ResolvePlannedSequenceIndex(nodeId, parentKey), planned.ParentNodeId, planned);
            }
        }

        if (_plannedNodeCount > 0)
        {
            var iterationIndex = _executionIndex / _plannedNodeCount;
            var sequenceIndex = _executionIndex % _plannedNodeCount;
            _executionIndex++;
            return (iterationIndex, sequenceIndex, planned?.ParentNodeId, planned);
        }

        return (0, ResolvePlannedSequenceIndex(nodeId), planned?.ParentNodeId, planned);
    }

    private int ResolvePlannedSequenceIndex(string nodeId)
    {
        return ResolvePlannedSequenceIndex(nodeId, RootIterationKey);
    }

    private int ResolvePlannedSequenceIndex(string nodeId, string parentKey)
    {
        if (_plannedNodesByParent.TryGetValue(parentKey, out var parentList))
        {
            var parentIndex = parentList.FindIndex(node => node.NodeId == nodeId);
            if (parentIndex >= 0)
            {
                return parentIndex;
            }
        }

        var planned = FindPlannedNode(nodeId);
        if (planned is not null)
        {
            var plannedIndex = _plannedNodes.IndexOf(planned);
            if (plannedIndex >= 0)
            {
                return plannedIndex;
            }
        }
        return 0;
    }

    private NodeExecutionState? FindLatestNodeInstance(string nodeId, string? parentNodeId)
    {
        return _nodeInstances.Values
            .Where(node => node.NodeId == nodeId && node.ParentNodeId == parentNodeId)
            .OrderByDescending(node => node.IterationIndex)
            .ThenByDescending(node => node.SequenceIndex)
            .FirstOrDefault();
    }

    private NodeExecutionState CreateNodeInstance(
        string nodeId,
        int iterationIndex,
        int sequenceIndex,
        PlannedNode? planned)
    {
        return new NodeExecutionState
        {
            NodeId = nodeId,
            NodeType = planned?.NodeType ?? RunType.TestCase,
            TestId = planned?.TestId ?? nodeId,
            TestVersion = planned?.TestVersion ?? string.Empty,
            TestName = FindDisplayName(planned),
            SuiteName = FindSuiteName(planned),
            PlanName = FindPlanName(planned),
            Status = null,
            IsRunning = false,
            ParentNodeId = planned?.ParentNodeId,
            IterationIndex = iterationIndex,
            SequenceIndex = sequenceIndex
        };
    }

    private void AddPlannedNodeById(PlannedNode planned)
    {
        if (!_plannedNodesById.TryGetValue(planned.NodeId, out var list))
        {
            list = new List<PlannedNode>();
            _plannedNodesById[planned.NodeId] = list;
        }

        if (!list.Contains(planned))
        {
            list.Add(planned);
        }
    }

    private PlannedNode? FindPlannedNode(string nodeId, string? parentNodeId = null)
    {
        if (!string.IsNullOrEmpty(parentNodeId) &&
            _plannedNodeLookup.TryGetValue(BuildPlannedKey(nodeId, parentNodeId), out var planned))
        {
            return planned;
        }

        if (!_plannedNodesById.TryGetValue(nodeId, out var candidates) || candidates.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(_currentSuiteNodeId))
        {
            var match = candidates.FirstOrDefault(p => p.ParentNodeId == _currentSuiteNodeId);
            if (match is not null)
            {
                return match;
            }
        }

        return candidates[0];
    }

    private string? FindDisplayName(PlannedNode? planned)
    {
        if (planned is null || _engine.Discovery is null)
        {
            return null;
        }

        if (planned.NodeType == RunType.TestCase)
        {
            var testCase = _engine.Discovery.TestCases.Values.FirstOrDefault(tc =>
                tc.Manifest.Id.Equals(planned.TestId, StringComparison.OrdinalIgnoreCase));
            return testCase?.Manifest.Name;
        }

        return null;
    }

    private string? FindSuiteName(PlannedNode? planned)
    {
        if (planned is null || _engine.Discovery is null || planned.NodeType != RunType.TestSuite)
        {
            return null;
        }

        var suite = _engine.Discovery.TestSuites.Values.FirstOrDefault(s =>
            s.Manifest.Id.Equals(planned.TestId, StringComparison.OrdinalIgnoreCase));
        return suite?.Manifest.Name;
    }

    private string? FindPlanName(PlannedNode? planned)
    {
        if (planned is null || _engine.Discovery is null || planned.NodeType != RunType.TestPlan)
        {
            return null;
        }

        var plan = _engine.Discovery.TestPlans.Values.FirstOrDefault(p =>
            p.Manifest.Id.Equals(planned.TestId, StringComparison.OrdinalIgnoreCase));
        return plan?.Manifest.Name;
    }

    private static string GetIterationKey(string? parentNodeId)
    {
        return string.IsNullOrEmpty(parentNodeId) ? RootIterationKey : parentNodeId;
    }

    private static string BuildPlannedKey(string nodeId, string? parentNodeId)
    {
        return $"{GetIterationKey(parentNodeId)}::{nodeId}";
    }

    private static string BuildNodeKey(string nodeId, string? parentNodeId, int iterationIndex, int sequenceIndex)
    {
        return $"{iterationIndex}:{sequenceIndex}:{GetIterationKey(parentNodeId)}:{nodeId}";
    }

    #endregion

    public async Task<RunExecutionContext> ExecuteAsync(RunRequest request, CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.CurrentSettings;
        
        // Configure engine
        _engine.Configure(
            settings.ResolvedTestCasesRoot,
            settings.ResolvedTestSuitesRoot,
            settings.ResolvedTestPlansRoot,
            settings.ResolvedRunsRoot,
            settings.ResolvedAssetsRoot);
        
        // Set reporter for progress events
        _engine.SetReporter(this);
        
        // Create execution context
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var runType = request.TargetType ?? RunType.TestCase;
        var targetIdentity = request.TargetIdentity ?? string.Empty;
        
        _currentContext = new RunExecutionContext
        {
            RunId = string.Empty, // Will be set by OnRunPlanned
            RunType = runType,
            TargetIdentity = targetIdentity,
            StartTime = DateTime.UtcNow,
            CancellationSource = cts
        };
        
        // Initialize state
        _nodeInstances.Clear();
        _activeNodes.Clear();
        _plannedNodeLookup.Clear();
        _plannedNodesById.Clear();
        _plannedNodesByParent.Clear();
        _plannedCountsByParent.Clear();
        _executionIndexByParent.Clear();
        _plannedNodes.Clear();
        _plannedNodeCount = 0;
        _executionIndex = 0;
        _supportsIterations = false;
        _currentSuiteNodeId = null;
        _currentState = new RunExecutionState
        {
            RunId = string.Empty,
            IsRunning = true,
            Nodes = new List<NodeExecutionState>()
        };
        
        StateChanged?.Invoke(this, _currentState);
        
        try
        {
            // Execute through engine - reporter events will update state in real-time
            var result = await _engine.ExecuteAsync(request, cts.Token);
            
            // Update context with the run ID (should already be set by OnRunPlanned)
            if (!string.IsNullOrEmpty(_currentState.RunId))
            {
                _currentContext.RunId = _currentState.RunId;
            }
            else
            {
                // Fallback: extract from index.jsonl
                var actualRunId = await GetMostRecentRunIdFromIndexAsync();
                if (!string.IsNullOrEmpty(actualRunId))
                {
                    _currentContext.RunId = actualRunId;
                    _currentState.RunId = actualRunId;
                    LogDebug($"Extracted RunId from index: {actualRunId}");
                }
            }
            
            // Wait for all background console loading tasks to complete
            Task[] tasksToWait;
            lock (_consoleLoadingTasks)
            {
                tasksToWait = _consoleLoadingTasks.ToArray();
            }
            if (tasksToWait.Length > 0)
            {
                await Task.WhenAll(tasksToWait);
            }
            
            // Stop all tailing services and ensure final content is flushed
            await StopAllTailingAsync();
            
            // Give a short delay to ensure all buffered output has been processed
            await Task.Delay(150);
            
            // Notify completion
            ConsoleOutput?.Invoke(this, $"\nRun completed with status: {_currentState.FinalStatus}");
        }
        catch (OperationCanceledException)
        {
            _currentState.IsRunning = false;
            _currentState.FinalStatus = RunStatus.Aborted;
            
            // Stop all tailing services
            await StopAllTailingAsync();
            
            // Give a short delay to ensure all buffered output has been processed
            await Task.Delay(150);
            
            // Mark remaining pending nodes as Aborted
            foreach (var node in _nodeInstances.Values)
            {
                if (node.Status is null && !node.IsRunning)
                {
                    node.Status = RunStatus.Aborted;
                }
                else if (node.IsRunning)
                {
                    node.IsRunning = false;
                    node.Status = RunStatus.Aborted;
                }
            }
            
            ConsoleOutput?.Invoke(this, "\nRun aborted by user.");
            StateChanged?.Invoke(this, _currentState);
        }
        catch (Exception ex)
        {
            // Stop all tailing services
            await StopAllTailingAsync();
            
            // Give a short delay to ensure all buffered output has been processed
            await Task.Delay(150);
            
            _currentState.IsRunning = false;
            _currentState.FinalStatus = RunStatus.Error;
            
            ConsoleOutput?.Invoke(this, $"\nError: {ex.Message}\n{ex.StackTrace}");
            StateChanged?.Invoke(this, _currentState);
        }
        
        return _currentContext;
    }

    public Task StopAsync(string runId)
    {
        if (_currentContext?.RunId == runId)
        {
            // Request graceful cancellation
            _currentContext.CancellationSource.Cancel();
        }
        return Task.CompletedTask;
    }

    public Task AbortAsync(string runId)
    {
        if (_currentContext?.RunId == runId)
        {
            // Force immediate cancellation
            _currentContext.CancellationSource.Cancel();
        }
        return Task.CompletedTask;
    }

    private async Task<string?> GetMostRecentRunIdFromIndexAsync()
    {
        try
        {
            var settings = _settingsService.CurrentSettings;
            var indexPath = Path.Combine(settings.ResolvedRunsRoot, "index.jsonl");
            
            if (!_fileSystemService.FileExists(indexPath))
            {
                return null;
            }
            
            var lines = await _fileSystemService.ReadAllLinesAsync(indexPath);
            if (lines.Length == 0)
            {
                return null;
            }
            
            // Get the last line (most recent entry)
            var lastLine = lines[^1];
            if (string.IsNullOrWhiteSpace(lastLine))
            {
                return null;
            }
            
            var doc = JsonDocument.Parse(lastLine);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("runId", out var runIdElement))
            {
                return runIdElement.GetString();
            }
            
            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task LoadExecutionDetailsAsync(string runId)
    {
        try
        {
            var settings = _settingsService.CurrentSettings;
            var runFolder = Path.Combine(settings.ResolvedRunsRoot, runId);
            
            LogDebug($"Loading execution details from: {runFolder}");
            
            // Read result.json
            var resultPath = Path.Combine(runFolder, "result.json");
            LogDebug($"Checking result.json at: {resultPath}");
            LogDebug($"File exists: {_fileSystemService.FileExists(resultPath)}");
            
            if (_fileSystemService.FileExists(resultPath))
            {
                var json = await _fileSystemService.ReadAllTextAsync(resultPath);
                LogDebug($"Read result.json, length: {json.Length}");
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                // Extract node information from result
                var node = new NodeExecutionState
                {
                    NodeId = root.TryGetProperty("nodeId", out var nodeId) ? nodeId.GetString() ?? "" : "",
                    TestId = root.TryGetProperty("testId", out var testId) ? testId.GetString() ?? "" : "",
                    TestVersion = root.TryGetProperty("testVersion", out var version) ? version.GetString() ?? "" : "",
                    Status = root.TryGetProperty("status", out var status) 
                        ? Enum.TryParse<RunStatus>(status.GetString(), true, out var s) ? s : null
                        : null,
                    IsRunning = false,
                    IterationIndex = 0,
                    SequenceIndex = 0
                };
                
                // Calculate duration
                if (root.TryGetProperty("startTime", out var startTime) && 
                    root.TryGetProperty("endTime", out var endTime))
                {
                    if (DateTime.TryParse(startTime.GetString(), out var start) && 
                        DateTime.TryParse(endTime.GetString(), out var end))
                    {
                        node.Duration = end - start;
                    }
                }
                
                if (_currentState != null)
                {
                    LogDebug($"Adding node to _currentState.Nodes. Current count: {_currentState.Nodes.Count}");
                    _currentState.Nodes.Clear();
                    _currentState.Nodes.Add(node);
                    LogDebug($"After adding: NodeId={node.NodeId}, TestId={node.TestId}, Status={node.Status}, Count={_currentState.Nodes.Count}");
                }
            }
            
            // For suite/plan runs, check for children.jsonl
            var childrenPath = Path.Combine(runFolder, "children.jsonl");
            if (_fileSystemService.FileExists(childrenPath))
            {
                var lines = await _fileSystemService.ReadAllLinesAsync(childrenPath);
                if (_currentState != null)
                {
                    _currentState.Nodes.Clear();
                    
                    // Use dictionary to deduplicate nodes by NodeId (in case of multiple entries for retries)
                    var nodeDict = new Dictionary<string, NodeExecutionState>();
                    
                    var sequenceIndex = 0;
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        
                        try
                        {
                            var doc = JsonDocument.Parse(line);
                            var root = doc.RootElement;
                            
                            // For Plan runs, children have SuiteId/SuiteVersion instead of TestId/TestVersion
                            var testId = root.TryGetProperty("testId", out var testIdProp) ? testIdProp.GetString() ?? "" : "";
                            var testVersion = root.TryGetProperty("testVersion", out var testVersionProp) ? testVersionProp.GetString() ?? "" : "";
                            
                            // If no TestId, try SuiteId (for Plan runs)
                            if (string.IsNullOrEmpty(testId))
                            {
                                testId = root.TryGetProperty("suiteId", out var suiteIdProp) ? suiteIdProp.GetString() ?? "" : "";
                                testVersion = root.TryGetProperty("suiteVersion", out var suiteVersionProp) ? suiteVersionProp.GetString() ?? "" : "";
                            }
                            
                            var childNode = new NodeExecutionState
                            {
                                NodeId = root.TryGetProperty("nodeId", out var nodeId) ? nodeId.GetString() ?? "" : "",
                                TestId = testId,
                                TestVersion = testVersion,
                                Status = root.TryGetProperty("status", out var status) 
                                    ? Enum.TryParse<RunStatus>(status.GetString(), true, out var s) ? s : null
                                    : null,
                                IsRunning = false,
                                IterationIndex = 0,
                                SequenceIndex = sequenceIndex++
                            };
                            
                            // Try to get duration from the child run's result.json
                            if (root.TryGetProperty("runId", out var childRunId) && !string.IsNullOrEmpty(childRunId.GetString()))
                            {
                                var childRunFolder = Path.Combine(settings.ResolvedRunsRoot, childRunId.GetString()!);
                                var childResultPath = Path.Combine(childRunFolder, "result.json");
                                
                                if (_fileSystemService.FileExists(childResultPath))
                                {
                                    try
                                    {
                                        var childJson = await _fileSystemService.ReadAllTextAsync(childResultPath);
                                        var childDoc = JsonDocument.Parse(childJson);
                                        var childRoot = childDoc.RootElement;
                                        
                                        if (childRoot.TryGetProperty("startTime", out var childStartTime) && 
                                            childRoot.TryGetProperty("endTime", out var childEndTime))
                                        {
                                            if (DateTime.TryParse(childStartTime.GetString(), out var start) && 
                                                DateTime.TryParse(childEndTime.GetString(), out var end))
                                            {
                                                childNode.Duration = end - start;
                                            }
                                        }
                                    }
                                    catch
                                    {
                                        // Ignore errors reading child result
                                    }
                                }
                            }
                            
                            // Add or update the node in dictionary (later entries overwrite earlier ones)
                            if (!string.IsNullOrEmpty(childNode.NodeId))
                            {
                                nodeDict[childNode.NodeId] = childNode;
                            }
                        }
                        catch
                        {
                            // Skip malformed lines
                        }
                    }
                    
                    // Add all deduplicated nodes to the collection
                    foreach (var node in nodeDict.Values)
                    {
                        _currentState.Nodes.Add(node);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleOutput?.Invoke(this, $"\n[ERROR] Failed to load execution details: {ex.Message}");
            ConsoleOutput?.Invoke(this, $"[ERROR] Stack trace: {ex.StackTrace}");
        }
    }

    private async Task LoadConsoleOutputAsync(string runId)
    {
        try
        {
            var settings = _settingsService.CurrentSettings;
            var runFolder = Path.Combine(settings.ResolvedRunsRoot, runId);
            var stdoutPath = Path.Combine(runFolder, "stdout.log");
            
            if (_fileSystemService.FileExists(stdoutPath))
            {
                var content = await _fileSystemService.ReadAllTextAsync(stdoutPath);
                if (!string.IsNullOrEmpty(content))
                {
                    ConsoleOutput?.Invoke(this, content);
                }
            }
        }
        catch
        {
            // Ignore errors reading log file
        }
    }

    /// <summary>
    /// Starts tailing logs for a node. Looks up the child run folder from children.jsonl.
    /// For standalone runs (nodeId="standalone"), tails the main run folder directly.
    /// </summary>
    private async Task StartNodeTailingAsync(string parentRunId, string nodeId)
    {
        try
        {
            var settings = _settingsService.CurrentSettings;
            var parentRunFolder = Path.Combine(settings.ResolvedRunsRoot, parentRunId);
            
            // For standalone test case runs, the nodeId is "standalone" and there's no children.jsonl
            // The test runs directly in the main run folder
            if (nodeId == "standalone")
            {
                // Wait for run folder to be created
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    if (_fileSystemService.DirectoryExists(parentRunFolder))
                        break;
                    await Task.Delay(100);
                }
                
                if (!_fileSystemService.DirectoryExists(parentRunFolder))
                    return;
                
                // Create tail service for the main run folder
                var standaloneTailService = new LogTailService(_dispatcher);
                standaloneTailService.ContentReceived += (s, content) => ConsoleOutput?.Invoke(this, content);
                
                lock (_nodeTailServices)
                {
                    // Stop any existing tail for this node
                    if (_nodeTailServices.TryGetValue(nodeId, out var existing))
                    {
                        existing.Dispose();
                    }
                    _nodeTailServices[nodeId] = standaloneTailService;
                }
                
                // Emit header for standalone run
                ConsoleOutput?.Invoke(this, $"\n{new string('=', 6)} [Standalone | {parentRunId}] {new string('=', 6)}\n");
                
                standaloneTailService.StartTailing(parentRunFolder);
                return;
            }
            
            // For suite/plan child nodes, look up the child run ID from children.jsonl
            var childrenPath = Path.Combine(parentRunFolder, "children.jsonl");
            
            // Wait for children.jsonl to contain this node's entry
            string? childRunId = null;
            for (int attempt = 0; attempt < 30; attempt++) // Wait up to 3 seconds
            {
                if (_fileSystemService.FileExists(childrenPath))
                {
                    var lines = await _fileSystemService.ReadAllLinesAsync(childrenPath);
                    
                    foreach (var line in lines.Reverse())
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        
                        try
                        {
                            var child = JsonSerializer.Deserialize<ChildEntry>(line);
                            if (child?.NodeId == nodeId)
                            {
                                childRunId = child.RunId;
                                break;
                            }
                        }
                        catch { continue; }
                    }
                    
                    if (!string.IsNullOrEmpty(childRunId)) break;
                }
                
                await Task.Delay(100);
            }
            
            if (string.IsNullOrEmpty(childRunId)) return;
            
            var childRunFolder = Path.Combine(settings.ResolvedRunsRoot, childRunId);
            
            // Create tail service for this node
            var tailService = new LogTailService(_dispatcher);
            tailService.ContentReceived += (s, content) => ConsoleOutput?.Invoke(this, content);
            
            lock (_nodeTailServices)
            {
                // Stop any existing tail for this node
                if (_nodeTailServices.TryGetValue(nodeId, out var existing))
                {
                    existing.Dispose();
                }
                _nodeTailServices[nodeId] = tailService;
            }
            
            // Emit header for this node
            ConsoleOutput?.Invoke(this, $"\n{new string('-', 6)} [{nodeId} | RUNNING | {childRunId}] {new string('-', 6)}\n");
            
            tailService.StartTailing(childRunFolder);
        }
        catch
        {
            // Best effort - tailing is optional
        }
    }

    /// <summary>
    /// Stops tailing for a node and emits a completion header.
    /// </summary>
    private async Task StopNodeTailingAndEmitHeaderAsync(string nodeId, RunStatus status)
    {
        try
        {
            LogTailService? tailService = null;
            lock (_nodeTailServices)
            {
                _nodeTailServices.TryGetValue(nodeId, out tailService);
            }
            
            if (tailService is not null)
            {
                await tailService.StopTailingAsync();
                
                lock (_nodeTailServices)
                {
                    _nodeTailServices.Remove(nodeId);
                }
                
                tailService.Dispose();
            }
        }
        catch
        {
            // Best effort
        }
    }

    private class ChildEntry
    {
        [JsonPropertyName("runId")]
        public string RunId { get; set; } = string.Empty;
        
        [JsonPropertyName("nodeId")]
        public string NodeId { get; set; } = string.Empty;
        
        [JsonPropertyName("testId")]
        public string TestId { get; set; } = string.Empty;
        
        [JsonPropertyName("testVersion")]
        public string TestVersion { get; set; } = string.Empty;
        
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
    }

    private static string GenerateRunId(RunType runType)
    {
        var prefix = runType switch
        {
            RunType.TestCase => "R",
            RunType.TestSuite => "S",
            RunType.TestPlan => "P",
            _ => "R"
        };
        
        return $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..35];
    }

    private static RunStatus GetStatusFromResult(object result)
    {
        // Extract status from various result types
        var statusProp = result.GetType().GetProperty("Status");
        if (statusProp?.GetValue(result) is RunStatus status)
        {
            return status;
        }
        return RunStatus.Passed;
    }
}
