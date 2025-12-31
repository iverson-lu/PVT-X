using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
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

    partial void OnSelectedRunChanged(RunIndexEntryViewModel? value)
    {
        // Load full run details when selection changes
        _ = LoadRunDetailsAsync(value?.RunId);
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    // Filters
    [ObservableProperty] private DateTime? _startTimeFrom;
    [ObservableProperty] private DateTime? _startTimeTo;
    [ObservableProperty] private RunStatus? _statusFilter;
    [ObservableProperty] private RunType? _runTypeFilter;
    [ObservableProperty] private bool _topLevelOnly = true;

    // Run Details - Main observable
    [ObservableProperty] private RunDetails? _runDetails;

    // Computed properties based on RunDetails
    public bool IsRunSelected => RunDetails is not null;
    public string RunStatus => RunDetails?.IndexEntry.Status.ToString() ?? string.Empty;
    public RunStatus? RunStatusEnum => RunDetails?.IndexEntry.Status;
    public string RunDisplayName => RunDetails?.IndexEntry.DisplayName ?? string.Empty;
    public string StartTimeDisplay => RunDetails?.IndexEntry.StartTime.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
    public string EndTimeDisplay => RunDetails?.IndexEntry.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
    public string DurationDisplay => RunDetails?.IndexEntry.Duration?.ToString(@"hh\:mm\:ss") ?? string.Empty;
    public int? ExitCode => GetExitCodeFromResult();
    public string ErrorMessage => GetErrorMessageFromResult();

    // Content properties
    [ObservableProperty] private string _stdoutContent = string.Empty;
    [ObservableProperty] private string _stderrContent = string.Empty;

    // Event properties
    [ObservableProperty] private string _eventSearchText = string.Empty;
    [ObservableProperty] private bool _errorsOnly = false;
    [ObservableProperty] private ObservableCollection<StructuredEventViewModel> _filteredEvents = new();
    [ObservableProperty] private StructuredEventViewModel? _selectedEvent;
    [ObservableProperty] private string _eventDetailsJson = string.Empty;

    // Artifact properties
    [ObservableProperty] private ObservableCollection<ArtifactNodeViewModel> _artifacts = new();
    [ObservableProperty] private ArtifactNodeViewModel? _selectedArtifact;
    [ObservableProperty] private string _artifactContent = string.Empty;
    [ObservableProperty] private string _artifactContentType = "text";



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
    partial void OnEventSearchTextChanged(string value) => ApplyEventFilter();
    partial void OnErrorsOnlyChanged(bool value) => ApplyEventFilter();

    partial void OnSelectedArtifactChanged(ArtifactNodeViewModel? value)
    {
        if (value is not null && !value.IsDirectory && SelectedRun is not null)
        {
            _ = LoadArtifactContentAsync(SelectedRun.RunId, value);
        }
        else
        {
            ArtifactContent = string.Empty;
        }
    }

    partial void OnSelectedEventChanged(StructuredEventViewModel? value)
    {
        EventDetailsJson = value != null ? System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) : string.Empty;
    }

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

    private async Task LoadRunDetailsAsync(string? runId)
    {
        if (string.IsNullOrEmpty(runId))
        {
            ClearRunDetails();
            return;
        }

        SetBusy(true, "Loading run details...");

        try
        {
            RunDetails = await _runRepository.GetRunDetailsAsync(runId);

            if (RunDetails is null)
            {
                _fileDialogService.ShowError("Run Not Found", $"Could not load run: {runId}");
                ClearRunDetails();
                return;
            }

            // Load artifacts tree
            await LoadArtifactsAsync(runId);

            // Load stdout/stderr logs
            await LoadStdoutStderrAsync(runId);

            // Load events
            await LoadEventsAsync(runId);

            // Notify UI of computed property changes
            OnPropertyChanged(nameof(IsRunSelected));
            OnPropertyChanged(nameof(RunStatus));
            OnPropertyChanged(nameof(RunStatusEnum));
            OnPropertyChanged(nameof(RunDisplayName));
            OnPropertyChanged(nameof(StartTimeDisplay));
            OnPropertyChanged(nameof(EndTimeDisplay));
            OnPropertyChanged(nameof(DurationDisplay));
            OnPropertyChanged(nameof(ExitCode));
            OnPropertyChanged(nameof(ErrorMessage));
        }
        catch (Exception ex)
        {
            _fileDialogService.ShowError("Error Loading Run", $"Failed to load run details: {ex.Message}");
            ClearRunDetails();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ClearRunDetails()
    {
        RunDetails = null;
        StdoutContent = string.Empty;
        StderrContent = string.Empty;
        FilteredEvents.Clear();
        Artifacts.Clear();
        ArtifactContent = string.Empty;

        // Notify UI of computed property changes
        OnPropertyChanged(nameof(IsRunSelected));
        OnPropertyChanged(nameof(RunStatus));
        OnPropertyChanged(nameof(RunStatusEnum));
        OnPropertyChanged(nameof(RunDisplayName));
        OnPropertyChanged(nameof(StartTimeDisplay));
        OnPropertyChanged(nameof(EndTimeDisplay));
        OnPropertyChanged(nameof(DurationDisplay));
        OnPropertyChanged(nameof(ExitCode));
        OnPropertyChanged(nameof(ErrorMessage));
    }

    private async Task LoadStdoutStderrAsync(string runId)
    {
        var runFolder = _runRepository.GetRunFolderPath(runId);

        // Load stdout.log
        var stdoutPath = Path.Combine(runFolder, "stdout.log");
        if (_fileSystemService.FileExists(stdoutPath))
        {
            try
            {
                StdoutContent = await _fileSystemService.ReadAllTextAsync(stdoutPath);
            }
            catch (Exception ex)
            {
                StdoutContent = $"Error reading stdout.log: {ex.Message}";
            }
        }
        else
        {
            StdoutContent = "(No stdout.log found)";
        }

        // Load stderr.log
        var stderrPath = Path.Combine(runFolder, "stderr.log");
        if (_fileSystemService.FileExists(stderrPath))
        {
            try
            {
                StderrContent = await _fileSystemService.ReadAllTextAsync(stderrPath);
            }
            catch (Exception ex)
            {
                StderrContent = $"Error reading stderr.log: {ex.Message}";
            }
        }
        else
        {
            StderrContent = "(No stderr.log found)";
        }
    }

    private List<StructuredEventViewModel> _allEvents = new();

    private async Task LoadEventsAsync(string runId)
    {
        _allEvents.Clear();
        FilteredEvents.Clear();

        var hasAnyEvents = false;
        await foreach (var batch in _runRepository.StreamEventsAsync(runId))
        {
            foreach (var evt in batch.Events)
            {
                hasAnyEvents = true;
                var evtVm = new StructuredEventViewModel
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
                };

                _allEvents.Add(evtVm);
            }
        }

        if (!hasAnyEvents)
        {
            // Add a placeholder message when no events are found
            _allEvents.Add(new StructuredEventViewModel
            {
                Timestamp = DateTime.Now,
                Level = "info",
                Message = "(No structured events found for this run. The test may not have generated an events.jsonl file.)",
                RawJson = "{}"
            });
        }

        ApplyEventFilter();
    }

    private void ApplyEventFilter()
    {
        FilteredEvents.Clear();

        var filtered = _allEvents.AsEnumerable();

        if (ErrorsOnly)
        {
            filtered = filtered.Where(e => e.Level?.ToLowerInvariant() == "error");
        }

        if (!string.IsNullOrWhiteSpace(EventSearchText))
        {
            var searchLower = EventSearchText.ToLowerInvariant();
            filtered = filtered.Where(e =>
                e.Message?.ToLowerInvariant().Contains(searchLower) == true ||
                e.Type?.ToLowerInvariant().Contains(searchLower) == true ||
                e.Source?.ToLowerInvariant().Contains(searchLower) == true);
        }

        foreach (var evt in filtered)
        {
            FilteredEvents.Add(evt);
        }
    }

    private async Task<string> LoadFileContentAsync(string filePath)
    {
        try
        {
            if (_fileSystemService.FileExists(filePath))
            {
                return await File.ReadAllTextAsync(filePath);
            }
        }
        catch (Exception ex)
        {
            return $"Error loading file: {ex.Message}";
        }
        return string.Empty;
    }

    private async Task LoadArtifactsAsync(string runId)
    {
        Artifacts.Clear();
        var artifacts = await _runRepository.GetArtifactsAsync(runId);

        foreach (var artifact in artifacts)
        {
            Artifacts.Add(CreateArtifactNode(artifact));
        }
    }

    private ArtifactNodeViewModel CreateArtifactNode(ArtifactInfo artifact)
    {
        var node = new ArtifactNodeViewModel
        {
            Name = artifact.Name,
            RelativePath = artifact.RelativePath,
            FullPath = artifact.FullPath,
            Size = artifact.Size,
            IsDirectory = artifact.IsDirectory
        };

        if (artifact.IsDirectory)
        {
            foreach (var child in artifact.Children)
            {
                node.Children.Add(CreateArtifactNode(child));
            }
        }

        return node;
    }

    private async Task LoadArtifactContentAsync(string runId, ArtifactNodeViewModel artifact)
    {
        try
        {
            var content = await _runRepository.ReadArtifactAsync(runId, artifact.RelativePath);
            ArtifactContent = content;

            // Determine content type
            ArtifactContentType = artifact.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? "json"
                : artifact.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                    ? "log"
                    : "text";
        }
        catch (Exception ex)
        {
            ArtifactContent = $"Error loading artifact: {ex.Message}";
            ArtifactContentType = "text";
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

    public async Task InitializeAsync(object? parameter)
    {
        await LoadAsync();

        // If a runId was provided as parameter, select that run
        if (parameter is string runId && !string.IsNullOrEmpty(runId))
        {
            var run = Runs.FirstOrDefault(r => r.RunId == runId);
            if (run != null)
            {
                SelectedRun = run;
            }
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

    [RelayCommand]
    private void CopyRunId()
    {
        if (SelectedRun is not null)
        {
            System.Windows.Clipboard.SetText(SelectedRun.RunId);
        }
    }

    [RelayCommand]
    private async Task RefreshEvents()
    {
        // Placeholder for loading structured events from run
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void ClearEventFilters()
    {
        EventSearchText = string.Empty;
        ErrorsOnly = false;
    }

    private int? GetExitCodeFromResult()
    {
        if (RunDetails?.ResultJson is null) return null;

        try
        {
            var doc = JsonDocument.Parse(RunDetails.ResultJson);
            if (doc.RootElement.TryGetProperty("exitCode", out var exitCodeProp))
            {
                return exitCodeProp.GetInt32();
            }
        }
        catch { }

        return null;
    }

    private string GetErrorMessageFromResult()
    {
        if (RunDetails?.ResultJson is null) return string.Empty;

        try
        {
            var doc = JsonDocument.Parse(RunDetails.ResultJson);
            if (doc.RootElement.TryGetProperty("error", out var errorProp))
            {
                return errorProp.GetString() ?? string.Empty;
            }
        }
        catch { }

        return string.Empty;
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
