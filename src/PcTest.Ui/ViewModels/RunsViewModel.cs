using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTest.Contracts;
using PcTest.Ui.Services;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// ViewModel for the unified Runs page (replaces History + Logs & Results).
/// Provides a master-detail layout with run list on the left and run inspector on the right.
/// </summary>
public partial class RunsViewModel : ViewModelBase
{
    private readonly IRunRepository _runRepository;
    private readonly IFileSystemService _fileSystemService;
    private readonly IFileDialogService _fileDialogService;
    private readonly INavigationService _navigationService;

    #region Run List (Master) - from History

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

    public IEnumerable<RunStatus?> StatusOptions => new RunStatus?[] { null }
        .Concat(Enum.GetValues<RunStatus>().Cast<RunStatus?>());

    public IEnumerable<RunType?> RunTypeOptions => new RunType?[] { null }
        .Concat(Enum.GetValues<RunType>().Cast<RunType?>());

    #endregion

    #region Run Inspector (Detail) - from LogsResults

    [ObservableProperty] private RunDetails? _runDetails;

    // Computed properties for UI binding
    public bool IsRunSelected => RunDetails is not null;
    public string RunStatus => RunDetails?.IndexEntry.Status.ToString() ?? string.Empty;
    public string RunDisplayName => RunDetails?.IndexEntry.DisplayName ?? string.Empty;
    public string StartTimeDisplay => RunDetails?.IndexEntry.StartTime.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
    public string EndTimeDisplay => RunDetails?.IndexEntry.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
    public string DurationDisplay => RunDetails?.IndexEntry.Duration?.ToString(@"hh\:mm\:ss") ?? string.Empty;
    public int? ExitCode => GetExitCodeFromResult();
    public string ErrorMessage => GetErrorMessageFromResult();

    // Observable properties for stdout/stderr content (loaded asynchronously)
    [ObservableProperty] private string _stdoutContent = string.Empty;
    [ObservableProperty] private string _stderrContent = string.Empty;

    // Artifacts
    [ObservableProperty]
    private ObservableCollection<ArtifactNodeViewModel> _artifacts = new();

    [ObservableProperty]
    private ArtifactNodeViewModel? _selectedArtifact;

    // Content viewer
    [ObservableProperty] private string _artifactContent = string.Empty;
    [ObservableProperty] private string _artifactContentType = "text";

    // Events viewer
    [ObservableProperty]
    private ObservableCollection<StructuredEventViewModel> _events = new();

    [ObservableProperty]
    private StructuredEventViewModel? _selectedEvent;

    [ObservableProperty] private string _eventDetailsJson = string.Empty;

    // Event filters
    [ObservableProperty] private string _eventSearchText = string.Empty;
    [ObservableProperty] private bool _errorsOnly;
    [ObservableProperty] private string? _nodeIdFilter;
    [ObservableProperty]
    private ObservableCollection<string> _selectedLevels = new() { "info", "warning", "error" };

    public ObservableCollection<StructuredEventViewModel> FilteredEvents => Events;
    public IEnumerable<string> AvailableLevels => new[] { "trace", "debug", "info", "warning", "error" };

    private List<StructuredEventViewModel> _allEvents = new();

    #endregion

    public RunsViewModel(
        IRunRepository runRepository,
        IFileSystemService fileSystemService,
        IFileDialogService fileDialogService,
        INavigationService navigationService)
    {
        _runRepository = runRepository;
        _fileSystemService = fileSystemService;
        _fileDialogService = fileDialogService;
        _navigationService = navigationService;
    }

    #region Initialization

    public async Task InitializeAsync(object? parameter)
    {
        // Load run list
        await LoadAsync();

        // If a specific run is passed, auto-select it
        if (parameter is string runId && !string.IsNullOrEmpty(runId))
        {
            var run = _allRuns.FirstOrDefault(r => r.RunId == runId);
            if (run is not null)
            {
                SelectedRun = run;
            }
        }
    }

    #endregion

    #region Run List (Master) Logic

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnStatusFilterChanged(RunStatus? value) => _ = LoadAsync();
    partial void OnRunTypeFilterChanged(RunType? value) => _ = LoadAsync();
    partial void OnTopLevelOnlyChanged(bool value) => _ = LoadAsync();
    partial void OnStartTimeFromChanged(DateTime? value) => _ = LoadAsync();
    partial void OnStartTimeToChanged(DateTime? value) => _ = LoadAsync();

    partial void OnSelectedRunChanged(RunIndexEntryViewModel? value)
    {
        if (value is not null)
        {
            _ = LoadRunDetailsAsync(value.RunId);
        }
        else
        {
            ClearRunDetails();
        }
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

    public async Task LoadAsync()
    {
        // Don't use global busy state to avoid UI flicker on page load
        // Use inline loading indicators if needed
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
        catch (Exception ex)
        {
            _fileDialogService.ShowError("Error Loading History", $"Failed to load run history: {ex.Message}");
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

    #endregion

    #region Run Inspector (Detail) Logic

    private async Task LoadRunDetailsAsync(string runId)
    {
        // Don't use global busy state for detail loading to avoid UI flicker
        // The detail panel will show loading indicators as needed
        try
        {
            RunDetails = await _runRepository.GetRunDetailsAsync(runId);

            if (RunDetails is null)
            {
                _fileDialogService.ShowError("Run Not Found", $"Could not load run: {runId}");
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
        }
    }

    private void ClearRunDetails()
    {
        RunDetails = null;
        StdoutContent = string.Empty;
        StderrContent = string.Empty;
        Artifacts.Clear();
        Events.Clear();
        _allEvents.Clear();

        OnPropertyChanged(nameof(IsRunSelected));
        OnPropertyChanged(nameof(RunStatus));
        OnPropertyChanged(nameof(RunDisplayName));
        OnPropertyChanged(nameof(StartTimeDisplay));
        OnPropertyChanged(nameof(EndTimeDisplay));
        OnPropertyChanged(nameof(DurationDisplay));
        OnPropertyChanged(nameof(ExitCode));
        OnPropertyChanged(nameof(ErrorMessage));
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

    partial void OnSelectedArtifactChanged(ArtifactNodeViewModel? value)
    {
        if (value is not null && !value.IsDirectory && SelectedRun is not null)
        {
            _ = LoadArtifactContentAsync(SelectedRun.RunId, value);
        }
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

    private async Task LoadEventsAsync(string runId)
    {
        _allEvents.Clear();
        Events.Clear();

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

    partial void OnEventSearchTextChanged(string value) => ApplyEventFilter();
    partial void OnErrorsOnlyChanged(bool value) => ApplyEventFilter();
    partial void OnNodeIdFilterChanged(string? value) => ApplyEventFilter();

    private void ApplyEventFilter()
    {
        Events.Clear();

        var filtered = _allEvents.AsEnumerable();

        if (ErrorsOnly)
        {
            filtered = filtered.Where(e => e.Level.Equals("error", StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(NodeIdFilter))
        {
            filtered = filtered.Where(e => e.NodeId?.Equals(NodeIdFilter, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (!string.IsNullOrEmpty(EventSearchText))
        {
            filtered = filtered.Where(e =>
                e.Message.Contains(EventSearchText, StringComparison.OrdinalIgnoreCase) ||
                (e.Type?.Contains(EventSearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Code?.Contains(EventSearchText, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (SelectedLevels.Count > 0)
        {
            filtered = filtered.Where(e => SelectedLevels.Contains(e.Level.ToLowerInvariant()));
        }

        foreach (var e in filtered)
        {
            Events.Add(e);
        }
    }

    partial void OnSelectedEventChanged(StructuredEventViewModel? value)
    {
        if (value is not null)
        {
            try
            {
                var formatted = JsonSerializer.Serialize(
                    JsonSerializer.Deserialize<JsonElement>(value.RawJson),
                    new JsonSerializerOptions { WriteIndented = true });
                EventDetailsJson = formatted;
            }
            catch
            {
                EventDetailsJson = value.RawJson;
            }
        }
        else
        {
            EventDetailsJson = string.Empty;
        }
    }

    [RelayCommand]
    private async Task RefreshEventsAsync()
    {
        if (SelectedRun is not null)
        {
            await LoadEventsAsync(SelectedRun.RunId);
        }
    }

    [RelayCommand]
    private void ClearEventFilters()
    {
        EventSearchText = string.Empty;
        ErrorsOnly = false;
        NodeIdFilter = null;
        SelectedLevels = new ObservableCollection<string> { "info", "warning", "error" };
        ApplyEventFilter();
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
    }

    [RelayCommand]
    private void OpenFilePath()
    {
        if (SelectedEvent?.FilePath is not null && _fileSystemService.FileExists(SelectedEvent.FilePath))
        {
            _fileSystemService.OpenWithDefaultApp(SelectedEvent.FilePath);
        }
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

    [RelayCommand]
    private void CopyRunId()
    {
        if (SelectedRun is not null)
        {
            try
            {
                System.Windows.Clipboard.SetText(SelectedRun.RunId);
            }
            catch
            {
                // Clipboard operations can fail; silently ignore
            }
        }
    }

    #endregion
}
