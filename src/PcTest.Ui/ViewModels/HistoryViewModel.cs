using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTest.Contracts;
using PcTest.Ui.Services;

namespace PcTest.Ui.ViewModels;

public enum HistoryDetailView
{
    Summary,
    Stdout,
    Stderr,
    Artifacts
}

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

    /// <summary>
    /// Flattened list of visible tree nodes for virtualized display.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<RunTreeNodeViewModel> _visibleNodes = new();

    /// <summary>
    /// Root tree nodes (Plan level or top-level runs).
    /// </summary>
    private List<RunTreeNodeViewModel> _rootNodes = new();

    /// <summary>
    /// All tree nodes indexed by RunId for quick lookup.
    /// </summary>
    private Dictionary<string, RunTreeNodeViewModel> _nodesByRunId = new();

    [ObservableProperty]
    private RunTreeNodeViewModel? _selectedNode;

    [ObservableProperty]
    private HistoryDetailView _selectedView = HistoryDetailView.Summary;

    public IReadOnlyList<HistoryDetailView> DetailViews { get; } = new[]
    {
        HistoryDetailView.Summary,
        HistoryDetailView.Stdout,
        HistoryDetailView.Stderr,
        HistoryDetailView.Artifacts
    };

    partial void OnSelectedNodeChanged(RunTreeNodeViewModel? value)
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
        _ = LoadRunDetailsAsync(value.Run.RunId, _loadCancellationTokenSource.Token);
    }

    /// <summary>
    /// Gets the selected run from the selected node.
    /// </summary>
    public RunIndexEntryViewModel? SelectedRun => SelectedNode?.Run;

    [ObservableProperty]
    private string _searchText = string.Empty;

    // Filters
    [ObservableProperty] private DateTime? _startTimeFrom;
    [ObservableProperty] private DateTime? _startTimeTo;
    [ObservableProperty] private string _statusFilter = "ALL";
    [ObservableProperty] private string _runTypeFilter = "ALL";
    [ObservableProperty] private bool _topLevelOnly = true;

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
    partial void OnStatusFilterChanged(string value) => _ = LoadAsync();
    partial void OnRunTypeFilterChanged(string value) => _ = LoadAsync();
    partial void OnTopLevelOnlyChanged(bool value)
    {
        // Notify all nodes that CanExpand may have changed
        foreach (var node in _nodesByRunId.Values)
        {
            node.NotifyCanExpandChanged();
        }
        RebuildVisibleNodes();
    }
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

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            // No search text - rebuild with current expand state
            RebuildVisibleNodes();
            return;
        }

        // Search mode: find matching nodes and show their ancestor chain
        var matchingNodeIds = new HashSet<string>();
        var ancestorsToExpand = new HashSet<string>();

        foreach (var node in _nodesByRunId.Values)
        {
            if (MatchesSearch(node.Run))
            {
                matchingNodeIds.Add(node.Run.RunId);
                
                // Mark all ancestors to be shown and expanded
                var parent = node.Parent;
                while (parent != null)
                {
                    ancestorsToExpand.Add(parent.Run.RunId);
                    parent.IsExpanded = true;
                    parent = parent.Parent;
                }
            }
        }

        // Build visible nodes showing matching nodes and their ancestors
        VisibleNodes.Clear();
        foreach (var root in _rootNodes)
        {
            AddFilteredNodes(root, matchingNodeIds, ancestorsToExpand);
        }
    }

    private bool MatchesSearch(RunIndexEntryViewModel run)
    {
        return run.RunId.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               run.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               (run.TestId?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (run.SuiteId?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (run.PlanId?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void AddFilteredNodes(RunTreeNodeViewModel node, HashSet<string> matchingIds, HashSet<string> ancestorIds)
    {
        var isMatch = matchingIds.Contains(node.Run.RunId);
        var isAncestor = ancestorIds.Contains(node.Run.RunId);
        
        if (isMatch || isAncestor)
        {
            VisibleNodes.Add(node);
            
            if (node.IsExpanded || isAncestor)
            {
                foreach (var child in node.Children)
                {
                    AddFilteredNodes(child, matchingIds, ancestorIds);
                }
            }
        }
    }

    /// <summary>
    /// Rebuilds the visible nodes list based on expand/collapse state.
    /// </summary>
    private void RebuildVisibleNodes()
    {
        VisibleNodes.Clear();
        
        if (TopLevelOnly)
        {
            // Show only root nodes, no expand/collapse capability
            foreach (var root in _rootNodes)
            {
                // Reset depth to 0 for display
                root.Depth = 0;
                VisibleNodes.Add(root);
            }
        }
        else
        {
            // Show full tree with expand/collapse capability
            foreach (var root in _rootNodes)
            {
                AddVisibleNodes(root);
            }
        }
    }

    private void AddVisibleNodes(RunTreeNodeViewModel node)
    {
        VisibleNodes.Add(node);
        
        if (node.IsExpanded)
        {
            foreach (var child in node.Children)
            {
                AddVisibleNodes(child);
            }
        }
    }

    /// <summary>
    /// Toggles the expand/collapse state of a node.
    /// </summary>
    public void ToggleNodeExpansion(RunTreeNodeViewModel node)
    {
        if (!node.HasChildren) return;
        
        node.IsExpanded = !node.IsExpanded;
        
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            RebuildVisibleNodes();
        }
        else
        {
            ApplyFilter();
        }
    }

    /// <summary>
    /// Builds the tree structure from flat run list.
    /// </summary>
    private void BuildTree()
    {
        _rootNodes.Clear();
        _nodesByRunId.Clear();
        
        // First pass: create all nodes
        foreach (var run in _allRuns)
        {
            var node = new RunTreeNodeViewModel(run, this);
            _nodesByRunId[run.RunId] = node;
        }
        
        // Second pass: establish parent-child relationships
        foreach (var node in _nodesByRunId.Values)
        {
            if (!string.IsNullOrEmpty(node.Run.ParentRunId) && 
                _nodesByRunId.TryGetValue(node.Run.ParentRunId, out var parentNode))
            {
                parentNode.Children.Add(node);
                node.Parent = parentNode;
                node.Depth = parentNode.Depth + 1;
            }
            else
            {
                // No parent found - this is a root node
                _rootNodes.Add(node);
            }
        }
        
        // Sort root nodes by start time (descending)
        _rootNodes = _rootNodes.OrderByDescending(n => n.Run.StartTime).ToList();
        
        // Sort children by start time
        foreach (var node in _nodesByRunId.Values)
        {
            if (node.Children.Count > 0)
            {
                var sorted = node.Children.OrderBy(c => c.Run.StartTime).ToList();
                node.Children.Clear();
                foreach (var child in sorted)
                {
                    node.Children.Add(child);
                }
            }
        }
        
        // Update depths recursively
        foreach (var root in _rootNodes)
        {
            UpdateDepths(root, 0);
        }
    }

    private void UpdateDepths(RunTreeNodeViewModel node, int depth)
    {
        node.Depth = depth;
        foreach (var child in node.Children)
        {
            UpdateDepths(child, depth + 1);
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
            // Note: For tree view, we need ALL runs (not just top-level) to build the hierarchy
            // But we still apply other filters
            var filter = new RunFilter
            {
                StartTimeFrom = StartTimeFrom,
                StartTimeTo = StartTimeTo,
                Status = GetStatusFilterEnum(),
                RunType = GetRunTypeFilterEnum(),
                TopLevelOnly = false, // Always load all runs for tree building
                MaxResults = 2000 // Increased limit for full hierarchy
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

            // Build tree structure
            BuildTree();
            
            // Apply current filter/search
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
            if (_nodesByRunId.TryGetValue(runId, out var node))
            {
                // Expand ancestors to make the node visible
                var parent = node.Parent;
                while (parent != null)
                {
                    parent.IsExpanded = true;
                    parent = parent.Parent;
                }
                
                RebuildVisibleNodes();
                SelectedNode = node;
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
/// ViewModel for a tree node in the run hierarchy.
/// </summary>
public partial class RunTreeNodeViewModel : ViewModelBase
{
    private readonly HistoryViewModel _viewModel;

    public RunTreeNodeViewModel(RunIndexEntryViewModel run, HistoryViewModel viewModel)
    {
        Run = run;
        _viewModel = viewModel;
        
        // Watch for children changes to update HasChildren and CanExpand
        _children.CollectionChanged += (s, e) => 
        {
            OnPropertyChanged(nameof(HasChildren));
            OnPropertyChanged(nameof(CanExpand));
        };
    }

    /// <summary>
    /// The underlying run data.
    /// </summary>
    public RunIndexEntryViewModel Run { get; }

    /// <summary>
    /// Parent node in the tree (null for root nodes).
    /// </summary>
    public RunTreeNodeViewModel? Parent { get; set; }

    /// <summary>
    /// Child nodes.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<RunTreeNodeViewModel> _children = new();

    partial void OnChildrenChanged(ObservableCollection<RunTreeNodeViewModel> value)
    {
        OnPropertyChanged(nameof(HasChildren));
        OnPropertyChanged(nameof(CanExpand));
        value.CollectionChanged += (s, e) => 
        {
            OnPropertyChanged(nameof(HasChildren));
            OnPropertyChanged(nameof(CanExpand));
        };
    }

    /// <summary>
    /// Whether this node is expanded.
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Depth in the tree (0 for root nodes).
    /// </summary>
    [ObservableProperty]
    private int _depth;

    /// <summary>
    /// Whether this node has children.
    /// </summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>
    /// Whether expand/collapse is available (has children and not in top-level-only mode).
    /// </summary>
    public bool CanExpand => HasChildren && !_viewModel.TopLevelOnly;

    /// <summary>
    /// Notifies that CanExpand may have changed.
    /// </summary>
    public void NotifyCanExpandChanged()
    {
        OnPropertyChanged(nameof(CanExpand));
    }

    /// <summary>
    /// Toggles the expand/collapse state.
    /// </summary>
    [RelayCommand]
    private void ToggleExpand()
    {
        _viewModel.ToggleNodeExpansion(this);
    }
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
