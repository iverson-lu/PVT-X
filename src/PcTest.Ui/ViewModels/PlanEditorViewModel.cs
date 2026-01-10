using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Ui.Services;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// ViewModel for editing a single plan.
/// </summary>
public partial class PlanEditorViewModel : EditableViewModelBase
{
    private readonly IPlanRepository _planRepository;
    private readonly ISuiteRepository _suiteRepository;
    private readonly IDiscoveryService _discoveryService;
    private readonly IFileDialogService _fileDialogService;
    private readonly INavigationService _navigationService;

    private PlanInfo? _originalPlanInfo;
    private bool _isNew;

    public event EventHandler? Saved;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditPlanCommand))]
    [NotifyCanExecuteChangedFor(nameof(SavePlanCommand))]
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

    [ObservableProperty]
    private ObservableCollection<SuiteReferenceViewModel> _suiteReferences = new();

    [ObservableProperty]
    private SuiteReferenceViewModel? _selectedSuiteReference;

    [ObservableProperty]
    private ObservableCollection<SuiteListItemViewModel> _availableSuites = new();

    [ObservableProperty]
    private EnvVarEditorViewModel _environmentEditor = new();

    public PlanEditorViewModel(
        IPlanRepository planRepository,
        ISuiteRepository suiteRepository,
        IDiscoveryService discoveryService,
        IFileDialogService fileDialogService,
        INavigationService navigationService)
    {
        _planRepository = planRepository;
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

    public async Task LoadAsync(PlanInfo planInfo, bool isNew = false)
    {
        _originalPlanInfo = planInfo;
        _isNew = isNew;

        var m = planInfo.Manifest;
        Id = m.Id;
        Name = m.Name;
        Version = m.Version;
        Description = m.Description ?? string.Empty;
        TagsText = m.Tags is not null ? string.Join(", ", m.Tags) : string.Empty;

        // Load suite references
        SuiteReferences.Clear();
        foreach (var suiteRef in m.Suites)
        {
            var refVm = new SuiteReferenceViewModel(_discoveryService) { SuiteIdentity = suiteRef };
            refVm.PropertyChanged += (s, e) => MarkDirty();
            SuiteReferences.Add(refVm);
        }

        // Load environment variables
        EnvironmentEditor.LoadFromDictionary(m.Environment?.Env);

        // Load available suites
        await LoadAvailableSuitesAsync();

        ClearDirty();
        IsEditing = false;
    }

    private async Task LoadAvailableSuitesAsync()
    {
        AvailableSuites.Clear();

        var suites = await _suiteRepository.GetAllAsync();
        foreach (var suite in suites.OrderBy(s => s.Manifest.Name))
        {
            AvailableSuites.Add(new SuiteListItemViewModel
            {
                Id = suite.Manifest.Id,
                Name = suite.Manifest.Name,
                Version = suite.Manifest.Version,
                NodeCount = suite.Manifest.TestCases?.Count ?? 0
            });
        }
    }

    [RelayCommand]
    private async Task AddSuiteReferenceAsync()
    {
        // Get existing suite identities to exclude from picker
        var existingIdentities = SuiteReferences.Select(r => r.SuiteIdentity).ToList();

        // Show picker dialog
        var selected = _fileDialogService.ShowSuitePicker(AvailableSuites, existingIdentities);
        
        if (selected.Count == 0)
            return;

        // Add suite references for each selected suite
        foreach (var identity in selected)
        {
            var refVm = new SuiteReferenceViewModel(_discoveryService)
            {
                SuiteIdentity = identity
            };
            refVm.PropertyChanged += (s, e) => MarkDirty();
            SuiteReferences.Add(refVm);
        }

        // Select the last added suite reference
        SelectedSuiteReference = SuiteReferences.LastOrDefault();
        MarkDirty();
    }

    [RelayCommand]
    private void RemoveSuiteReference()
    {
        if (SelectedSuiteReference is null) return;
        SuiteReferences.Remove(SelectedSuiteReference);
        SelectedSuiteReference = SuiteReferences.FirstOrDefault();
        MarkDirty();
    }

    [RelayCommand]
    private void MoveSuiteReferenceUp()
    {
        if (SelectedSuiteReference is null) return;
        var index = SuiteReferences.IndexOf(SelectedSuiteReference);
        if (index <= 0) return;
        SuiteReferences.Move(index, index - 1);
        MarkDirty();
    }

    [RelayCommand]
    private void MoveSuiteReferenceDown()
    {
        if (SelectedSuiteReference is null) return;
        var index = SuiteReferences.IndexOf(SelectedSuiteReference);
        if (index < 0 || index >= SuiteReferences.Count - 1) return;
        SuiteReferences.Move(index, index + 1);
        MarkDirty();
    }

    public override void Validate()
    {
        var manifest = BuildManifest();
        var result = _planRepository.ValidatePlan(manifest);

        IsValid = result.IsValid;
        ValidationMessage = result.IsValid
            ? "Plan is valid"
            : string.Join("\n", result.Errors);
    }

    [RelayCommand(CanExecute = nameof(CanEditPlan))]
    private void EditPlan()
    {
        IsEditing = true;
    }

    private bool CanEditPlan() => !IsEditing;

    [RelayCommand]
    private void ValidatePlan()
    {
        Validate();

        if (IsValid)
        {
            _fileDialogService.ShowInfo("Validation", "Plan is valid.");
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
            _fileDialogService.ShowError("Cannot Save", ValidationMessage ?? "Plan is not valid");
            return;
        }

        var manifest = BuildManifest();
        var identity = $"{manifest.Id}@{manifest.Version}";

        // Check for duplicate identity (id@version)
        if (_isNew)
        {
            var allPlans = await _planRepository.GetAllAsync();
            
            // Check for duplicate identity
            var duplicateIdentity = allPlans.FirstOrDefault(p => 
                $"{p.Manifest.Id}@{p.Manifest.Version}".Equals(identity, StringComparison.OrdinalIgnoreCase));
            
            if (duplicateIdentity != null)
            {
                _fileDialogService.ShowError("Cannot Save", 
                    $"A plan with identity '{identity}' already exists.\nEach plan must have a unique combination of ID and Version.");
                return;
            }
            
            // Check for duplicate name
            var duplicateName = allPlans.FirstOrDefault(p => 
                p.Manifest.Name.Equals(manifest.Name, StringComparison.OrdinalIgnoreCase));
            
            if (duplicateName != null)
            {
                _fileDialogService.ShowError("Cannot Save", 
                    $"A plan with name '{manifest.Name}' already exists.\nEach plan must have a unique name.");
                return;
            }
        }
        else if (_originalPlanInfo is not null)
        {
            // Check if identity changed and conflicts with another plan
            var originalIdentity = $"{_originalPlanInfo.Manifest.Id}@{_originalPlanInfo.Manifest.Version}";
            if (!identity.Equals(originalIdentity, StringComparison.OrdinalIgnoreCase))
            {
                var allPlans = await _planRepository.GetAllAsync();
                var duplicateIdentity = allPlans.FirstOrDefault(p => 
                    $"{p.Manifest.Id}@{p.Manifest.Version}".Equals(identity, StringComparison.OrdinalIgnoreCase));
                
                if (duplicateIdentity != null)
                {
                    _fileDialogService.ShowError("Cannot Save", 
                        $"A plan with identity '{identity}' already exists.\nEach plan must have a unique combination of ID and Version.");
                    return;
                }
            }
            
            // Check if name changed and conflicts with another plan
            var originalName = _originalPlanInfo.Manifest.Name;
            if (!manifest.Name.Equals(originalName, StringComparison.OrdinalIgnoreCase))
            {
                var allPlans = await _planRepository.GetAllAsync();
                var duplicateName = allPlans.FirstOrDefault(p => 
                    p.Manifest.Name.Equals(manifest.Name, StringComparison.OrdinalIgnoreCase));
                
                if (duplicateName != null)
                {
                    _fileDialogService.ShowError("Cannot Save", 
                        $"A plan with name '{manifest.Name}' already exists.\nEach plan must have a unique name.");
                    return;
                }
            }
        }

        if (_isNew)
        {
            var planInfo = await _planRepository.CreateAsync(manifest);
            _originalPlanInfo = planInfo;
            _isNew = false;
        }
        else if (_originalPlanInfo is not null)
        {
            _originalPlanInfo.Manifest = manifest;
            await _planRepository.UpdateAsync(_originalPlanInfo);
        }

        ClearDirty();
        IsEditing = false;
        Saved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanSavePlan))]
    private async Task SavePlanAsync()
    {
        await SaveAsync();
    }

    private bool CanSavePlan() => IsEditing || IsDirty;

    protected override void OnIsDirtyChanged()
    {
        SavePlanCommand.NotifyCanExecuteChanged();
    }

    public override void Discard()
    {
        if (_originalPlanInfo is not null && !_isNew)
        {
            _ = LoadAsync(_originalPlanInfo, false);
        }
        else
        {
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
    private async Task RunPlan()
    {
        if (IsDirty)
        {
            _fileDialogService.ShowWarning("Unsaved Changes", "Please save the plan before running.");
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
            RunType.TestPlan, identity, discovery);

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
            RunType = RunType.TestPlan,
            AutoStart = true,
            SourcePage = "Plan",
            SourceTabIndex = 2  // Plans tab is index 2
        };
        _navigationService.NavigateTo("Run", navParam);
    }

    private TestPlanManifest BuildManifest()
    {
        var manifest = new TestPlanManifest
        {
            SchemaVersion = "1.5.0",
            Id = Id,
            Name = Name,
            Version = Version,
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description,
            Tags = string.IsNullOrWhiteSpace(TagsText)
                ? null
                : TagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
        };

        manifest.Suites = SuiteReferences.Select(sr => sr.SuiteIdentity).ToList();

        // Add environment variables
        var envDict = EnvironmentEditor.ToDictionary();
        if (envDict is not null)
        {
            manifest.Environment = new PlanEnvironment
            {
                Env = envDict
            };
        }

        return manifest;
    }
}

/// <summary>
/// ViewModel for a suite reference in a plan.
/// </summary>
public partial class SuiteReferenceViewModel : ViewModelBase
{
    private readonly IDiscoveryService? _discoveryService;
    
    [ObservableProperty] private string _suiteIdentity = string.Empty;

    public string DisplayName => SuiteIdentity;
    
    // Parse identity to extract Name and Id
    public string Name
    {
        get
        {
            // Try to get suite name from discovery service
            if (_discoveryService?.CurrentDiscovery?.TestSuites != null)
            {
                var suite = _discoveryService.CurrentDiscovery.TestSuites.Values
                    .FirstOrDefault(s => $"{s.Manifest.Id}@{s.Manifest.Version}" == SuiteIdentity);
                if (suite != null)
                    return suite.Manifest.Name;
            }
            
            // Fallback: return the full identity
            return SuiteIdentity;
        }
    }
    
    public string Id
    {
        get
        {
            // Return full identity (id@version) to show version information
            return SuiteIdentity;
        }
    }
    
    public int CaseCount
    {
        get
        {
            // Try to get case count from discovery service
            if (_discoveryService?.CurrentDiscovery?.TestSuites != null)
            {
                var suite = _discoveryService.CurrentDiscovery.TestSuites.Values
                    .FirstOrDefault(s => $"{s.Manifest.Id}@{s.Manifest.Version}" == SuiteIdentity);
                if (suite != null)
                    return suite.Manifest.TestCases?.Count ?? 0;
            }
            
            return 0;
        }
    }
    
    public SuiteReferenceViewModel(IDiscoveryService? discoveryService = null)
    {
        _discoveryService = discoveryService;
    }
    
    partial void OnSuiteIdentityChanged(string value)
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(CaseCount));
    }
}
