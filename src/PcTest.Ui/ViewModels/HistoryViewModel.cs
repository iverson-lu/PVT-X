using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTest.Contracts;
using PcTest.Ui.Services;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// ViewModel for the History page.
/// </summary>
public partial class HistoryViewModel : ViewModelBase
{
    private readonly IRunRepository _runRepository;
    private readonly INavigationService _navigationService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IFileDialogService _fileDialogService;

    [ObservableProperty]
    private ObservableCollection<RunIndexEntryViewModel> _runs = new();

    [ObservableProperty]
    private RunIndexEntryViewModel? _selectedRun;

    [ObservableProperty]
    private string _searchText = string.Empty;

    // Filters
    [ObservableProperty] private DateTime? _startTimeFrom;
    [ObservableProperty] private DateTime? _startTimeTo;
    [ObservableProperty] private RunStatus? _statusFilter;
    [ObservableProperty] private RunType? _runTypeFilter;
    [ObservableProperty] private bool _topLevelOnly = true;

    private List<RunIndexEntryViewModel> _allRuns = new();

    public HistoryViewModel(
        IRunRepository runRepository,
        INavigationService navigationService,
        IFileSystemService fileSystemService,
        IFileDialogService fileDialogService)
    {
        _runRepository = runRepository;
        _navigationService = navigationService;
        _fileSystemService = fileSystemService;
        _fileDialogService = fileDialogService;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnStatusFilterChanged(RunStatus? value) => _ = LoadAsync();
    partial void OnRunTypeFilterChanged(RunType? value) => _ = LoadAsync();
    partial void OnTopLevelOnlyChanged(bool value) => _ = LoadAsync();
    partial void OnStartTimeFromChanged(DateTime? value) => _ = LoadAsync();
    partial void OnStartTimeToChanged(DateTime? value) => _ = LoadAsync();

    private void ApplyFilter()
    {
        Runs.Clear();
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allRuns
            : _allRuns.Where(r =>
                r.RunId.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                r.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                (r.TestId?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.SuiteId?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.PlanId?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));

        foreach (var r in filtered)
        {
            Runs.Add(r);
        }
    }

    public async Task LoadAsync()
    {
        SetBusy(true, "Loading run history...");

        try
        {
            var filter = new RunFilter
            {
                StartTimeFrom = StartTimeFrom,
                StartTimeTo = StartTimeTo,
                Status = StatusFilter,
                RunType = RunTypeFilter,
                TopLevelOnly = TopLevelOnly,
                MaxResults = 500
            };

            var runs = await _runRepository.GetRunsAsync(filter);
            _allRuns.Clear();

            foreach (var run in runs)
            {
                _allRuns.Add(new RunIndexEntryViewModel
                {
                    RunId = run.RunId,
                    RunType = run.RunType,
                    NodeId = run.NodeId,
                    TestId = run.TestId,
                    TestVersion = run.TestVersion,
                    SuiteId = run.SuiteId,
                    SuiteVersion = run.SuiteVersion,
                    PlanId = run.PlanId,
                    PlanVersion = run.PlanVersion,
                    ParentRunId = run.ParentRunId,
                    StartTime = run.StartTime,
                    EndTime = run.EndTime,
                    Status = run.Status,
                    DisplayName = run.DisplayName,
                    Duration = run.Duration
                });
            }

            ApplyFilter();
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        StartTimeFrom = null;
        StartTimeTo = null;
        StatusFilter = null;
        RunTypeFilter = null;
        TopLevelOnly = true;
    }

    [RelayCommand]
    private void ViewLogsResults()
    {
        if (SelectedRun is not null)
        {
            _navigationService.NavigateToLogsResults(SelectedRun.RunId);
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (SelectedRun is null) return;

        var folderPath = _runRepository.GetRunFolderPath(SelectedRun.RunId);
        if (_fileSystemService.DirectoryExists(folderPath))
        {
            _fileSystemService.OpenInExplorer(folderPath);
        }
        else
        {
            _fileDialogService.ShowWarning("Folder Not Found", $"Run folder not found: {folderPath}");
        }
    }

    [RelayCommand]
    private void Rerun()
    {
        if (SelectedRun is null) return;

        var identity = SelectedRun.RunType switch
        {
            RunType.TestCase => $"{SelectedRun.TestId}@{SelectedRun.TestVersion}",
            RunType.TestSuite => $"{SelectedRun.SuiteId}@{SelectedRun.SuiteVersion}",
            RunType.TestPlan => $"{SelectedRun.PlanId}@{SelectedRun.PlanVersion}",
            _ => string.Empty
        };

        if (!string.IsNullOrEmpty(identity))
        {
            _navigationService.NavigateToRun(identity, SelectedRun.RunType);
        }
    }

    public IEnumerable<RunStatus?> StatusOptions => new RunStatus?[] { null }
        .Concat(Enum.GetValues<RunStatus>().Cast<RunStatus?>());

    public IEnumerable<RunType?> RunTypeOptions => new RunType?[] { null }
        .Concat(Enum.GetValues<RunType>().Cast<RunType?>());
}

/// <summary>
/// ViewModel for a run index entry.
/// </summary>
public partial class RunIndexEntryViewModel : ViewModelBase
{
    [ObservableProperty] private string _runId = string.Empty;
    [ObservableProperty] private RunType _runType;
    [ObservableProperty] private string? _nodeId;
    [ObservableProperty] private string? _testId;
    [ObservableProperty] private string? _testVersion;
    [ObservableProperty] private string? _suiteId;
    [ObservableProperty] private string? _suiteVersion;
    [ObservableProperty] private string? _planId;
    [ObservableProperty] private string? _planVersion;
    [ObservableProperty] private string? _parentRunId;
    [ObservableProperty] private DateTime _startTime;
    [ObservableProperty] private DateTime? _endTime;
    [ObservableProperty] private RunStatus _status;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private TimeSpan? _duration;

    public string StartTimeDisplay => StartTime.ToString("yyyy-MM-dd HH:mm:ss");
    public string DurationDisplay => Duration?.ToString(@"mm\:ss\.fff") ?? "-";
    public string StatusDisplay => Status.ToString();
    public bool IsTopLevel => string.IsNullOrEmpty(ParentRunId);
    
    /// <summary>
    /// Short run ID for display in table (last 8 chars of hash).
    /// </summary>
    public string ShortRunId
    {
        get
        {
            if (string.IsNullOrEmpty(RunId)) return string.Empty;
            
            // If format is "R-TIMESTAMP-UUID" or "P-TIMESTAMP-UUID", extract last 8 chars of UUID
            if (RunId.StartsWith("R-") || RunId.StartsWith("P-"))
            {
                var parts = RunId.Split('-');
                if (parts.Length >= 3)
                {
                    // Get the UUID part (last segment) and take last 8 chars
                    var uuid = parts[^1];
                    return uuid.Length > 8 ? uuid[^8..] : uuid;
                }
            }
            
            // Fallback: take last 8 characters
            return RunId.Length > 8 ? RunId[^8..] : RunId;
        }
    }
    
    /// <summary>
    /// Tooltip with full run type name.
    /// </summary>
    public string RunTypeTooltip => RunType switch
    {
        RunType.TestCase => "Test Case",
        RunType.TestSuite => "Test Suite",
        RunType.TestPlan => "Test Plan",
        _ => "Unknown"
    };
    
    /// <summary>
    /// Version number for display (picks the appropriate version based on run type).
    /// </summary>
    public string VersionDisplay
    {
        get
        {
            var version = RunType switch
            {
                RunType.TestCase => TestVersion,
                RunType.TestSuite => SuiteVersion,
                RunType.TestPlan => PlanVersion,
                _ => null
            };
            return !string.IsNullOrEmpty(version) ? $"v{version}" : string.Empty;
        }
    }
    
    /// <summary>
    /// Display name without version (since version is shown separately).
    /// </summary>
    public string NameWithoutVersion
    {
        get
        {
            return RunType switch
            {
                RunType.TestCase => TestId ?? string.Empty,
                RunType.TestSuite => SuiteId ?? string.Empty,
                RunType.TestPlan => PlanId ?? string.Empty,
                _ => DisplayName
            };
        }
    }
}
