using System.Collections.ObjectModel;
using System.Text;
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
    private string _consoleOutput = string.Empty;

    [ObservableProperty]
    private string _eventsOutput = string.Empty;

    [ObservableProperty]
    private ObservableCollection<StructuredEventViewModel> _events = new();

    [ObservableProperty]
    private ObservableCollection<IterationExecutionStateViewModel> _iterations = new();

    [ObservableProperty]
    private int _repeatCount = 1;

    [ObservableProperty]
    private int _currentIterationIndex = 1;

    [ObservableProperty]
    private bool _showRepeatStatus;

    [ObservableProperty]
    private string _repeatStatusText = string.Empty;

    private readonly StringBuilder _consoleBuffer = new();
    private readonly StringBuilder _eventsBuffer = new();
    private readonly Dictionary<string, NodeExecutionSnapshot> _nodeSnapshots = new();
    private bool _iterationsInitialized;

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

            // Incremental node update - avoid clearing/rebuilding the collection
            // to prevent UI flicker and losing selection
            for (int i = 0; i < state.Nodes.Count; i++)
            {
                var nodeState = state.Nodes[i];
                
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
            }

            UpdateIterations(state);
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

    partial void OnRepeatCountChanged(int value)
    {
        ShowRepeatStatus = value > 1;
        UpdateRepeatStatusText();
    }

    partial void OnCurrentIterationIndexChanged(int value) => UpdateRepeatStatusText();

    private void UpdateRepeatStatusText()
    {
        RepeatStatusText = RepeatCount > 1
            ? $"Repeat {RepeatCount}x \u00b7 Iteration {CurrentIterationIndex}/{RepeatCount}"
            : string.Empty;
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
        ResetIterations();

        _runCts = new CancellationTokenSource();

        var request = new RunRequest();
        RepeatCount = await ResolveRepeatCountAsync();
        
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

    private async Task<int> ResolveRepeatCountAsync()
    {
        if (RunType != RunType.TestSuite)
        {
            return 1;
        }

        var suite = await _suiteRepository.GetByIdentityAsync(TargetIdentity);
        return Math.Max(1, suite?.Manifest.Controls?.Repeat ?? 1);
    }

    private void ResetIterations()
    {
        Iterations.Clear();
        _nodeSnapshots.Clear();
        _iterationsInitialized = false;
        CurrentIterationIndex = 1;
    }

    private void InitializeIterations(IReadOnlyList<NodeExecutionState> nodes)
    {
        Iterations.Clear();
        if (nodes.Count == 0)
        {
            return;
        }

        var totalCases = nodes.Count;
        for (var index = 1; index <= RepeatCount; index++)
        {
            var iteration = new IterationExecutionStateViewModel(index, RepeatCount, totalCases)
            {
                IsExpanded = index == CurrentIterationIndex
            };

            foreach (var node in nodes)
            {
                iteration.AddCase(BuildCaseViewModel(node, isRunning: false, status: null));
            }
            iteration.RefreshSummary();
            Iterations.Add(iteration);
        }

        _iterationsInitialized = true;
    }

    private void UpdateIterations(RunExecutionState state)
    {
        if (!_iterationsInitialized && state.Nodes.Count > 0)
        {
            InitializeIterations(state.Nodes);
        }

        if (Iterations.Count == 0)
        {
            return;
        }

        foreach (var nodeState in state.Nodes)
        {
            if (!_nodeSnapshots.TryGetValue(nodeState.NodeId, out var snapshot))
            {
                snapshot = new NodeExecutionSnapshot();
            }

            var started = !snapshot.IsRunning && nodeState.IsRunning;
            var finished = snapshot.IsRunning && !nodeState.IsRunning;

            if (started)
            {
                var currentIteration = Iterations[CurrentIterationIndex - 1];
                if (currentIteration.CompletedCount >= currentIteration.TotalCount
                    && CurrentIterationIndex < RepeatCount)
                {
                    SetCurrentIteration(CurrentIterationIndex + 1);
                }

                UpdateIterationCase(CurrentIterationIndex, nodeState, isRunning: true);
            }
            else if (finished)
            {
                UpdateIterationCase(CurrentIterationIndex, nodeState, isRunning: false);
            }
            else
            {
                if (nodeState.IsRunning)
                {
                    UpdateIterationCase(CurrentIterationIndex, nodeState, isRunning: true, preserveStatus: true);
                }
                else if (nodeState.Status is not null)
                {
                    UpdateIterationCase(CurrentIterationIndex, nodeState, isRunning: false, preserveStatus: true);
                }
            }

            snapshot.IsRunning = nodeState.IsRunning;
            snapshot.Status = nodeState.Status;
            _nodeSnapshots[nodeState.NodeId] = snapshot;
        }

        foreach (var iteration in Iterations)
        {
            iteration.RefreshSummary();
        }
    }

    private void UpdateIterationCase(
        int iterationIndex,
        NodeExecutionState nodeState,
        bool isRunning,
        bool preserveStatus = false)
    {
        if (iterationIndex < 1 || iterationIndex > Iterations.Count)
        {
            return;
        }

        var iteration = Iterations[iterationIndex - 1];
        var caseVm = iteration.GetOrCreateCase(
            nodeState.NodeId,
            () => BuildCaseViewModel(nodeState, isRunning: false, status: null));

        caseVm.IsRunning = isRunning;
        if (isRunning)
        {
            caseVm.Status = null;
            caseVm.Duration = null;
            return;
        }

        if (!preserveStatus)
        {
            caseVm.Status = nodeState.Status;
            caseVm.Duration = nodeState.Duration;
        }
        else
        {
            caseVm.Status = nodeState.Status;
            caseVm.Duration = nodeState.Duration;
        }
    }

    private static NodeExecutionStateViewModel BuildCaseViewModel(
        NodeExecutionState nodeState,
        bool isRunning,
        RunStatus? status)
    {
        var indentLevel = string.IsNullOrEmpty(nodeState.ParentNodeId) ? 0 : 1;

        return new NodeExecutionStateViewModel
        {
            NodeId = nodeState.NodeId,
            NodeType = nodeState.NodeType,
            TestId = nodeState.TestId,
            TestVersion = nodeState.TestVersion,
            TestName = nodeState.TestName,
            SuiteName = nodeState.SuiteName,
            PlanName = nodeState.PlanName,
            Status = status,
            Duration = nodeState.Duration,
            RetryCount = nodeState.RetryCount,
            IsRunning = isRunning,
            ParentNodeId = nodeState.ParentNodeId,
            IndentLevel = indentLevel
        };
    }

    private void SetCurrentIteration(int index)
    {
        if (index < 1 || index > RepeatCount)
        {
            return;
        }

        CurrentIterationIndex = index;
        for (var i = 0; i < Iterations.Count; i++)
        {
            Iterations[i].IsExpanded = Iterations[i].Index == index;
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

public sealed partial class IterationExecutionStateViewModel : ViewModelBase
{
    private readonly Dictionary<string, NodeExecutionStateViewModel> _caseLookup = new();

    public IterationExecutionStateViewModel(int index, int totalIterations, int totalCount)
    {
        Index = index;
        TotalIterations = totalIterations;
        TotalCount = totalCount;
    }

    [ObservableProperty] private int _index;
    [ObservableProperty] private int _totalIterations;
    [ObservableProperty] private RunStatus? _status;
    [ObservableProperty] private TimeSpan? _duration;
    [ObservableProperty] private int _completedCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isExpanded;

    public ObservableCollection<NodeExecutionStateViewModel> Cases { get; } = new();

    public string IterationLabel => $"Iteration {Index}/{TotalIterations}";
    public string StatusDisplay => IsRunning ? "Running" : Status?.ToString() ?? "Pending";
    public string DurationDisplay => Duration?.ToString(@"mm\:ss") ?? "-";
    public string ProgressDisplay => $"{CompletedCount}/{TotalCount}";
    public double ProgressValue => TotalCount == 0 ? 0 : (double)CompletedCount / TotalCount;

    public void AddCase(NodeExecutionStateViewModel caseVm)
    {
        if (_caseLookup.ContainsKey(caseVm.NodeId))
        {
            return;
        }

        _caseLookup[caseVm.NodeId] = caseVm;
        Cases.Add(caseVm);
    }

    public NodeExecutionStateViewModel GetOrCreateCase(string nodeId, Func<NodeExecutionStateViewModel> factory)
    {
        if (_caseLookup.TryGetValue(nodeId, out var existing))
        {
            return existing;
        }

        var created = factory();
        AddCase(created);
        return created;
    }

    public void RefreshSummary()
    {
        var completed = 0;
        var running = false;
        var hasPassed = false;
        var hasFailed = false;
        var hasError = false;
        var hasTimeout = false;
        var hasAborted = false;
        TimeSpan totalDuration = TimeSpan.Zero;
        var hasDuration = false;

        foreach (var caseVm in Cases)
        {
            running |= caseVm.IsRunning;

            if (caseVm.Status.HasValue)
            {
                completed++;
                switch (caseVm.Status.Value)
                {
                    case RunStatus.Passed:
                        hasPassed = true;
                        break;
                    case RunStatus.Failed:
                        hasFailed = true;
                        break;
                    case RunStatus.Error:
                        hasError = true;
                        break;
                    case RunStatus.Timeout:
                        hasTimeout = true;
                        break;
                    case RunStatus.Aborted:
                        hasAborted = true;
                        break;
                }
            }

            if (caseVm.Duration.HasValue)
            {
                totalDuration += caseVm.Duration.Value;
                hasDuration = true;
            }
        }

        CompletedCount = completed;
        IsRunning = running;
        Duration = hasDuration ? totalDuration : null;

        if (running)
        {
            Status = null;
            return;
        }

        if (hasFailed)
        {
            Status = RunStatus.Failed;
        }
        else if (hasError)
        {
            Status = RunStatus.Error;
        }
        else if (hasTimeout)
        {
            Status = RunStatus.Timeout;
        }
        else if (hasAborted)
        {
            Status = RunStatus.Aborted;
        }
        else if (completed == TotalCount && hasPassed)
        {
            Status = RunStatus.Passed;
        }
        else
        {
            Status = null;
        }
    }

    partial void OnIndexChanged(int value) => OnPropertyChanged(nameof(IterationLabel));
    partial void OnTotalIterationsChanged(int value) => OnPropertyChanged(nameof(IterationLabel));
    partial void OnStatusChanged(RunStatus? value) => OnPropertyChanged(nameof(StatusDisplay));
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(StatusDisplay));
    partial void OnDurationChanged(TimeSpan? value) => OnPropertyChanged(nameof(DurationDisplay));
    partial void OnCompletedCountChanged(int value)
    {
        OnPropertyChanged(nameof(ProgressDisplay));
        OnPropertyChanged(nameof(ProgressValue));
    }
    partial void OnTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(ProgressDisplay));
        OnPropertyChanged(nameof(ProgressValue));
    }
}

public sealed class NodeExecutionSnapshot
{
    public bool IsRunning { get; set; }
    public RunStatus? Status { get; set; }
}
