using System.Collections.ObjectModel;
using System.Text;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTest.Contracts;
using PcTest.Contracts.Requests;
using PcTest.Ui.Services;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// ViewModel for the Run page.
/// </summary>
public partial class RunViewModel : ViewModelBase
{
    private readonly IRunService _runService;
    private readonly INavigationService _navigationService;
    private readonly IDiscoveryService _discoveryService;
    private readonly ISuiteRepository _suiteRepository;
    private readonly IPlanRepository _planRepository;
    private readonly IRunRepository _runRepository;

    private CancellationTokenSource? _runCts;
    private Dictionary<string, object?>? _parameterOverrides;
    private readonly Dictionary<string, NodeExecutionStateViewModel> _nodeViewModelDict = new();
    private System.Windows.Threading.DispatcherTimer? _eventRefreshTimer;
    private readonly List<string> _plannedNodeIds = new();
    private readonly Dictionary<string, int> _activeIterationByNodeId = new();
    private readonly Dictionary<(int iterationIndex, string nodeId), NodeExecutionStateViewModel> _iterationNodeLookup = new();
    private int _nodeStartCount;
    private string _lastCurrentNodeId = string.Empty;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _showTargetSelector = true;
    [ObservableProperty] private string _targetIdentity = string.Empty;
    [ObservableProperty] private RunType _runType = RunType.TestCase;
    [ObservableProperty] private string _runId = string.Empty;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private RunStatus? _finalStatus;
    [ObservableProperty] private int _repeatCount = 1;
    [ObservableProperty] private int _currentIterationIndex = 1;

    // Navigation context
    private string? _sourcePage;
    private int? _sourceTabIndex;
    private string? _sourceTargetIdentity;
    
    public bool HasBackButton => !string.IsNullOrEmpty(_sourcePage);

    [ObservableProperty]
    private ObservableCollection<string> _availableTargets = new();

    [ObservableProperty]
    private ObservableCollection<NodeExecutionStateViewModel> _nodes = new();

    [ObservableProperty]
    private NodeExecutionStateViewModel? _selectedNode;

    [ObservableProperty]
    private ObservableCollection<IterationExecutionStateViewModel> _iterations = new();

    [ObservableProperty]
    private string _consoleOutput = string.Empty;

    [ObservableProperty]
    private string _eventsOutput = string.Empty;

    [ObservableProperty]
    private ObservableCollection<StructuredEventViewModel> _events = new();

    private readonly StringBuilder _consoleBuffer = new();
    private readonly StringBuilder _eventsBuffer = new();

    public RunViewModel(
        IRunService runService, 
        INavigationService navigationService,
        IDiscoveryService discoveryService,
        ISuiteRepository suiteRepository,
        IPlanRepository planRepository,
        IRunRepository runRepository)
    {
        _runService = runService;
        _navigationService = navigationService;
        _discoveryService = discoveryService;
        _suiteRepository = suiteRepository;
        _planRepository = planRepository;
        _runRepository = runRepository;

        _runService.StateChanged += OnStateChanged;
        _runService.ConsoleOutput += OnConsoleOutput;

        // Setup event refresh timer (fires every 500ms during execution)
        _eventRefreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _eventRefreshTimer.Tick += (s, e) =>
        {
            if (!string.IsNullOrEmpty(RunId))
            {
                _ = LoadEventsAsync(RunId);
            }
        };
    }

    partial void OnIsRunningChanged(bool value)
    {
        if (value)
        {
            _eventRefreshTimer?.Start();
        }
        else
        {
            _eventRefreshTimer?.Stop();
        }
    }

    public string RepeatStatusText => RepeatCount > 1
        ? $"Repeat {RepeatCount}x · Iteration {CurrentIterationIndex}/{RepeatCount}"
        : string.Empty;

    public bool ShowRepeatStatus => RepeatCount > 1;

    partial void OnRepeatCountChanged(int value)
    {
        if (value < 1)
        {
            RepeatCount = 1;
            return;
        }

        if (CurrentIterationIndex > value)
        {
            CurrentIterationIndex = value;
        }

        OnPropertyChanged(nameof(RepeatStatusText));
        OnPropertyChanged(nameof(ShowRepeatStatus));
    }

    partial void OnCurrentIterationIndexChanged(int value)
    {
        OnPropertyChanged(nameof(RepeatStatusText));
    }

    public async void Initialize(object? parameter)
    {
        if (parameter is RunNavigationParameter navParam)
        {
            TargetIdentity = navParam.TargetIdentity;
            RunType = navParam.RunType;
            _parameterOverrides = navParam.ParameterOverrides;
            _sourcePage = navParam.SourcePage;
            _sourceTabIndex = navParam.SourceTabIndex;
            _sourceTargetIdentity = navParam.TargetIdentity;
            StatusText = $"Ready to run {RunType}: {TargetIdentity}";
            ShowTargetSelector = false;
            
            OnPropertyChanged(nameof(HasBackButton));
            await LoadRepeatCountAsync();
            
            // Auto-start if requested
            if (navParam.AutoStart)
            {
                await StartAsync();
            }
        }
        else
        {
            _sourcePage = null;
            _sourceTabIndex = null;
            _sourceTargetIdentity = null;
            ShowTargetSelector = true;
            OnPropertyChanged(nameof(HasBackButton));
            await LoadAvailableTargetsAsync();
            await LoadRepeatCountAsync();
        }
    }

    private async Task LoadAvailableTargetsAsync()
    {
        AvailableTargets.Clear();

        var discovery = _discoveryService.CurrentDiscovery;
        if (discovery is null)
        {
            discovery = await _discoveryService.DiscoverAsync();
        }

        switch (RunType)
        {
            case RunType.TestCase:
                foreach (var tc in discovery.TestCases.Values.OrderBy(c => c.Manifest.Name))
                {
                    AvailableTargets.Add($"{tc.Manifest.Id}@{tc.Manifest.Version}");
                }
                break;
                
            case RunType.TestSuite:
                var suites = await _suiteRepository.GetAllAsync();
                foreach (var suite in suites.OrderBy(s => s.Manifest.Name))
                {
                    AvailableTargets.Add($"{suite.Manifest.Id}@{suite.Manifest.Version}");
                }
                break;
                
            case RunType.TestPlan:
                var plans = await _planRepository.GetAllAsync();
                foreach (var plan in plans.OrderBy(p => p.Manifest.Name))
                {
                    AvailableTargets.Add($"{plan.Manifest.Id}@{plan.Manifest.Version}");
                }
                break;
        }
    }

    private async Task LoadRepeatCountAsync()
    {
        if (RunType == RunType.TestSuite && !string.IsNullOrEmpty(TargetIdentity))
        {
            var suite = await _suiteRepository.GetByIdentityAsync(TargetIdentity);
            RepeatCount = Math.Max(1, suite?.Manifest.Controls?.Repeat ?? 1);
        }
        else
        {
            RepeatCount = 1;
        }

        if (CurrentIterationIndex < 1)
        {
            CurrentIterationIndex = 1;
        }
    }

    private void OnStateChanged(object? sender, RunExecutionState state)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            RunId = state.RunId;
            IsRunning = state.IsRunning;
            FinalStatus = state.FinalStatus;

            StatusText = state.IsRunning 
                ? $"Running... Node: {state.CurrentNodeId}"
                : state.FinalStatus?.ToString() ?? "Completed";

            // Load events when run completes or periodically during execution
            if (!string.IsNullOrEmpty(RunId))
            {
                _ = LoadEventsAsync(RunId);
            }

            if (Iterations.Count == 0 && state.Nodes.Count > 0)
            {
                BuildIterations(state.Nodes);
            }

            if (!string.IsNullOrEmpty(state.CurrentNodeId) && state.CurrentNodeId != _lastCurrentNodeId)
            {
                HandleNodeStarted(state.CurrentNodeId);
                _lastCurrentNodeId = state.CurrentNodeId;
            }

            // Incremental node update - avoid clearing/rebuilding the collection
            // to prevent UI flicker and losing selection
            for (int i = 0; i < state.Nodes.Count; i++)
            {
                var nodeState = state.Nodes[i];
                
                if (_nodeViewModelDict.TryGetValue(nodeState.NodeId, out var existingVm))
                {
                    var wasRunning = existingVm.IsRunning;

                    // Update existing node in place
                    existingVm.Status = nodeState.Status;
                    existingVm.Duration = nodeState.Duration;
                    existingVm.RetryCount = nodeState.RetryCount;
                    existingVm.IsRunning = nodeState.IsRunning;

                    if (wasRunning && !nodeState.IsRunning)
                    {
                        HandleNodeFinished(nodeState);
                    }
                    
                    // Ensure the node is at the correct position in the collection
                    int currentIndex = Nodes.IndexOf(existingVm);
                    if (currentIndex != i && currentIndex >= 0)
                    {
                        Nodes.Move(currentIndex, i);
                    }
                }
                else
                {
                    // Determine indent level (1 if has parent, 0 otherwise)
                    int indentLevel = string.IsNullOrEmpty(nodeState.ParentNodeId) ? 0 : 1;
                    
                    // Add new node at the correct position
                    var newVm = new NodeExecutionStateViewModel
                    {
                        NodeId = nodeState.NodeId,
                        NodeType = nodeState.NodeType,
                        TestId = nodeState.TestId,
                        TestVersion = nodeState.TestVersion,
                        TestName = nodeState.TestName,
                        SuiteName = nodeState.SuiteName,
                        PlanName = nodeState.PlanName,
                        Status = nodeState.Status,
                        Duration = nodeState.Duration,
                        RetryCount = nodeState.RetryCount,
                        IsRunning = nodeState.IsRunning,
                        ParentNodeId = nodeState.ParentNodeId,
                        IndentLevel = indentLevel
                    };
                    _nodeViewModelDict[nodeState.NodeId] = newVm;
                    
                    // Insert at the correct position to match state.Nodes order
                    if (i < Nodes.Count)
                    {
                        Nodes.Insert(i, newVm);
                    }
                    else
                    {
                        Nodes.Add(newVm);
                    }
                }
            }

            if (!state.IsRunning && state.FinalStatus == RunStatus.Aborted)
            {
                MarkRemainingIterationsAborted();
            }
        });
    }

    private async Task LoadEventsAsync(string runId)
    {
        try
        {
            await foreach (var batch in _runRepository.StreamEventsAsync(runId))
            {
                foreach (var evt in batch.Events)
                {
                    // Check if event already exists
                    if (!Events.Any(e => e.Timestamp == evt.Timestamp && e.Message == evt.Message))
                    {
                        Events.Add(new StructuredEventViewModel
                        {
                            Timestamp = evt.Timestamp,
                            Level = evt.Level,
                            Source = evt.Source,
                            NodeId = evt.NodeId,
                            Type = evt.Type,
                            Code = evt.Code,
                            Message = evt.Message,
                            Exception = evt.Exception,
                            StackTrace = evt.StackTrace,
                            FilePath = evt.FilePath,
                            RawJson = evt.RawJson
                        });
                    }
                }
            }
        }
        catch
        {
            // Silently ignore errors loading events
        }
    }

    private void OnConsoleOutput(object? sender, string output)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _consoleBuffer.AppendLine(output);
            ConsoleOutput = _consoleBuffer.ToString();
        });
    }

    partial void OnRunTypeChanged(RunType value)
    {
        if (ShowTargetSelector)
        {
            _ = LoadAvailableTargetsAsync();
        }
        _ = LoadRepeatCountAsync();
    }

    partial void OnTargetIdentityChanged(string value)
    {
        if (ShowTargetSelector)
        {
            _ = LoadRepeatCountAsync();
        }
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (string.IsNullOrEmpty(TargetIdentity))
        {
            StatusText = "No target specified";
            return;
        }

        await LoadRepeatCountAsync();

        _consoleBuffer.Clear();
        _eventsBuffer.Clear();
        ConsoleOutput = string.Empty;
        EventsOutput = "(Events are available in Logs & Results after execution completes)";
        
        // Clear previous nodes for new run
        _nodeViewModelDict.Clear();
        Nodes.Clear();
        ResetIterationState();

        _runCts = new CancellationTokenSource();

        var request = new RunRequest();
        
        // Apply parameter overrides if available (for test cases)
        if (_parameterOverrides is not null && _parameterOverrides.Count > 0 && RunType == RunType.TestCase)
        {
            request.CaseInputs = _parameterOverrides.ToDictionary(
                kvp => kvp.Key,
                kvp => System.Text.Json.JsonSerializer.SerializeToElement(kvp.Value)
            );
        }
        
        switch (RunType)
        {
            case RunType.TestCase:
                request.TestCase = TargetIdentity;
                break;
            case RunType.TestSuite:
                request.Suite = TargetIdentity;
                break;
            case RunType.TestPlan:
                request.Plan = TargetIdentity;
                break;
        }

        try
        {
            ShowTargetSelector = false;
            await _runService.ExecuteAsync(request, _runCts.Token);
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        if (string.IsNullOrEmpty(RunId)) return;
        await _runService.StopAsync(RunId);
        StatusText = "Stopping...";
    }

    [RelayCommand]
    private async Task AbortAsync()
    {
        if (string.IsNullOrEmpty(RunId)) return;
        await _runService.AbortAsync(RunId);
        _runCts?.Cancel();
        StatusText = "Aborted";
    }

    [RelayCommand]
    private void ViewHistory()
    {
        if (!string.IsNullOrEmpty(RunId))
        {
            _navigationService.NavigateTo("History", RunId);
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        if (!string.IsNullOrEmpty(_sourcePage))
        {
            // Navigate back with tab index and target identity
            var parameter = _sourceTabIndex.HasValue
                ? new PlanNavigationParameter
                {
                    TabIndex = _sourceTabIndex.Value,
                    TargetIdentity = _sourceTargetIdentity
                }
                : null;
            _navigationService.NavigateTo(_sourcePage, parameter);
        }
    }

    public bool CanStart => !IsRunning && !string.IsNullOrEmpty(TargetIdentity);
    public bool CanStop => IsRunning;

    private void ResetIterationState()
    {
        Iterations.Clear();
        _plannedNodeIds.Clear();
        _iterationNodeLookup.Clear();
        _activeIterationByNodeId.Clear();
        _nodeStartCount = 0;
        _lastCurrentNodeId = string.Empty;
        CurrentIterationIndex = 1;
    }

    private void BuildIterations(IReadOnlyList<NodeExecutionState> nodes)
    {
        _plannedNodeIds.Clear();
        _iterationNodeLookup.Clear();

        foreach (var node in nodes)
        {
            _plannedNodeIds.Add(node.NodeId);
        }

        var iterationsToCreate = Math.Max(1, RepeatCount);
        for (var i = 0; i < iterationsToCreate; i++)
        {
            var iteration = new IterationExecutionStateViewModel
            {
                Index = i + 1,
                Total = iterationsToCreate,
                IsExpanded = i == 0,
            };

            foreach (var node in nodes)
            {
                int indentLevel = string.IsNullOrEmpty(node.ParentNodeId) ? 0 : 1;
                var caseVm = new NodeExecutionStateViewModel
                {
                    NodeId = node.NodeId,
                    NodeType = node.NodeType,
                    TestId = node.TestId,
                    TestVersion = node.TestVersion,
                    TestName = node.TestName,
                    SuiteName = node.SuiteName,
                    PlanName = node.PlanName,
                    Status = null,
                    Duration = null,
                    RetryCount = 0,
                    IsRunning = false,
                    ParentNodeId = node.ParentNodeId,
                    IndentLevel = indentLevel
                };

                iteration.Cases.Add(caseVm);
                _iterationNodeLookup[(i, node.NodeId)] = caseVm;
            }

            iteration.RefreshAggregate();
            Iterations.Add(iteration);
        }
    }

    private void HandleNodeStarted(string nodeId)
    {
        if (_plannedNodeIds.Count == 0 || Iterations.Count == 0)
        {
            return;
        }

        var totalNodes = _plannedNodeIds.Count;
        var iterationIndex = Math.Min(_nodeStartCount / totalNodes, Iterations.Count - 1);
        _nodeStartCount++;

        CurrentIterationIndex = iterationIndex + 1;
        UpdateIterationExpansion(iterationIndex);

        if (_iterationNodeLookup.TryGetValue((iterationIndex, nodeId), out var caseVm))
        {
            caseVm.IsRunning = true;
            caseVm.Status = null;
            caseVm.Duration = null;
        }

        var iteration = Iterations[iterationIndex];
        iteration.MarkStarted();
        iteration.RefreshAggregate();
        _activeIterationByNodeId[nodeId] = iterationIndex;
    }

    private void HandleNodeFinished(NodeExecutionState nodeState)
    {
        if (Iterations.Count == 0)
        {
            return;
        }

        var iterationIndex = _activeIterationByNodeId.TryGetValue(nodeState.NodeId, out var activeIndex)
            ? activeIndex
            : Math.Max(CurrentIterationIndex - 1, 0);

        if (_iterationNodeLookup.TryGetValue((iterationIndex, nodeState.NodeId), out var caseVm))
        {
            caseVm.IsRunning = false;
            caseVm.Status = nodeState.Status;
            caseVm.Duration = nodeState.Duration;
        }

        if (iterationIndex >= 0 && iterationIndex < Iterations.Count)
        {
            var iteration = Iterations[iterationIndex];
            iteration.RefreshAggregate();
            if (iteration.IsCompleted)
            {
                iteration.MarkCompleted();
            }
        }

        if (_activeIterationByNodeId.ContainsKey(nodeState.NodeId))
        {
            _activeIterationByNodeId.Remove(nodeState.NodeId);
        }
    }

    private void UpdateIterationExpansion(int currentIndex)
    {
        for (var i = 0; i < Iterations.Count; i++)
        {
            if (i == currentIndex)
            {
                Iterations[i].IsExpanded = true;
            }
            else if (Iterations[i].IsCompleted)
            {
                Iterations[i].IsExpanded = false;
            }
        }
    }

    private void MarkRemainingIterationsAborted()
    {
        foreach (var iteration in Iterations)
        {
            foreach (var caseVm in iteration.Cases)
            {
                if (caseVm.Status is null)
                {
                    caseVm.Status = RunStatus.Aborted;
                    caseVm.IsRunning = false;
                }
            }
            iteration.RefreshAggregate();
        }
    }
}

/// <summary>
/// ViewModel for node execution state.
/// </summary>
public partial class NodeExecutionStateViewModel : ViewModelBase
{
    [ObservableProperty] private string _nodeId = string.Empty;
    [ObservableProperty] private string _testId = string.Empty;
    [ObservableProperty] private string _testVersion = string.Empty;
    [ObservableProperty] private string? _testName;
    [ObservableProperty] private string? _suiteName;
    [ObservableProperty] private string? _planName;
    [ObservableProperty] private RunType _nodeType;
    [ObservableProperty] private RunStatus? _status;
    [ObservableProperty] private TimeSpan? _duration;
    [ObservableProperty] private int _retryCount;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string? _parentNodeId;
    [ObservableProperty] private int _indentLevel; // 0 for top-level, 1 for nested

    public string StatusDisplay => Status?.ToString() ?? (IsRunning ? "Running..." : "Pending");
    public string DurationDisplay => Duration?.ToString(@"mm\:ss\.fff") ?? "-";

    partial void OnStatusChanged(RunStatus? value) => OnPropertyChanged(nameof(StatusDisplay));
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(StatusDisplay));
    partial void OnDurationChanged(TimeSpan? value) => OnPropertyChanged(nameof(DurationDisplay));
    
    /// <summary>
    /// Display name only (without version).
    /// </summary>
    public string DisplayName
    {
        get
        {
            return NodeType switch
            {
                RunType.TestCase => TestName ?? TestId,
                RunType.TestSuite => SuiteName ?? TestId,
                RunType.TestPlan => PlanName ?? TestId,
                _ => TestId
            };
        }
    }
    
    /// <summary>
    /// Display identity as Id@Version.
    /// </summary>
    public string DisplayIdentity => $"{TestId}@{TestVersion}";
    
    public string Identity
    {
        get
        {
            var name = NodeType switch
            {
                RunType.TestCase => TestName ?? TestId,
                RunType.TestSuite => SuiteName ?? TestId,
                RunType.TestPlan => PlanName ?? TestId,
                _ => TestId
            };
            return $"{name}@{TestVersion}";
        }
    }
}

/// <summary>
/// ViewModel for iteration execution state grouping.
/// </summary>
public partial class IterationExecutionStateViewModel : ViewModelBase
{
    [ObservableProperty] private int _index;
    [ObservableProperty] private int _total;
    [ObservableProperty] private RunStatus? _status;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private TimeSpan? _duration;
    [ObservableProperty] private int _completedCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private ObservableCollection<NodeExecutionStateViewModel> _cases = new();

    private DateTime? _startTime;
    private DateTime? _endTime;

    public string IterationLabel => $"Iteration {Index}/{Total}";
    public bool IsHeaderVisible => Total > 1;
    public bool IsCompleted => CompletedCount >= TotalCount && TotalCount > 0;
    public string ProgressDisplay => $"{CompletedCount}/{TotalCount}";
    public double ProgressValue => TotalCount == 0 ? 0 : (double)CompletedCount / TotalCount * 100;
    public string StatusText => IsRunning ? "Running" : Status?.ToString() ?? "Pending";
    public string DurationDisplay => Duration?.ToString(@"mm\:ss\.fff") ?? "-";

    public void MarkStarted()
    {
        if (_startTime is null)
        {
            _startTime = DateTime.UtcNow;
        }
    }

    public void MarkCompleted()
    {
        _endTime ??= DateTime.UtcNow;
    }

    public void RefreshAggregate()
    {
        TotalCount = Cases.Count;
        CompletedCount = Cases.Count(c => c.Status is not null);
        IsRunning = Cases.Any(c => c.IsRunning);

        if (IsRunning)
        {
            Status = null;
        }
        else if (Cases.Any(c => c.Status == RunStatus.Aborted))
        {
            Status = RunStatus.Aborted;
        }
        else if (Cases.Any(c => c.Status is RunStatus.Failed or RunStatus.Error or RunStatus.Timeout))
        {
            Status = RunStatus.Failed;
        }
        else if (IsCompleted)
        {
            Status = RunStatus.Passed;
        }
        else
        {
            Status = null;
        }

        if (_startTime.HasValue)
        {
            var endTime = _endTime ?? DateTime.UtcNow;
            Duration = endTime - _startTime.Value;
        }
        else
        {
            Duration = null;
        }

        OnPropertyChanged(nameof(IterationLabel));
        OnPropertyChanged(nameof(IsHeaderVisible));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(ProgressDisplay));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DurationDisplay));
    }
}
