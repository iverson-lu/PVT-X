using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTest.Contracts;
using PcTest.Ui.Services;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// ViewModel for the Logs & Results page.
/// </summary>
public partial class LogsResultsViewModel : ViewModelBase
{
    private readonly IRunRepository _runRepository;
    private readonly IFileSystemService _fileSystemService;
    private readonly IFileDialogService _fileDialogService;
    private readonly INavigationService _navigationService;

    [ObservableProperty] private bool _isRunPickerVisible = true;
    [ObservableProperty] private bool _isContentVisible;
    
    [ObservableProperty] private string? _runId;
    [ObservableProperty] private RunDetails? _runDetails;

    // Artifacts
    [ObservableProperty]
    private ObservableCollection<ArtifactNodeViewModel> _artifactTree = new();

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

    // Run picker
    [ObservableProperty]
    private RunPickerViewModel _runPicker = null!;

    private List<StructuredEventViewModel> _allEvents = new();

    public LogsResultsViewModel(
        IRunRepository runRepository,
        IFileSystemService fileSystemService,
        IFileDialogService fileDialogService,
        INavigationService navigationService)
    {
        _runRepository = runRepository;
        _fileSystemService = fileSystemService;
        _fileDialogService = fileDialogService;
        _navigationService = navigationService;

        RunPicker = new RunPickerViewModel(runRepository);
        RunPicker.RunSelected += OnRunSelected;
    }

    private async void OnRunSelected(object? sender, string runId)
    {
        await LoadRunAsync(runId);
    }

    public async Task InitializeAsync(object? parameter)
    {
        if (parameter is string runId && !string.IsNullOrEmpty(runId))
        {
            await LoadRunAsync(runId);
        }
        else
        {
            // Show run picker
            IsRunPickerVisible = true;
            IsContentVisible = false;
            await RunPicker.LoadRecentRunsAsync();
        }
    }

    partial void OnSelectedArtifactChanged(ArtifactNodeViewModel? value)
    {
        if (value is not null && !value.IsDirectory)
        {
            _ = LoadArtifactContentAsync(value);
        }
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

    public async Task LoadRunAsync(string runId)
    {
        SetBusy(true, "Loading run details...");

        try
        {
            RunId = runId;
            RunDetails = await _runRepository.GetRunDetailsAsync(runId);

            if (RunDetails is null)
            {
                _fileDialogService.ShowError("Run Not Found", $"Could not load run: {runId}");
                return;
            }

            // Load artifacts tree
            await LoadArtifactsAsync();

            // Load events
            await LoadEventsAsync();

            IsRunPickerVisible = false;
            IsContentVisible = true;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadArtifactsAsync()
    {
        if (RunId is null) return;

        ArtifactTree.Clear();
        var artifacts = await _runRepository.GetArtifactsAsync(RunId);

        foreach (var artifact in artifacts)
        {
            ArtifactTree.Add(CreateArtifactNode(artifact));
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

    private async Task LoadArtifactContentAsync(ArtifactNodeViewModel artifact)
    {
        if (RunId is null) return;

        try
        {
            var content = await _runRepository.ReadArtifactAsync(RunId, artifact.RelativePath);
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

    private async Task LoadEventsAsync()
    {
        if (RunId is null) return;

        _allEvents.Clear();
        Events.Clear();

        await foreach (var batch in _runRepository.StreamEventsAsync(RunId))
        {
            foreach (var evt in batch.Events)
            {
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

        ApplyEventFilter();
    }

    [RelayCommand]
    private void ShowRunPicker()
    {
        IsRunPickerVisible = true;
        IsContentVisible = false;
        _ = RunPicker.LoadRecentRunsAsync();
    }

    [RelayCommand]
    private void OpenRunFolder()
    {
        if (RunId is null) return;

        var folderPath = _runRepository.GetRunFolderPath(RunId);
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

    [RelayCommand]
    private void ClearEventFilters()
    {
        EventSearchText = string.Empty;
        ErrorsOnly = false;
        NodeIdFilter = null;
        SelectedLevels = new ObservableCollection<string> { "info", "warning", "error" };
        ApplyEventFilter();
    }

    public IEnumerable<string> AvailableLevels => new[] { "trace", "debug", "info", "warning", "error" };
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
