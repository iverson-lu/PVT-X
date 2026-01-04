using System.Collections.ObjectModel;
using System.IO;
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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditSuiteCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSuiteCommand))]
    private bool _isEditing;
    
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _tagsText = string.Empty;
    
    // Computed property for tags as a collection
    public List<string> Tags => string.IsNullOrWhiteSpace(TagsText)
        ? new List<string>()
        : TagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    
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

    [ObservableProperty]
    private EnvVarEditorViewModel _environmentEditor = new();

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

        // Hook up environment editor to mark dirty on changes
        EnvironmentEditor.OnRowChanged = MarkDirty;
    }

    // Track property changes for dirty state
    partial void OnIdChanged(string value) => MarkDirty();
    partial void OnNameChanged(string value) => MarkDirty();
    partial void OnVersionChanged(string value) => MarkDirty();
    partial void OnDescriptionChanged(string value) => MarkDirty();
    partial void OnTagsTextChanged(string value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(Tags));
    }
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
        
        // Load available test cases first (needed for parameter discovery)
        await LoadAvailableTestCasesAsync();
        
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
            
            // Load parameters for this test case
            await LoadNodeParametersAsync(nodeVm, node.Inputs);
            
            nodeVm.PropertyChanged += (s, e) => MarkDirty();
            Nodes.Add(nodeVm);
        }

        // Load environment variables
        EnvironmentEditor.LoadFromDictionary(m.Environment?.Env);

        // Clear dirty state after loading
        ClearDirty();
        IsEditing = false;
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
            var paramWrappers = tc.Manifest.Parameters?
                .Select(p => new ParameterViewModel(p))
                .ToList() ?? new();
                
            AvailableTestCases.Add(new TestCaseItemViewModel
            {
                Id = tc.Manifest.Id,
                Name = tc.Manifest.Name,
                Version = tc.Manifest.Version,
                ParameterWrappers = paramWrappers
            });
        }
    }
    
    private async Task LoadNodeParametersAsync(TestCaseNodeViewModel nodeVm, Dictionary<string, JsonElement>? inputs)
    {
        var discovery = _discoveryService.CurrentDiscovery;
        if (discovery is null)
        {
            discovery = await _discoveryService.DiscoverAsync();
        }
        
        // Find the test case by NodeId (which is the test case ID)
        var testCase = discovery.TestCases.Values.FirstOrDefault(tc => 
            tc.Manifest.Id.Equals(nodeVm.NodeId, StringComparison.OrdinalIgnoreCase));
            
        if (testCase?.Manifest.Parameters is null)
            return;
            
        nodeVm.Parameters.Clear();
        
        foreach (var paramDef in testCase.Manifest.Parameters)
        {
            var paramVm = new ParameterViewModel(paramDef);
            
            // If there's an existing input value, use it
            if (inputs?.TryGetValue(paramDef.Name, out var value) == true)
            {
                try
                {
                    paramVm.CurrentValue = value.ValueKind switch
                    {
                        JsonValueKind.String => value.GetString() ?? string.Empty,
                        JsonValueKind.Number => value.ToString(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => value.ToString()
                    };
                }
                catch { }
            }
            
            paramVm.PropertyChanged += (s, e) => MarkDirty();
            nodeVm.Parameters.Add(paramVm);
        }
    }

    [RelayCommand]
    private async Task AddNodeAsync()
    {
        // Get current discovery
        var discovery = _discoveryService.CurrentDiscovery;
        if (discovery is null)
        {
            discovery = await _discoveryService.DiscoverAsync();
        }

        // Get existing refs to exclude from picker
        var existingRefs = Nodes.Select(n => n.Ref).Where(r => !string.IsNullOrEmpty(r)).ToList();

        // Show picker dialog
        var selected = _fileDialogService.ShowTestCasePicker(discovery, existingRefs);
        
        if (selected.Count == 0)
            return;

        // Add nodes for each selected test case
        foreach (var tc in selected)
        {
            var nodeId = GenerateUniqueNodeId(tc.Id);
            var nodeVm = new TestCaseNodeViewModel
            {
                NodeId = nodeId,
                Ref = tc.Name,
                InputsJson = "{}"
            };
            
            // Load parameters for this test case
            await LoadNodeParametersAsync(nodeVm, null);
            
            nodeVm.PropertyChanged += (s, e) => MarkDirty();
            Nodes.Add(nodeVm);
        }

        // Select the last added node
        SelectedNode = Nodes.LastOrDefault();
        MarkDirty();
    }

    private string GenerateUniqueNodeId(string baseId)
    {
        var nodeId = baseId;
        var counter = 1;
        
        while (Nodes.Any(n => n.NodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase)))
        {
            nodeId = $"{baseId}_{counter}";
            counter++;
        }
        
        return nodeId;
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

    [RelayCommand(CanExecute = nameof(CanEditSuite))]
    private void EditSuite()
    {
        IsEditing = true;
    }

    private bool CanEditSuite() => !IsEditing;

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
        IsEditing = false;
        Saved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanSaveSuite))]
    private async Task SaveSuiteAsync()
    {
        await SaveAsync();
    }

    private bool CanSaveSuite() => IsEditing || IsDirty;

    protected override void OnIsDirtyChanged()
    {
        SaveSuiteCommand.NotifyCanExecuteChanged();
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
            IsEditing = false;
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
        var navParam = new RunNavigationParameter
        {
            TargetIdentity = identity,
            RunType = RunType.TestSuite,
            AutoStart = true,
            SourcePage = "Plan",
            SourceTabIndex = 1  // Suites tab is index 1
        };
        _navigationService.NavigateTo("Run", navParam);
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

        // Add environment variables
        var envDict = EnvironmentEditor.ToDictionary();
        if (envDict is not null)
        {
            manifest.Environment = new SuiteEnvironment
            {
                Env = envDict
            };
        }

        foreach (var nodeVm in Nodes)
        {
            var node = new TestCaseNode
            {
                NodeId = nodeVm.NodeId,
                Ref = nodeVm.Ref
            };

            // Build inputs from parameters if any
            if (nodeVm.Parameters.Count > 0)
            {
                var inputs = new Dictionary<string, JsonElement>();
                foreach (var param in nodeVm.Parameters)
                {
                    if (!string.IsNullOrWhiteSpace(param.CurrentValue))
                    {
                        // Parse the value based on type
                        try
                        {
                            JsonElement element = param.Type.ToLowerInvariant() switch
                            {
                                "boolean" => JsonSerializer.SerializeToElement(bool.Parse(param.CurrentValue)),
                                "integer" => JsonSerializer.SerializeToElement(int.Parse(param.CurrentValue)),
                                "number" => JsonSerializer.SerializeToElement(double.Parse(param.CurrentValue)),
                                _ => JsonSerializer.SerializeToElement(param.CurrentValue)
                            };
                            inputs[param.Name] = element;
                        }
                        catch
                        {
                            // If parsing fails, store as string
                            inputs[param.Name] = JsonSerializer.SerializeToElement(param.CurrentValue);
                        }
                    }
                }
                
                if (inputs.Count > 0)
                {
                    node.Inputs = inputs;
                }
            }
            else if (!string.IsNullOrWhiteSpace(nodeVm.InputsJson) && nodeVm.InputsJson != "{}")
            {
                // Fallback to JSON if no parameters (backward compatibility)
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
    [ObservableProperty] private ObservableCollection<ParameterViewModel> _parameters = new();

    public TestCaseNodeViewModel()
    {
        Parameters.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasParameters));
    }

    public string DisplayName => string.IsNullOrEmpty(Ref) ? NodeId : $"{NodeId} ({Ref})";
    public bool HasParameters => Parameters.Count > 0;
}
