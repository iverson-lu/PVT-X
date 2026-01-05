using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTest.Contracts;
using PcTest.Contracts.Requests;
using PcTest.Contracts.Validation;
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

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _showTargetSelector = true;
    [ObservableProperty] private string _targetIdentity = string.Empty;
    [ObservableProperty] private RunType _runType = RunType.TestCase;
    [ObservableProperty] private string _runId = string.Empty;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private RunStatus? _finalStatus;

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
    private ObservableCollection<ExecutionIterationViewModel> _iterations = new();

    [ObservableProperty]
    private int _repeatCount = 1;

    [ObservableProperty]
    private int _currentIterationIndex = 1;

    [ObservableProperty]
    private string _consoleOutput = string.Empty;

    [ObservableProperty]
    private string _eventsOutput = string.Empty;

    [ObservableProperty]
    private ObservableCollection<StructuredEventViewModel> _events = new();

    private readonly StringBuilder _consoleBuffer = new();
    private readonly StringBuilder _eventsBuffer = new();
    private readonly Dictionary<string, RunStatus?> _lastKnownStatus = new();
    private readonly Dictionary<string, TimeSpan?> _lastKnownDuration = new();
    private readonly List<string> _nodeOrder = new();
    private int _currentNodeIndex;
    private bool _pendingIterationAdvance;
    private ExecutionCaseViewModel? _currentRunningCase;

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

            EnsureIterationsInitialized(state.Nodes);

            // Incremental node update - avoid clearing/rebuilding the collection
            // to prevent UI flicker and losing selection
            for (int i = 0; i < state.Nodes.Count; i++)
            {
                var nodeState = state.Nodes[i];
                _lastKnownStatus.TryGetValue(nodeState.NodeId, out var previousStatus);
                _lastKnownDuration.TryGetValue(nodeState.NodeId, out var previousDuration);
                
                if (_nodeViewModelDict.TryGetValue(nodeState.NodeId, out var existingVm))
                {
                    // Update existing node in place
                    existingVm.Status = nodeState.Status;
                    existingVm.Duration = nodeState.Duration;
                    existingVm.RetryCount = nodeState.RetryCount;
                    existingVm.IsRunning = nodeState.IsRunning;
                    
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

                if (nodeState.Status is not null
                    && !nodeState.IsRunning
                    && (previousStatus != nodeState.Status || previousDuration != nodeState.Duration))
                {
                    RecordNodeCompletion(nodeState);
                }

                _lastKnownStatus[nodeState.NodeId] = nodeState.Status;
                _lastKnownDuration[nodeState.NodeId] = nodeState.Duration;
            }

            UpdateRunningCase(state.CurrentNodeId);
            RefreshIterationSummaries();
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
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (string.IsNullOrEmpty(TargetIdentity))
        {
            StatusText = "No target specified";
            return;
        }

        _consoleBuffer.Clear();
        _eventsBuffer.Clear();
        ConsoleOutput = string.Empty;
        EventsOutput = "(Events are available in Logs & Results after execution completes)";
        
        // Clear previous nodes for new run
        _nodeViewModelDict.Clear();
        Nodes.Clear();
        ResetIterationTracking();

        RepeatCount = await ResolveRepeatCountAsync();

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

    public bool IsRepeatVisible => RepeatCount > 1;
    public string RepeatBadgeText => RepeatCount > 1
        ? $"Repeat {RepeatCount}x · Iteration {CurrentIterationIndex}/{RepeatCount}"
        : string.Empty;

    partial void OnRepeatCountChanged(int value)
    {
        OnPropertyChanged(nameof(IsRepeatVisible));
        OnPropertyChanged(nameof(RepeatBadgeText));
    }

    partial void OnCurrentIterationIndexChanged(int value)
    {
        OnPropertyChanged(nameof(RepeatBadgeText));
    }

    private async Task<int> ResolveRepeatCountAsync()
    {
        if (RunType != RunType.TestSuite)
        {
            return 1;
        }

        var discovery = _discoveryService.CurrentDiscovery ?? await _discoveryService.DiscoverAsync();
        var parseResult = IdentityParser.Parse(TargetIdentity);
        if (!parseResult.Success)
        {
            return 1;
        }

        var suite = discovery.TestSuites.Values.FirstOrDefault(suite =>
            suite.Manifest.Id.Equals(parseResult.Id, StringComparison.OrdinalIgnoreCase)
            && suite.Manifest.Version.Equals(parseResult.Version, StringComparison.OrdinalIgnoreCase));

        return Math.Max(1, suite?.Manifest.Controls?.Repeat ?? 1);
    }

    private void ResetIterationTracking()
    {
        Iterations.Clear();
        _nodeOrder.Clear();
        _lastKnownStatus.Clear();
        _lastKnownDuration.Clear();
        _currentIterationIndex = 1;
        _currentNodeIndex = 0;
        _pendingIterationAdvance = false;
        _currentRunningCase = null;
        CurrentIterationIndex = 1;
    }

    private void EnsureIterationsInitialized(IReadOnlyList<NodeExecutionState> nodes)
    {
        if (Iterations.Count > 0 || nodes.Count == 0)
        {
            return;
        }

        var orderedNodes = nodes.Where(node => node.NodeType == RunType.TestCase || RunType != RunType.TestSuite).ToList();
        if (orderedNodes.Count == 0)
        {
            orderedNodes = nodes.ToList();
        }

        _nodeOrder.Clear();
        _nodeOrder.AddRange(orderedNodes.Select(node => node.NodeId));

        var totalIterations = Math.Max(1, RepeatCount);

        for (int i = 0; i < totalIterations; i++)
        {
            var iteration = new ExecutionIterationViewModel
            {
                Index = i + 1,
                Total = totalIterations,
                IsExpanded = i == 0
            };

            foreach (var node in orderedNodes)
            {
                iteration.Cases.Add(new ExecutionCaseViewModel
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
                    IsRunning = false
                });
            }

            iteration.TotalCount = iteration.Cases.Count;
            Iterations.Add(iteration);
        }
    }

    private bool TryGetNodeIndex(string nodeId, out int index)
    {
        index = _nodeOrder.IndexOf(nodeId);
        return index >= 0;
    }

    private void UpdateRunningCase(string currentNodeId)
    {
        if (_currentRunningCase is not null && string.IsNullOrEmpty(currentNodeId))
        {
            _currentRunningCase.IsRunning = false;
            _currentRunningCase = null;
            return;
        }

        if (!TryGetNodeIndex(currentNodeId, out var nodeIndex))
        {
            return;
        }

        if (_pendingIterationAdvance && nodeIndex == 0 && _currentIterationIndex < Iterations.Count)
        {
            Iterations[_currentIterationIndex - 1].IsExpanded = false;
            _currentIterationIndex++;
            CurrentIterationIndex = _currentIterationIndex;
            _currentNodeIndex = 0;
            _pendingIterationAdvance = false;
            Iterations[_currentIterationIndex - 1].IsExpanded = true;
        }

        var iteration = Iterations[Math.Min(_currentIterationIndex - 1, Iterations.Count - 1)];
        var caseVm = iteration.Cases[nodeIndex];

        if (_currentRunningCase != caseVm)
        {
            if (_currentRunningCase is not null)
            {
                _currentRunningCase.IsRunning = false;
            }
            _currentRunningCase = caseVm;
        }

        if (!iteration.StartTime.HasValue)
        {
            iteration.StartTime = DateTime.UtcNow;
        }

        caseVm.IsRunning = true;
        _currentNodeIndex = Math.Max(_currentNodeIndex, nodeIndex);
    }

    private void RecordNodeCompletion(NodeExecutionState nodeState)
    {
        if (!TryGetNodeIndex(nodeState.NodeId, out var nodeIndex))
        {
            return;
        }

        var iteration = Iterations[Math.Min(_currentIterationIndex - 1, Iterations.Count - 1)];

        if (nodeIndex >= iteration.Cases.Count)
        {
            return;
        }

        if (!iteration.StartTime.HasValue)
        {
            iteration.StartTime = DateTime.UtcNow;
        }

        var caseVm = iteration.Cases[nodeIndex];
        caseVm.Status = nodeState.Status;
        caseVm.Duration = nodeState.Duration;
        caseVm.IsRunning = false;

        iteration.EndTime = DateTime.UtcNow;
        _currentNodeIndex = Math.Max(_currentNodeIndex, nodeIndex + 1);

        if (_currentNodeIndex >= _nodeOrder.Count)
        {
            _pendingIterationAdvance = true;
        }
    }

    private void RefreshIterationSummaries()
    {
        var now = DateTime.UtcNow;

        foreach (var iteration in Iterations)
        {
            iteration.CompletedCount = iteration.Cases.Count(c => c.Status is not null);
            iteration.TotalCount = iteration.Cases.Count;
            iteration.IsRunning = iteration.Cases.Any(c => c.IsRunning);
            iteration.AggregatedStatus = ComputeAggregateStatus(iteration);
            iteration.UpdateDuration(now);
        }
    }

    private static RunStatus? ComputeAggregateStatus(ExecutionIterationViewModel iteration)
    {
        if (iteration.IsRunning)
        {
            return null;
        }

        var statuses = iteration.Cases
            .Select(c => c.Status)
            .Where(status => status.HasValue)
            .Select(status => status!.Value)
            .ToList();

        if (statuses.Count == 0)
        {
            return null;
        }

        if (statuses.Contains(RunStatus.Aborted))
        {
            return RunStatus.Aborted;
        }

        if (statuses.Any(status => status is RunStatus.Failed or RunStatus.Error or RunStatus.Timeout))
        {
            return RunStatus.Failed;
        }

        return statuses.Count == iteration.TotalCount ? RunStatus.Passed : null;
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

public partial class ExecutionIterationViewModel : ViewModelBase
{
    [ObservableProperty] private int _index;
    [ObservableProperty] private int _total;
    [ObservableProperty] private ObservableCollection<ExecutionCaseViewModel> _cases = new();
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private RunStatus? _aggregatedStatus;
    [ObservableProperty] private int _completedCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private TimeSpan? _duration;
    [ObservableProperty] private DateTime? _startTime;
    [ObservableProperty] private DateTime? _endTime;

    public string IterationLabel => $"Iteration {Index}/{Total}";
    public string ProgressDisplay => $"{CompletedCount}/{TotalCount}";
    public string DurationDisplay => Duration?.ToString(@"mm\:ss\.fff") ?? "-";
    public string StatusText => IsRunning ? "Running" : AggregatedStatus?.ToString() ?? "Pending";

    public void UpdateDuration(DateTime now)
    {
        if (StartTime.HasValue)
        {
            var endTime = EndTime ?? now;
            Duration = endTime - StartTime.Value;
        }
        else
        {
            Duration = null;
        }
        OnPropertyChanged(nameof(DurationDisplay));
    }

    partial void OnIndexChanged(int value) => OnPropertyChanged(nameof(IterationLabel));
    partial void OnTotalChanged(int value) => OnPropertyChanged(nameof(IterationLabel));
    partial void OnCompletedCountChanged(int value) => OnPropertyChanged(nameof(ProgressDisplay));
    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(ProgressDisplay));
    partial void OnAggregatedStatusChanged(RunStatus? value) => OnPropertyChanged(nameof(StatusText));
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(StatusText));
}

public partial class ExecutionCaseViewModel : ViewModelBase
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
    [ObservableProperty] private bool _isRunning;

    public string StatusDisplay => Status?.ToString() ?? (IsRunning ? "Running..." : "Pending");
    public string DurationDisplay => Duration?.ToString(@"mm\:ss\.fff") ?? "-";

    partial void OnStatusChanged(RunStatus? value) => OnPropertyChanged(nameof(StatusDisplay));
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(StatusDisplay));
    partial void OnDurationChanged(TimeSpan? value) => OnPropertyChanged(nameof(DurationDisplay));

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

    public string DisplayIdentity => $"{TestId}@{TestVersion}";
}
