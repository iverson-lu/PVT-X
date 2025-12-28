using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Engine.Discovery;
using PcTest.Ui.Services;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// ViewModel for editing a single suite.
/// </summary>
public partial class SuiteEditorViewModel : EditableViewModelBase
{
    private readonly ISuiteRepository _suiteRepository;
    private readonly IDiscoveryService _discoveryService;
    private readonly IFileDialogService _fileDialogService;
    private readonly INavigationService _navigationService;

    private SuiteInfo? _originalSuiteInfo;
    private bool _isNew;

    public event EventHandler? Saved;

    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _tagsText = string.Empty;
    
    // Controls
    [ObservableProperty] private int _repeat = 1;
    [ObservableProperty] private int _maxParallel = 1;
    [ObservableProperty] private bool _continueOnFailure;
    [ObservableProperty] private int _retryOnError;

    [ObservableProperty]
    private ObservableCollection<TestCaseNodeViewModel> _nodes = new();

    [ObservableProperty]
    private TestCaseNodeViewModel? _selectedNode;

    [ObservableProperty]
    private ObservableCollection<TestCaseItemViewModel> _availableTestCases = new();

    public SuiteEditorViewModel(
        ISuiteRepository suiteRepository,
        IDiscoveryService discoveryService,
        IFileDialogService fileDialogService,
        INavigationService navigationService)
    {
        _suiteRepository = suiteRepository;
        _discoveryService = discoveryService;
        _fileDialogService = fileDialogService;
        _navigationService = navigationService;
    }

    // Track property changes for dirty state
    partial void OnIdChanged(string value) => MarkDirty();
    partial void OnNameChanged(string value) => MarkDirty();
    partial void OnVersionChanged(string value) => MarkDirty();
    partial void OnDescriptionChanged(string value) => MarkDirty();
    partial void OnTagsTextChanged(string value) => MarkDirty();
    partial void OnRepeatChanged(int value) => MarkDirty();
    partial void OnMaxParallelChanged(int value) => MarkDirty();
    partial void OnContinueOnFailureChanged(bool value) => MarkDirty();
    partial void OnRetryOnErrorChanged(int value) => MarkDirty();

    public async Task LoadAsync(SuiteInfo suiteInfo, bool isNew = false)
    {
        _originalSuiteInfo = suiteInfo;
        _isNew = isNew;

        // Populate fields from manifest
        var m = suiteInfo.Manifest;
        Id = m.Id;
        Name = m.Name;
        Version = m.Version;
        Description = m.Description ?? string.Empty;
        TagsText = m.Tags is not null ? string.Join(", ", m.Tags) : string.Empty;

        if (m.Controls is not null)
        {
            Repeat = m.Controls.Repeat;
            MaxParallel = m.Controls.MaxParallel;
            ContinueOnFailure = m.Controls.ContinueOnFailure;
            RetryOnError = m.Controls.RetryOnError;
        }

        // Load nodes
        Nodes.Clear();
        foreach (var node in m.TestCases)
        {
            var nodeVm = new TestCaseNodeViewModel
            {
                NodeId = node.NodeId,
                Ref = node.Ref,
                InputsJson = node.Inputs is not null 
                    ? JsonSerializer.Serialize(node.Inputs, new JsonSerializerOptions { WriteIndented = true }) 
                    : "{}"
            };
            nodeVm.PropertyChanged += (s, e) => MarkDirty();
            Nodes.Add(nodeVm);
        }

        // Load available test cases
        await LoadAvailableTestCasesAsync();

        // Clear dirty state after loading
        ClearDirty();
    }

    private async Task LoadAvailableTestCasesAsync()
    {
        AvailableTestCases.Clear();
        
        var discovery = _discoveryService.CurrentDiscovery;
        if (discovery is null)
        {
            discovery = await _discoveryService.DiscoverAsync();
        }

        foreach (var tc in discovery.TestCases.Values.OrderBy(c => c.Manifest.Name))
        {
            AvailableTestCases.Add(new TestCaseItemViewModel
            {
                Id = tc.Manifest.Id,
                Name = tc.Manifest.Name,
                Version = tc.Manifest.Version,
                Parameters = tc.Manifest.Parameters?.ToList() ?? new()
            });
        }
    }

    [RelayCommand]
    private void AddNode()
    {
        var nodeId = $"node_{Nodes.Count + 1}";
        var nodeVm = new TestCaseNodeViewModel
        {
            NodeId = nodeId,
            Ref = string.Empty,
            InputsJson = "{}"
        };
        nodeVm.PropertyChanged += (s, e) => MarkDirty();
        Nodes.Add(nodeVm);
        SelectedNode = nodeVm;
        MarkDirty();
    }

    [RelayCommand]
    private void RemoveNode()
    {
        if (SelectedNode is null) return;
        Nodes.Remove(SelectedNode);
        SelectedNode = Nodes.FirstOrDefault();
        MarkDirty();
    }

    [RelayCommand]
    private void MoveNodeUp()
    {
        if (SelectedNode is null) return;
        var index = Nodes.IndexOf(SelectedNode);
        if (index <= 0) return;
        Nodes.Move(index, index - 1);
        MarkDirty();
    }

    [RelayCommand]
    private void MoveNodeDown()
    {
        if (SelectedNode is null) return;
        var index = Nodes.IndexOf(SelectedNode);
        if (index < 0 || index >= Nodes.Count - 1) return;
        Nodes.Move(index, index + 1);
        MarkDirty();
    }

    public override void Validate()
    {
        var manifest = BuildManifest();
        var result = _suiteRepository.ValidateSuite(manifest);
        
        IsValid = result.IsValid;
        ValidationMessage = result.IsValid 
            ? "Suite is valid" 
            : string.Join("\n", result.Errors);
    }

    [RelayCommand]
    private void ValidateSuite()
    {
        Validate();
        
        if (IsValid)
        {
            _fileDialogService.ShowInfo("Validation", "Suite is valid.");
        }
        else
        {
            _fileDialogService.ShowWarning("Validation", ValidationMessage ?? "Validation failed");
        }
    }

    public override async Task SaveAsync()
    {
        Validate();
        if (!IsValid)
        {
            _fileDialogService.ShowError("Cannot Save", ValidationMessage ?? "Suite is not valid");
            return;
        }

        var manifest = BuildManifest();

        if (_isNew)
        {
            var suiteInfo = await _suiteRepository.CreateAsync(manifest);
            _originalSuiteInfo = suiteInfo;
            _isNew = false;
        }
        else if (_originalSuiteInfo is not null)
        {
            _originalSuiteInfo.Manifest = manifest;
            await _suiteRepository.UpdateAsync(_originalSuiteInfo);
        }

        ClearDirty();
        Saved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task SaveSuiteAsync()
    {
        await SaveAsync();
    }

    public override void Discard()
    {
        if (_originalSuiteInfo is not null && !_isNew)
        {
            // Reload from original
            _ = LoadAsync(_originalSuiteInfo, false);
        }
        else
        {
            // Clear for new suite
            ClearDirty();
        }
    }

    [RelayCommand]
    private void DiscardChanges()
    {
        if (IsDirty)
        {
            var confirmed = _fileDialogService.ShowConfirmation(
                "Discard Changes",
                "Are you sure you want to discard all changes?");
            if (!confirmed) return;
        }
        
        Discard();
    }

    [RelayCommand]
    private void RunSuite()
    {
        if (IsDirty)
        {
            _fileDialogService.ShowWarning("Unsaved Changes", "Please save the suite before running.");
            return;
        }

        var identity = $"{Id}@{Version}";
        _navigationService.NavigateToRun(identity, RunType.TestSuite);
    }

    private TestSuiteManifest BuildManifest()
    {
        var manifest = new TestSuiteManifest
        {
            SchemaVersion = "1.5.0",
            Id = Id,
            Name = Name,
            Version = Version,
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description,
            Tags = string.IsNullOrWhiteSpace(TagsText) 
                ? null 
                : TagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Controls = new SuiteControls
            {
                Repeat = Repeat,
                MaxParallel = MaxParallel,
                ContinueOnFailure = ContinueOnFailure,
                RetryOnError = RetryOnError
            }
        };

        foreach (var nodeVm in Nodes)
        {
            var node = new TestCaseNode
            {
                NodeId = nodeVm.NodeId,
                Ref = nodeVm.Ref
            };

            if (!string.IsNullOrWhiteSpace(nodeVm.InputsJson) && nodeVm.InputsJson != "{}")
            {
                try
                {
                    var inputs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(nodeVm.InputsJson);
                    node.Inputs = inputs;
                }
                catch
                {
                    // Keep null if invalid JSON
                }
            }

            manifest.TestCases.Add(node);
        }

        return manifest;
    }
}

/// <summary>
/// ViewModel for a test case node in a suite.
/// </summary>
public partial class TestCaseNodeViewModel : ViewModelBase
{
    [ObservableProperty] private string _nodeId = string.Empty;
    [ObservableProperty] private string _ref = string.Empty;
    [ObservableProperty] private string _inputsJson = "{}";

    public string DisplayName => string.IsNullOrEmpty(Ref) ? NodeId : $"{NodeId} ({Ref})";
}
