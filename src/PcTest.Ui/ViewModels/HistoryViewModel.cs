using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
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
    private CancellationTokenSource? _loadCancellationTokenSource;

    [ObservableProperty]
    private RunIndexEntryViewModel? _selectedRun;

    partial void OnSelectedRunChanged(RunIndexEntryViewModel? value)
    {
        // Cancel any pending load operation
        _loadCancellationTokenSource?.Cancel();
        _loadCancellationTokenSource?.Dispose();
        _loadCancellationTokenSource = null;
        
        // Clear old data immediately to prevent showing stale content
        if (value is null)
        {
            ClearRunDetails();
            return;
        }
        
        // Create new cancellation token for this load
        _loadCancellationTokenSource = new CancellationTokenSource();
        
        // Load full run details when selection changes
        _ = LoadRunDetailsAsync(value.RunId, _loadCancellationTokenSource.Token);
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    // Filters
    [ObservableProperty] private DateTime? _startTimeFrom;
    [ObservableProperty] private DateTime? _startTimeTo;
    [ObservableProperty] private string _statusFilter = "ALL";
    [ObservableProperty] private string _runTypeFilter = "ALL";
    [ObservableProperty] private bool _topLevelOnly = true;

    [ObservableProperty]
    private ObservableCollection<RunTreeNodeViewModel> _visibleNodes = new();

    [ObservableProperty]
    private RunTreeNodeViewModel? _selectedNode;

    partial void OnSelectedNodeChanged(RunTreeNodeViewModel? value)
    {
        SelectedRun = value?.Run;
    }

    private RunStatus? GetStatusFilterEnum() => 
        StatusFilter == "ALL" ? null : Enum.TryParse<RunStatus>(StatusFilter, out var status) ? status : null;
    
    private RunType? GetRunTypeFilterEnum() => 
        RunTypeFilter == "ALL" ? null : Enum.TryParse<RunType>(RunTypeFilter, out var type) ? type : null;

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
    [ObservableProperty] private string? _nodeIdFilter;
    [ObservableProperty]
    private ObservableCollection<string> _selectedLevels = new() { "info", "warning", "error" };
    [ObservableProperty] private ObservableCollection<StructuredEventViewModel> _filteredEvents = new();
    [ObservableProperty] private StructuredEventViewModel? _selectedEvent;
    [ObservableProperty] private string _eventDetailsJson = string.Empty;

    public IEnumerable<string> AvailableLevels => new[] { "trace", "debug", "info", "warning", "error" };

    // Artifact properties
    [ObservableProperty] private ObservableCollection<ArtifactNodeViewModel> _artifacts = new();
    [ObservableProperty] private ArtifactNodeViewModel? _selectedArtifact;
    [ObservableProperty] private string _artifactContent = string.Empty;
    [ObservableProperty] private string _artifactContentType = "text";



    private readonly List<RunIndexEntryViewModel> _allRuns = new();
    private readonly Dictionary<string, RunTreeNodeViewModel> _nodeLookup = new();
    private readonly Dictionary<RunTreeNodeViewModel, RunTreeNodeViewModel?> _parentLookup = new();
    private readonly List<RunTreeNodeViewModel> _rootNodes = new();
    private bool _isUpdatingExpansion;

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

    partial void OnSearchTextChanged(string value) => ApplySearchFilter();
    partial void OnStatusFilterChanged(string value) => _ = LoadAsync();
    partial void OnRunTypeFilterChanged(string value) => _ = LoadAsync();
    partial void OnTopLevelOnlyChanged(bool value) => ApplySearchFilter();
    partial void OnStartTimeFromChanged(DateTime? value) => _ = LoadAsync();
    partial void OnStartTimeToChanged(DateTime? value) => _ = LoadAsync();
    partial void OnEventSearchTextChanged(string value) => ApplyEventFilter();
    partial void OnErrorsOnlyChanged(bool value) => ApplyEventFilter();
    partial void OnNodeIdFilterChanged(string? value) => ApplyEventFilter();

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

    private void ApplySearchFilter()
    {
        VisibleNodes.Clear();

        if (_rootNodes.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            RebuildVisibleNodes(null);
            return;
        }

        var search = SearchText.Trim();
        var includeSet = new HashSet<RunTreeNodeViewModel>();
        var expandSet = new HashSet<RunTreeNodeViewModel>();

        foreach (var root in _rootNodes)
        {
            FilterNode(root, search, includeSet, expandSet);
        }

        _isUpdatingExpansion = true;
        try
        {
            foreach (var node in includeSet)
            {
                node.IsExpanded = expandSet.Contains(node);
            }
        }
        finally
        {
            _isUpdatingExpansion = false;
        }

        RebuildVisibleNodes(includeSet);
    }

    private bool FilterNode(
        RunTreeNodeViewModel node,
        string search,
        HashSet<RunTreeNodeViewModel> includeSet,
        HashSet<RunTreeNodeViewModel> expandSet)
    {
        var isMatch = MatchesSearch(node.Run, search);
        var hasMatchingChild = false;

        foreach (var child in node.Children)
        {
            if (FilterNode(child, search, includeSet, expandSet))
            {
                hasMatchingChild = true;
            }
        }

        if (isMatch || hasMatchingChild)
        {
            includeSet.Add(node);
        }

        if (hasMatchingChild)
        {
            expandSet.Add(node);
        }

        return isMatch || hasMatchingChild;
    }

    private static bool MatchesSearch(RunIndexEntryViewModel run, string search)
    {
        return run.RunId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               run.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               (run.TestId?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (run.SuiteId?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (run.PlanId?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void RebuildVisibleNodes(HashSet<RunTreeNodeViewModel>? includeSet)
    {
        VisibleNodes.Clear();

        foreach (var root in _rootNodes)
        {
            AddVisibleNode(root, includeSet);
        }
    }

    private void AddVisibleNode(RunTreeNodeViewModel node, HashSet<RunTreeNodeViewModel>? includeSet)
    {
        if (includeSet is not null && !includeSet.Contains(node))
        {
            return;
        }

        VisibleNodes.Add(node);

        if (includeSet is null && TopLevelOnly)
        {
            return;
        }

        if (includeSet is null && !node.IsExpanded)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            AddVisibleNode(child, includeSet);
        }
    }

    private async Task LoadRunDetailsAsync(string? runId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(runId))
        {
            ClearRunDetails();
            return;
        }

        // Clear previous content first
        StdoutContent = string.Empty;
        StderrContent = string.Empty;
        FilteredEvents.Clear();
        Artifacts.Clear();
        ArtifactContent = string.Empty;

        try
        {
            if (cancellationToken.IsCancellationRequested) return;
            
            RunDetails = await _runRepository.GetRunDetailsAsync(runId);

            if (cancellationToken.IsCancellationRequested) return;
            
            if (RunDetails is null)
            {
                _fileDialogService.ShowError("Run Not Found", $"Could not load run: {runId}");
                ClearRunDetails();
                return;
            }

            // Notify UI of computed property changes immediately
            OnPropertyChanged(nameof(IsRunSelected));
            OnPropertyChanged(nameof(RunStatus));
            OnPropertyChanged(nameof(RunStatusEnum));
            OnPropertyChanged(nameof(RunDisplayName));
            OnPropertyChanged(nameof(StartTimeDisplay));
            OnPropertyChanged(nameof(EndTimeDisplay));
            OnPropertyChanged(nameof(DurationDisplay));
            OnPropertyChanged(nameof(ExitCode));
            OnPropertyChanged(nameof(ErrorMessage));

            if (cancellationToken.IsCancellationRequested) return;

            // Load additional data sequentially on UI thread
            // This ensures smooth updates without cross-thread issues
            await LoadArtifactsAsync(runId);
            
            if (cancellationToken.IsCancellationRequested) return;
            
            await LoadStdoutStderrAsync(runId);
            
            if (cancellationToken.IsCancellationRequested) return;
            
            await LoadEventsAsync(runId);
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation, ignore
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _fileDialogService.ShowError("Error Loading Run", $"Failed to load run details: {ex.Message}");
                ClearRunDetails();
            }
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

        if (!string.IsNullOrEmpty(NodeIdFilter))
        {
            filtered = filtered.Where(e => e.NodeId?.Equals(NodeIdFilter, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (!string.IsNullOrWhiteSpace(EventSearchText))
        {
            var searchLower = EventSearchText.ToLowerInvariant();
            filtered = filtered.Where(e =>
                e.Message?.ToLowerInvariant().Contains(searchLower) == true ||
                e.Type?.ToLowerInvariant().Contains(searchLower) == true ||
                e.Code?.ToLowerInvariant().Contains(searchLower) == true ||
                e.Source?.ToLowerInvariant().Contains(searchLower) == true);
        }

        if (SelectedLevels.Count > 0)
        {
            filtered = filtered.Where(e => SelectedLevels.Contains(e.Level.ToLowerInvariant()));
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
                Status = GetStatusFilterEnum(),
                RunType = GetRunTypeFilterEnum(),
                TopLevelOnly = false,
                MaxResults = 500
            };

            var runs = await _runRepository.GetRunIndexAsync(filter);
            _allRuns.Clear();

            foreach (var run in runs)
            {
                _allRuns.Add(run);
            }

            BuildRunTree();
            ApplySearchFilter();
            RestoreSelection();
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
            SelectRunById(runId);
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
        StatusFilter = "ALL";
        RunTypeFilter = "ALL";
        TopLevelOnly = true;
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

    public IEnumerable<string> StatusOptions => new[] { "ALL" }
        .Concat(Enum.GetValues<RunStatus>().Select(s => s.ToString()));

    public IEnumerable<string> RunTypeOptions => new[] { "ALL" }
        .Concat(Enum.GetValues<RunType>().Select(t => t.ToString()));

    private void BuildRunTree()
    {
        _nodeLookup.Clear();
        _parentLookup.Clear();
        _rootNodes.Clear();

        foreach (var run in _allRuns)
        {
            var node = new RunTreeNodeViewModel
            {
                Run = run,
                IsExpanded = false,
                Depth = 0
            };
            node.PropertyChanged += OnNodePropertyChanged;
            _nodeLookup[run.RunId] = node;
        }

        foreach (var run in _allRuns)
        {
            var node = _nodeLookup[run.RunId];
            if (!string.IsNullOrEmpty(run.ParentRunId) && _nodeLookup.TryGetValue(run.ParentRunId, out var parent))
            {
                parent.Children.Add(node);
                _parentLookup[node] = parent;
            }
            else
            {
                _rootNodes.Add(node);
                _parentLookup[node] = null;
            }
        }

        foreach (var root in _rootNodes)
        {
            SetNodeDepth(root, 0);
        }
    }

    private void SetNodeDepth(RunTreeNodeViewModel node, int depth)
    {
        node.Depth = depth;
        foreach (var child in node.Children)
        {
            SetNodeDepth(child, depth + 1);
        }
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUpdatingExpansion || e.PropertyName != nameof(RunTreeNodeViewModel.IsExpanded))
        {
            return;
        }

        ApplySearchFilter();
    }

    private void RestoreSelection()
    {
        if (SelectedRun is null)
        {
            return;
        }

        if (_nodeLookup.TryGetValue(SelectedRun.RunId, out var node))
        {
            SelectedNode = node;
        }
        else
        {
            SelectedNode = null;
        }
    }

    private void SelectRunById(string runId)
    {
        if (_nodeLookup.TryGetValue(runId, out var node))
        {
            ExpandAncestors(node);
            SelectedNode = node;
            ApplySearchFilter();
        }
    }

    private void ExpandAncestors(RunTreeNodeViewModel node)
    {
        _isUpdatingExpansion = true;
        try
        {
            var current = node;
            while (_parentLookup.TryGetValue(current, out var parent) && parent is not null)
            {
                parent.IsExpanded = true;
                current = parent;
            }
        }
        finally
        {
            _isUpdatingExpansion = false;
        }
    }
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

/// <summary>
/// ViewModel for a run tree node.
/// </summary>
public partial class RunTreeNodeViewModel : ViewModelBase
{
    [ObservableProperty] private RunIndexEntryViewModel _run = new();
    [ObservableProperty] private ObservableCollection<RunTreeNodeViewModel> _children = new();
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private int _depth;

    public RunTreeNodeViewModel()
    {
        _children.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasChildren));
    }

    public bool HasChildren => Children.Count > 0;
}

/// <summary>
/// ViewModel for artifact tree node.
/// </summary>
public partial class ArtifactNodeViewModel : ViewModelBase
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _relativePath = string.Empty;
    [ObservableProperty] private string _fullPath = string.Empty;
    [ObservableProperty] private long _size;
    [ObservableProperty] private bool _isDirectory;
    [ObservableProperty] private bool _isExpanded;

    [ObservableProperty]
    private ObservableCollection<ArtifactNodeViewModel> _children = new();

    public string SizeDisplay => IsDirectory ? string.Empty : FormatSize(Size);

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}

/// <summary>
/// ViewModel for a structured event.
/// </summary>
public partial class StructuredEventViewModel : ViewModelBase
{
    [ObservableProperty] private DateTime _timestamp;
    [ObservableProperty] private string _level = "info";
    [ObservableProperty] private string? _source;
    [ObservableProperty] private string? _nodeId;
    [ObservableProperty] private string? _type;
    [ObservableProperty] private string? _code;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string? _exception;
    [ObservableProperty] private string? _stackTrace;
    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private string _rawJson = string.Empty;

    public string TimestampDisplay => Timestamp.ToString("HH:mm:ss.fff");
    public bool HasException => !string.IsNullOrEmpty(Exception);
    public bool HasFilePath => !string.IsNullOrEmpty(FilePath);
}
