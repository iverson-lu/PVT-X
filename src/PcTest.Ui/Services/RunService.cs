using System.IO;
using System.Text.Json;
using PcTest.Contracts;
using PcTest.Contracts.Requests;
using PcTest.Engine;

namespace PcTest.Ui.Services;

/// <summary>
/// Service for executing test runs.
/// </summary>
public sealed class RunService : IRunService
{
    private readonly TestEngine _engine;
    private readonly ISettingsService _settingsService;
    private readonly IFileSystemService _fileSystemService;
    private RunExecutionContext? _currentContext;
    private RunExecutionState? _currentState;

    public event EventHandler<RunExecutionState>? StateChanged;
    public event EventHandler<string>? ConsoleOutput;

    public RunExecutionState? CurrentState => _currentState;

    public RunService(TestEngine engine, ISettingsService settingsService, IFileSystemService fileSystemService)
    {
        _engine = engine;
        _settingsService = settingsService;
        _fileSystemService = fileSystemService;
    }

    private void LogDebug(string message)
    {
        if (_settingsService.CurrentSettings.ShowDebugOutput)
        {
            ConsoleOutput?.Invoke(this, message);
        }
    }

    public async Task<RunExecutionContext> ExecuteAsync(RunRequest request, CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.CurrentSettings;
        
        // Configure engine
        _engine.Configure(
            settings.ResolvedTestCasesRoot,
            settings.ResolvedTestSuitesRoot,
            settings.ResolvedTestPlansRoot,
            settings.ResolvedRunsRoot);
        
        // Create execution context
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var runType = request.TargetType ?? RunType.TestCase;
        var targetIdentity = request.TargetIdentity ?? string.Empty;
        
        _currentContext = new RunExecutionContext
        {
            RunId = string.Empty, // Will be set after execution starts
            RunType = runType,
            TargetIdentity = targetIdentity,
            StartTime = DateTime.UtcNow,
            CancellationSource = cts
        };
        
        // Initialize state
        _currentState = new RunExecutionState
        {
            RunId = string.Empty,
            IsRunning = true
        };
        
        StateChanged?.Invoke(this, _currentState);
        
        try
        {
            // Execute through engine
            var result = await _engine.ExecuteAsync(request, cts.Token);
            
            // Extract the actual run ID from index.jsonl (most recent entry)
            var actualRunId = await GetMostRecentRunIdFromIndexAsync();
            if (!string.IsNullOrEmpty(actualRunId))
            {
                _currentContext.RunId = actualRunId;
                _currentState.RunId = actualRunId;
                LogDebug($"\n[DEBUG] Extracted RunId from index: {actualRunId}");
            }
            else
            {
                ConsoleOutput?.Invoke(this, "\n[ERROR] Failed to extract RunId from index.jsonl");
            }
            
            // Update final state
            _currentState.IsRunning = false;
            _currentState.FinalStatus = GetStatusFromResult(result);
            
            // Load execution details from result artifacts
            LogDebug($"\n[DEBUG] About to load execution details. RunId={_currentContext.RunId}");
            if (!string.IsNullOrEmpty(_currentContext.RunId))
            {
                await LoadExecutionDetailsAsync(_currentContext.RunId);
                LogDebug($"[DEBUG] After LoadExecutionDetailsAsync, Nodes.Count={_currentState.Nodes.Count}");
            }
            
            LogDebug($"[DEBUG] Firing StateChanged event with {_currentState.Nodes.Count} nodes");
            StateChanged?.Invoke(this, _currentState);
            
            // Load and stream stdout.log if available
            if (!string.IsNullOrEmpty(_currentContext.RunId))
            {
                await LoadConsoleOutputAsync(_currentContext.RunId);
            }
            
            // Notify completion
            ConsoleOutput?.Invoke(this, $"\nRun completed with status: {_currentState.FinalStatus}");
        }
        catch (OperationCanceledException)
        {
            _currentState.IsRunning = false;
            _currentState.FinalStatus = RunStatus.Aborted;
            ConsoleOutput?.Invoke(this, "\nRun aborted by user.");
            StateChanged?.Invoke(this, _currentState);
        }
        catch (Exception ex)
        {
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
            
            LogDebug($"\n[DEBUG] Loading execution details from: {runFolder}");
            
            // Read result.json
            var resultPath = Path.Combine(runFolder, "result.json");
            LogDebug($"[DEBUG] Checking result.json at: {resultPath}");
            LogDebug($"[DEBUG] File exists: {_fileSystemService.FileExists(resultPath)}");
            
            if (_fileSystemService.FileExists(resultPath))
            {
                var json = await _fileSystemService.ReadAllTextAsync(resultPath);
                LogDebug($"[DEBUG] Read result.json, length: {json.Length}");
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
                    IsRunning = false
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
                    LogDebug($"[DEBUG] Adding node to _currentState.Nodes. Current count: {_currentState.Nodes.Count}");
                    _currentState.Nodes.Clear();
                    _currentState.Nodes.Add(node);
                    LogDebug($"[DEBUG] After adding: NodeId={node.NodeId}, TestId={node.TestId}, Status={node.Status}, Count={_currentState.Nodes.Count}");
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
                                IsRunning = false
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
                            
                            _currentState.Nodes.Add(childNode);
                        }
                        catch
                        {
                            // Skip malformed lines
                        }
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
