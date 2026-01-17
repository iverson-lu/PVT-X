using System.Collections.ObjectModel;
using System.Linq;
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
    private readonly IFileDialogService _fileDialogService;

    private CancellationTokenSource? _runCts;
    private Dictionary<string, object?>? _parameterOverrides;
    private readonly Dictionary<string, NodeExecutionStateViewModel> _nodeViewModelDict = new();
    private readonly List<PlannedNodeTemplate> _plannedNodeTemplates = new();
    private readonly Dictionary<string, PlanSuiteExecutionViewModel> _suiteViewModels = new();
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
    private ObservableCollection<IterationExecutionViewModel> _iterations = new();

    [ObservableProperty]
    private int _repeatCount = 1;

    [ObservableProperty]
    private int _currentIterationIndex = 1;

    [ObservableProperty]
    private ObservableCollection<PlanSuiteExecutionViewModel> _suiteGroups = new();

    [ObservableProperty]
    private string _consoleOutput = string.Empty;

    [ObservableProperty]
    private string _eventsOutput = string.Empty;

    [ObservableProperty]
    private ObservableCollection<StructuredEventViewModel> _events = new();

    private readonly StringBuilder _consoleBuffer = new();
    private readonly StringBuilder _eventsBuffer = new();

    public bool ShowRepeatIndicator => RepeatCount > 1;
    public string RepeatIterationBadgeText => RepeatCount > 1
        ? $"Repeat {RepeatCount}x · Iteration {CurrentIterationIndex}/{RepeatCount}"
        : string.Empty;
    public bool ShowIterationGroups => RunType == RunType.TestSuite;
    public bool ShowPlanPipeline => RunType == RunType.TestPlan;
    public bool ShowFlatPipeline => RunType == RunType.TestCase;

    public RunViewModel(
        IRunService runService, 
        INavigationService navigationService,
        IDiscoveryService discoveryService,
        ISuiteRepository suiteRepository,
        IPlanRepository planRepository,
        IRunRepository runRepository,
        IFileDialogService fileDialogService)
    {
        _runService = runService;
        _navigationService = navigationService;
        _discoveryService = discoveryService;
        _suiteRepository = suiteRepository;
        _planRepository = planRepository;
        _runRepository = runRepository;
        _fileDialogService = fileDialogService;

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

    partial void OnRepeatCountChanged(int value)
    {
        OnPropertyChanged(nameof(ShowRepeatIndicator));
        OnPropertyChanged(nameof(RepeatIterationBadgeText));
    }

    partial void OnCurrentIterationIndexChanged(int value)
    {
        OnPropertyChanged(nameof(RepeatIterationBadgeText));
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

    private async Task LoadRepeatCountAsync()
    {
        RepeatCount = 1;

        if (RunType != RunType.TestSuite || string.IsNullOrEmpty(TargetIdentity))
        {
            return;
        }

        try
        {
            var suiteInfo = await _suiteRepository.GetByIdentityAsync(TargetIdentity);
            RepeatCount = Math.Max(1, suiteInfo?.Manifest.Controls?.Repeat ?? 1);
        }
        catch
        {
            RepeatCount = 1;
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

            UpdateIterations(state);
        });
    }

    private void UpdateIterations(RunExecutionState state)
    {
        if (!ShowIterationGroups)
        {
            if (ShowPlanPipeline)
            {
                UpdatePlanSuites(state);
            }
            else
            {
                UpdateFlatNodes(state);
            }
            return;
        }

        if (RepeatCount > 1 && _plannedNodeTemplates.Count == 0)
        {
            var templates = state.Nodes
                .Where(node => node.IterationIndex == 0)
                .OrderBy(node => node.SequenceIndex)
                .Select(node => new PlannedNodeTemplate(node))
                .ToList();

            if (templates.Count > 0)
            {
                _plannedNodeTemplates.AddRange(templates);
            }
        }

        var maxIterationIndex = state.Nodes.Count > 0 ? state.Nodes.Max(node => node.IterationIndex) : 0;
        var totalIterations = RepeatCount > 1 ? Math.Max(RepeatCount, maxIterationIndex + 1) : maxIterationIndex + 1;
        if (totalIterations <= 0)
        {
            totalIterations = 1;
        }

        EnsureIterationViewModels(totalIterations);

        var stateNodeLookup = state.Nodes.ToDictionary(BuildNodeKey);

        var flattenedNodes = new List<NodeExecutionStateViewModel>();

        for (var iterationIndex = 0; iterationIndex < totalIterations; iterationIndex++)
        {
            var iterationVm = Iterations[iterationIndex];
            iterationVm.Index = iterationIndex + 1;
            iterationVm.Total = totalIterations;

            var cases = BuildIterationCases(iterationIndex, stateNodeLookup);
            SyncCases(iterationVm.Cases, cases);
            flattenedNodes.AddRange(iterationVm.Cases);

            UpdateIterationSummary(iterationVm);
        }

        UpdateCurrentIteration();
        Nodes = new ObservableCollection<NodeExecutionStateViewModel>(flattenedNodes);
    }

    private void UpdatePlanSuites(RunExecutionState state)
    {
        var suites = state.Nodes
            .Where(node => node.ParentNodeId is null)
            .OrderBy(node => state.Nodes.IndexOf(node))
            .ToList();

        var orderedSuiteGroups = new List<PlanSuiteExecutionViewModel>();

        foreach (var suiteState in suites)
        {
            if (!_suiteViewModels.TryGetValue(suiteState.NodeId, out var suiteVm))
            {
                suiteVm = new PlanSuiteExecutionViewModel
                {
                    SuiteNode = GetOrCreateNodeViewModel(suiteState)
                };
                _suiteViewModels[suiteState.NodeId] = suiteVm;
            }
            else
            {
                UpdateNodeViewModel(suiteVm.SuiteNode, suiteState);
            }

            var children = state.Nodes
                .Where(node => node.ParentNodeId == suiteState.NodeId)
                .OrderBy(node => node.SequenceIndex)
                .ToList();

            var maxIterationIndex = children.Count > 0 ? children.Max(node => node.IterationIndex) : 0;
            var totalIterations = maxIterationIndex + 1;

            suiteVm.ShowIterations = totalIterations > 1;
            if (suiteVm.ShowIterations)
            {
                EnsureIterationViewModels(suiteVm.Iterations, totalIterations);

                for (var iterationIndex = 0; iterationIndex < totalIterations; iterationIndex++)
                {
                    var iterationVm = suiteVm.Iterations[iterationIndex];
                    iterationVm.Index = iterationIndex + 1;
                    iterationVm.Total = totalIterations;
                    var iterationCases = children
                        .Where(node => node.IterationIndex == iterationIndex)
                        .OrderBy(node => node.SequenceIndex)
                        .Select(GetOrCreateNodeViewModel)
                        .ToList();

                    SyncCases(iterationVm.Cases, iterationCases);
                    UpdateIterationSummary(iterationVm);
                }
            }
            else
            {
                var suiteCases = children
                    .Where(node => node.IterationIndex == 0)
                    .OrderBy(node => node.SequenceIndex)
                    .Select(GetOrCreateNodeViewModel)
                    .ToList();
                SyncCases(suiteVm.Cases, suiteCases);
            }

            orderedSuiteGroups.Add(suiteVm);
        }

        SuiteGroups = new ObservableCollection<PlanSuiteExecutionViewModel>(orderedSuiteGroups);
    }

    private void UpdateFlatNodes(RunExecutionState state)
    {
        for (int i = 0; i < state.Nodes.Count; i++)
        {
            var nodeState = state.Nodes[i];
            var key = BuildNodeKey(nodeState);

            if (_nodeViewModelDict.TryGetValue(key, out var existingVm))
            {
                UpdateNodeViewModel(existingVm, nodeState);

                int currentIndex = Nodes.IndexOf(existingVm);
                if (currentIndex != i && currentIndex >= 0)
                {
                    Nodes.Move(currentIndex, i);
                }
            }
            else
            {
                var newVm = GetOrCreateNodeViewModel(nodeState);
                _nodeViewModelDict[key] = newVm;

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
    }

    private void EnsureIterationViewModels(int totalIterations)
    {
        while (Iterations.Count < totalIterations)
        {
            Iterations.Add(new IterationExecutionViewModel
            {
                Index = Iterations.Count + 1,
                Total = totalIterations
            });
        }

        while (Iterations.Count > totalIterations)
        {
            Iterations.RemoveAt(Iterations.Count - 1);
        }
    }

    private static void EnsureIterationViewModels(ObservableCollection<IterationExecutionViewModel> target, int totalIterations)
    {
        while (target.Count < totalIterations)
        {
            target.Add(new IterationExecutionViewModel
            {
                Index = target.Count + 1,
                Total = totalIterations
            });
        }

        while (target.Count > totalIterations)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private List<NodeExecutionStateViewModel> BuildIterationCases(
        int iterationIndex,
        IReadOnlyDictionary<string, NodeExecutionState> stateNodeLookup)
    {
        var cases = new List<NodeExecutionStateViewModel>();

        if (RepeatCount <= 1 || _plannedNodeTemplates.Count == 0)
        {
            var nodes = stateNodeLookup.Values
                .Where(node => node.IterationIndex == iterationIndex)
                .OrderBy(node => node.SequenceIndex);

            foreach (var nodeState in nodes)
            {
                cases.Add(GetOrCreateNodeViewModel(nodeState));
            }

            return cases;
        }

        foreach (var template in _plannedNodeTemplates)
        {
        var key = BuildNodeKey(iterationIndex, template.SequenceIndex, template.ParentNodeId, template.NodeId);
            if (!_nodeViewModelDict.TryGetValue(key, out var vm))
            {
                vm = new NodeExecutionStateViewModel
                {
                    NodeId = template.NodeId,
                    NodeType = template.NodeType,
                    TestId = template.TestId,
                    TestVersion = template.TestVersion,
                    TestName = template.TestName,
                    SuiteName = template.SuiteName,
                    PlanName = template.PlanName,
                    ReferenceName = template.ReferenceName,
                    Status = null,
                    Duration = null,
                    RetryCount = 0,
                    IsRunning = false,
                    ParentNodeId = template.ParentNodeId,
                    IndentLevel = string.IsNullOrEmpty(template.ParentNodeId) ? 0 : 1,
                    IterationIndex = iterationIndex,
                    SequenceIndex = template.SequenceIndex
                };
                _nodeViewModelDict[key] = vm;
            }

            if (stateNodeLookup.TryGetValue(key, out var nodeState))
            {
                UpdateNodeViewModel(vm, nodeState);
            }

            cases.Add(vm);
        }

        return cases;
    }

    private NodeExecutionStateViewModel GetOrCreateNodeViewModel(NodeExecutionState nodeState)
    {
        var key = BuildNodeKey(nodeState);
        if (_nodeViewModelDict.TryGetValue(key, out var existingVm))
        {
            UpdateNodeViewModel(existingVm, nodeState);
            return existingVm;
        }

        var indentLevel = string.IsNullOrEmpty(nodeState.ParentNodeId) ? 0 : 1;
        var newVm = new NodeExecutionStateViewModel
        {
            NodeId = nodeState.NodeId,
            NodeType = nodeState.NodeType,
            TestId = nodeState.TestId,
            TestVersion = nodeState.TestVersion,
            TestName = nodeState.TestName,
            SuiteName = nodeState.SuiteName,
            PlanName = nodeState.PlanName,
            ReferenceName = nodeState.ReferenceName,
            Status = nodeState.Status,
            Duration = nodeState.Duration,
            RetryCount = nodeState.RetryCount,
            IsRunning = nodeState.IsRunning,
            ParentNodeId = nodeState.ParentNodeId,
            IndentLevel = indentLevel,
            IterationIndex = nodeState.IterationIndex,
            SequenceIndex = nodeState.SequenceIndex
        };

        _nodeViewModelDict[key] = newVm;
        return newVm;
    }

    private void UpdateNodeViewModel(NodeExecutionStateViewModel vm, NodeExecutionState nodeState)
    {
        vm.Status = nodeState.Status;
        vm.Duration = nodeState.Duration;
        vm.RetryCount = nodeState.RetryCount;
        vm.IsRunning = nodeState.IsRunning;
        vm.ParentNodeId = nodeState.ParentNodeId;
        vm.IndentLevel = string.IsNullOrEmpty(nodeState.ParentNodeId) ? 0 : 1;
        vm.IterationIndex = nodeState.IterationIndex;
        vm.SequenceIndex = nodeState.SequenceIndex;
    }

    private void SyncCases(ObservableCollection<NodeExecutionStateViewModel> target, List<NodeExecutionStateViewModel> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void UpdateIterationSummary(IterationExecutionViewModel iterationVm)
    {
        var cases = iterationVm.Cases;
        iterationVm.TotalCases = cases.Count;
        iterationVm.CompletedCases = cases.Count(c => c.Status is not null);

        var totalTicks = cases
            .Where(c => c.Duration.HasValue)
            .Sum(c => c.Duration!.Value.Ticks);
        iterationVm.Duration = totalTicks > 0 ? TimeSpan.FromTicks(totalTicks) : null;

        var hasRunning = cases.Any(c => c.IsRunning);
        iterationVm.IsRunning = hasRunning;

        if (hasRunning)
        {
            iterationVm.Status = null;
            return;
        }

        if (cases.Count == 0 || cases.Any(c => c.Status is null))
        {
            iterationVm.Status = null;
            return;
        }

        if (cases.Any(c => c.Status == RunStatus.Aborted))
        {
            iterationVm.Status = RunStatus.Aborted;
            return;
        }

        if (cases.Any(c => c.Status == RunStatus.Failed
            || c.Status == RunStatus.Error
            || c.Status == RunStatus.Timeout
            || c.Status == RunStatus.RebootRequired))
        {
            iterationVm.Status = RunStatus.Failed;
            return;
        }

        iterationVm.Status = RunStatus.Passed;
    }

    private void UpdateCurrentIteration()
    {
        var currentIteration = Iterations.FirstOrDefault(iteration => iteration.Cases.Any(c => c.IsRunning))
            ?? Iterations.FirstOrDefault(iteration => iteration.Cases.Any(c => c.Status is null))
            ?? Iterations.LastOrDefault();

        CurrentIterationIndex = currentIteration?.Index ?? 1;

        foreach (var iteration in Iterations)
        {
            iteration.IsExpanded = iteration.Index == CurrentIterationIndex;
        }
    }

    private static string BuildNodeKey(NodeExecutionState nodeState)
    {
        return BuildNodeKey(nodeState.IterationIndex, nodeState.SequenceIndex, nodeState.ParentNodeId, nodeState.NodeId);
    }

    private static string BuildNodeKey(int iterationIndex, int sequenceIndex, string? parentNodeId, string nodeId)
    {
        return $"{iterationIndex}:{sequenceIndex}:{parentNodeId}:{nodeId}";
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

        OnPropertyChanged(nameof(ShowIterationGroups));
        OnPropertyChanged(nameof(ShowPlanPipeline));
        OnPropertyChanged(nameof(ShowFlatPipeline));
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
        _plannedNodeTemplates.Clear();
        Nodes.Clear();
        Iterations.Clear();
        SuiteGroups.Clear();
        _suiteViewModels.Clear();
        RepeatCount = 1;
        CurrentIterationIndex = 1;

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
            await LoadRepeatCountAsync();
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
}

public sealed class PlannedNodeTemplate
{
    public PlannedNodeTemplate(NodeExecutionState nodeState)
    {
        NodeId = nodeState.NodeId;
        NodeType = nodeState.NodeType;
        TestId = nodeState.TestId;
        TestVersion = nodeState.TestVersion;
        TestName = nodeState.TestName;
        SuiteName = nodeState.SuiteName;
        PlanName = nodeState.PlanName;
        ReferenceName = nodeState.ReferenceName;
        ParentNodeId = nodeState.ParentNodeId;
        SequenceIndex = nodeState.SequenceIndex;
    }

    public string NodeId { get; }
    public RunType NodeType { get; }
    public string TestId { get; }
    public string TestVersion { get; }
    public string? TestName { get; }
    public string? SuiteName { get; }
    public string? PlanName { get; }
    public string? ReferenceName { get; }
    public string? ParentNodeId { get; }
    public int SequenceIndex { get; }
}

public partial class IterationExecutionViewModel : ViewModelBase
{
    [ObservableProperty] private int _index;
    [ObservableProperty] private int _total;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private ObservableCollection<NodeExecutionStateViewModel> _cases = new();
    [ObservableProperty] private int _completedCases;
    [ObservableProperty] private int _totalCases;
    [ObservableProperty] private RunStatus? _status;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private TimeSpan? _duration;

    public string Label => $"Iteration {Index}/{Total}";
    public string ProgressDisplay => $"{CompletedCases}/{TotalCases}";
    public string DurationDisplay => Duration?.ToString(@"mm\:ss") ?? "--";
    public string StatusLabel => IsRunning ? "Running" : Status?.ToString() ?? "Pending";

    partial void OnIndexChanged(int value) => OnPropertyChanged(nameof(Label));
    partial void OnTotalChanged(int value) => OnPropertyChanged(nameof(Label));
    partial void OnCompletedCasesChanged(int value) => OnPropertyChanged(nameof(ProgressDisplay));
    partial void OnTotalCasesChanged(int value) => OnPropertyChanged(nameof(ProgressDisplay));
    partial void OnDurationChanged(TimeSpan? value) => OnPropertyChanged(nameof(DurationDisplay));
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(StatusLabel));
    partial void OnStatusChanged(RunStatus? value) => OnPropertyChanged(nameof(StatusLabel));
}

public partial class PlanSuiteExecutionViewModel : ViewModelBase
{
    [ObservableProperty] private NodeExecutionStateViewModel _suiteNode = new();
    [ObservableProperty] private ObservableCollection<IterationExecutionViewModel> _iterations = new();
    [ObservableProperty] private ObservableCollection<NodeExecutionStateViewModel> _cases = new();
    [ObservableProperty] private bool _showIterations;
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
    [ObservableProperty] private string? _referenceName;
    [ObservableProperty] private RunType _nodeType;
    [ObservableProperty] private RunStatus? _status;
    [ObservableProperty] private TimeSpan? _duration;
    [ObservableProperty] private int _retryCount;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string? _parentNodeId;
    [ObservableProperty] private int _indentLevel; // 0 for top-level, 1 for nested
    [ObservableProperty] private int _iterationIndex;
    [ObservableProperty] private int _sequenceIndex;

    public string StatusDisplay => Status?.ToString() ?? (IsRunning ? "Running..." : "Pending");
    public string DurationDisplay => Duration?.ToString(@"mm\:ss\.fff") ?? "-";

    partial void OnStatusChanged(RunStatus? value) => OnPropertyChanged(nameof(StatusDisplay));
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(StatusDisplay));
    partial void OnDurationChanged(TimeSpan? value) => OnPropertyChanged(nameof(DurationDisplay));
    
    /// <summary>
    /// Display name only (without version).
    /// For test cases in suites, uses ReferenceName if available, otherwise falls back to TestName.
    /// </summary>
    public string DisplayName
    {
        get
        {
            return NodeType switch
            {
                RunType.TestCase => ReferenceName ?? TestName ?? TestId,
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
