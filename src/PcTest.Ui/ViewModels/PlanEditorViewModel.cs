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

    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _tagsText = string.Empty;

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
    partial void OnTagsTextChanged(string value) => MarkDirty();

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
            var refVm = new SuiteReferenceViewModel { SuiteIdentity = suiteRef };
            refVm.PropertyChanged += (s, e) => MarkDirty();
            SuiteReferences.Add(refVm);
        }

        // Load environment variables
        EnvironmentEditor.LoadFromDictionary(m.Environment?.Env);

        // Load available suites
        await LoadAvailableSuitesAsync();

        ClearDirty();
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
                Version = suite.Manifest.Version
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
            var refVm = new SuiteReferenceViewModel
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
        Saved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task SavePlanAsync()
    {
        await SaveAsync();
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
    private void RunPlan()
    {
        if (IsDirty)
        {
            _fileDialogService.ShowWarning("Unsaved Changes", "Please save the plan before running.");
            return;
        }

        var identity = $"{Id}@{Version}";
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
    [ObservableProperty] private string _suiteIdentity = string.Empty;

    public string DisplayName => SuiteIdentity;
}
