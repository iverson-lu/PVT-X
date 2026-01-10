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
        
        // Strip _1, _2, etc. suffix from nodeId to get the actual test case identity
        // e.g., "hw.bios.version_check@1.0.0_1" -> "hw.bios.version_check@1.0.0"
        var testCaseIdentity = StripNodeIdSuffix(nodeVm.NodeId);
        
        // Find the test case by the stripped identity (id@version)
        var testCase = discovery.TestCases.Values.FirstOrDefault(tc => 
            tc.Identity.Equals(testCaseIdentity, StringComparison.OrdinalIgnoreCase));

        if (testCase is not null)
        {
            nodeVm.Privilege = testCase.Manifest.Privilege;
        }
            
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
            // Use id@version format for nodeId
            var baseNodeId = $"{tc.Id}@{tc.Version}";
            var nodeId = GenerateUniqueNodeId(baseNodeId);
            var nodeVm = new TestCaseNodeViewModel
            {
                NodeId = nodeId,
                Ref = tc.Name,
                InputsJson = "{}",
                Privilege = tc.Privilege
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

    /// <summary>
    /// Strips the _1, _2, etc. suffix from a nodeId to get the base test case identity.
    /// e.g., "hw.bios.version_check@1.0.0_1" -> "hw.bios.version_check@1.0.0"
    /// </summary>
    private static string StripNodeIdSuffix(string nodeId)
    {
        var match = System.Text.RegularExpressions.Regex.Match(nodeId, @"^(.+)_(\d+)$");
        return match.Success ? match.Groups[1].Value : nodeId;
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
        var identity = $"{manifest.Id}@{manifest.Version}";

        // Check for duplicate identity (id@version)
        if (_isNew)
        {
            var allSuites = await _suiteRepository.GetAllAsync();
            
            // Check for duplicate identity
            var duplicateIdentity = allSuites.FirstOrDefault(s => 
                $"{s.Manifest.Id}@{s.Manifest.Version}".Equals(identity, StringComparison.OrdinalIgnoreCase));
            
            if (duplicateIdentity != null)
            {
                _fileDialogService.ShowError("Cannot Save", 
                    $"A suite with identity '{identity}' already exists.\nEach suite must have a unique combination of ID and Version.");
                return;
            }
            
            // Check for duplicate name
            var duplicateName = allSuites.FirstOrDefault(s => 
                s.Manifest.Name.Equals(manifest.Name, StringComparison.OrdinalIgnoreCase));
            
            if (duplicateName != null)
            {
                _fileDialogService.ShowError("Cannot Save", 
                    $"A suite with name '{manifest.Name}' already exists.\nEach suite must have a unique name.");
                return;
            }
        }
        else if (_originalSuiteInfo is not null)
        {
            // Check if identity changed and conflicts with another suite
            var originalIdentity = $"{_originalSuiteInfo.Manifest.Id}@{_originalSuiteInfo.Manifest.Version}";
            if (!identity.Equals(originalIdentity, StringComparison.OrdinalIgnoreCase))
            {
                var allSuites = await _suiteRepository.GetAllAsync();
                var duplicateIdentity = allSuites.FirstOrDefault(s => 
                    $"{s.Manifest.Id}@{s.Manifest.Version}".Equals(identity, StringComparison.OrdinalIgnoreCase));
                
                if (duplicateIdentity != null)
                {
                    _fileDialogService.ShowError("Cannot Save", 
                        $"A suite with identity '{identity}' already exists.\nEach suite must have a unique combination of ID and Version.");
                    return;
                }
            }
            
            // Check if name changed and conflicts with another suite
            var originalName = _originalSuiteInfo.Manifest.Name;
            if (!manifest.Name.Equals(originalName, StringComparison.OrdinalIgnoreCase))
            {
                var allSuites = await _suiteRepository.GetAllAsync();
                var duplicateName = allSuites.FirstOrDefault(s => 
                    s.Manifest.Name.Equals(manifest.Name, StringComparison.OrdinalIgnoreCase));
                
                if (duplicateName != null)
                {
                    _fileDialogService.ShowError("Cannot Save", 
                        $"A suite with name '{manifest.Name}' already exists.\nEach suite must have a unique name.");
                    return;
                }
            }
        }

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
    private async Task RunSuite()
    {
        if (IsDirty)
        {
            _fileDialogService.ShowWarning("Unsaved Changes", "Please save the suite before running.");
            return;
        }

        var identity = $"{Id}@{Version}";

        // Check privilege requirements before navigation
        var discovery = _discoveryService.CurrentDiscovery;
        if (discovery is null)
        {
            discovery = await _discoveryService.DiscoverAsync();
        }

        var (isValid, requiredPrivilege, message) = PcTest.Engine.PrivilegeChecker.ValidatePrivilege(
            RunType.TestSuite, identity, discovery);

        if (!isValid)
        {
            if (requiredPrivilege == PcTest.Contracts.Privilege.AdminRequired)
            {
                // AdminRequired: Block execution
                _fileDialogService.ShowError("Administrator Privileges Required", message!);
                return;
            }
            else if (requiredPrivilege == PcTest.Contracts.Privilege.AdminPreferred)
            {
                // AdminPreferred: Show warning and let user decide
                var continueAnyway = _fileDialogService.ShowConfirmation(
                    "Administrator Privileges Recommended", message!);
                
                if (!continueAnyway)
                {
                    return;
                }
            }
        }

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
    [ObservableProperty] private PcTest.Contracts.Privilege _privilege = PcTest.Contracts.Privilege.User;

    public TestCaseNodeViewModel()
    {
        Parameters.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasParameters));
    }

    public string DisplayName => string.IsNullOrEmpty(Ref) ? NodeId : $"{NodeId} ({Ref})";
    public bool HasParameters => Parameters.Count > 0;
    public bool IsAdminRequired => Privilege == PcTest.Contracts.Privilege.AdminRequired;
    public bool IsAdminPreferred => Privilege == PcTest.Contracts.Privilege.AdminPreferred;

    partial void OnPrivilegeChanged(PcTest.Contracts.Privilege value)
    {
        OnPropertyChanged(nameof(IsAdminRequired));
        OnPropertyChanged(nameof(IsAdminPreferred));
    }
}
