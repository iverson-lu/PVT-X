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

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _showTargetSelector = true;
    [ObservableProperty] private string _targetIdentity = string.Empty;
    [ObservableProperty] private RunType _runType = RunType.TestCase;
    [ObservableProperty] private string _runId = string.Empty;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private RunStatus? _finalStatus;

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
    }

    public async void Initialize(object? parameter)
    {
        if (parameter is RunNavigationParameter navParam)
        {
            TargetIdentity = navParam.TargetIdentity;
            RunType = navParam.RunType;
            _parameterOverrides = navParam.ParameterOverrides;
            StatusText = $"Ready to run {RunType}: {TargetIdentity}";
            ShowTargetSelector = false;
            
            // Auto-start if requested
            if (navParam.AutoStart)
            {
                await StartAsync();
            }
        }
        else
        {
            ShowTargetSelector = true;
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
                        TestId = nodeState.TestId,
                        TestVersion = nodeState.TestVersion,
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

    public bool CanStart => !IsRunning && !string.IsNullOrEmpty(TargetIdentity);
    public bool CanStop => IsRunning;
}

/// <summary>
/// ViewModel for node execution state.
/// </summary>
public partial class NodeExecutionStateViewModel : ViewModelBase
{
    [ObservableProperty] private string _nodeId = string.Empty;
    [ObservableProperty] private string _testId = string.Empty;
    [ObservableProperty] private string _testVersion = string.Empty;
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
    public string Identity => $"{TestId}@{TestVersion}";
}
