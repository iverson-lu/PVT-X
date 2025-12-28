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

    private CancellationTokenSource? _runCts;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _targetIdentity = string.Empty;
    [ObservableProperty] private RunType _runType;
    [ObservableProperty] private string _runId = string.Empty;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private RunStatus? _finalStatus;

    [ObservableProperty]
    private ObservableCollection<NodeExecutionStateViewModel> _nodes = new();

    [ObservableProperty]
    private NodeExecutionStateViewModel? _selectedNode;

    [ObservableProperty]
    private string _consoleOutput = string.Empty;

    [ObservableProperty]
    private string _eventsOutput = string.Empty;

    private readonly StringBuilder _consoleBuffer = new();
    private readonly StringBuilder _eventsBuffer = new();

    public RunViewModel(IRunService runService, INavigationService navigationService)
    {
        _runService = runService;
        _navigationService = navigationService;

        _runService.StateChanged += OnStateChanged;
        _runService.ConsoleOutput += OnConsoleOutput;
    }

    public void Initialize(object? parameter)
    {
        if (parameter is RunNavigationParameter navParam)
        {
            TargetIdentity = navParam.TargetIdentity;
            RunType = navParam.RunType;
            StatusText = $"Ready to run {RunType}: {TargetIdentity}";
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

            // Update nodes
            Nodes.Clear();
            foreach (var node in state.Nodes)
            {
                Nodes.Add(new NodeExecutionStateViewModel
                {
                    NodeId = node.NodeId,
                    TestId = node.TestId,
                    TestVersion = node.TestVersion,
                    Status = node.Status,
                    Duration = node.Duration,
                    RetryCount = node.RetryCount,
                    IsRunning = node.IsRunning
                });
            }
        });
    }

    private void OnConsoleOutput(object? sender, string output)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _consoleBuffer.AppendLine(output);
            ConsoleOutput = _consoleBuffer.ToString();
        });
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
        EventsOutput = string.Empty;
        Nodes.Clear();

        _runCts = new CancellationTokenSource();

        var request = new RunRequest();
        
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
    private void ViewLogs()
    {
        if (!string.IsNullOrEmpty(RunId))
        {
            _navigationService.NavigateToLogsResults(RunId);
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

    public string StatusDisplay => Status?.ToString() ?? (IsRunning ? "Running..." : "Pending");
    public string DurationDisplay => Duration?.ToString(@"mm\:ss\.fff") ?? "-";
    public string Identity => $"{TestId}@{TestVersion}";
}
