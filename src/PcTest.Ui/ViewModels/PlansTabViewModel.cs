using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTest.Contracts.Manifests;
using PcTest.Ui.Services;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// ViewModel for the Plans tab.
/// </summary>
public partial class PlansTabViewModel : ViewModelBase
{
    private readonly IPlanRepository _planRepository;
    private readonly ISuiteRepository _suiteRepository;
    private readonly IDiscoveryService _discoveryService;
    private readonly IFileDialogService _fileDialogService;
    private readonly INavigationService _navigationService;
    private bool _isChangingSelection;

    [ObservableProperty]
    private ObservableCollection<PlanListItemViewModel> _plans = new();

    [ObservableProperty]
    private PlanListItemViewModel? _selectedPlan;

    [ObservableProperty]
    private PlanEditorViewModel? _editor;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isEditorVisible;

    public PlansTabViewModel(
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
    }

    partial void OnSelectedPlanChanged(PlanListItemViewModel? oldValue, PlanListItemViewModel? newValue)
    {
        // Prevent recursive calls when reverting selection
        if (_isChangingSelection) return;

        // Check for unsaved changes before switching
        if (Editor?.IsDirty == true)
        {
            var result = _fileDialogService.ShowYesNoCancel(
                "Unsaved Changes",
                "You have unsaved changes to the current plan. Do you want to save before switching?");

            if (result is null) // Cancel - revert selection
            {
                // Prevent infinite loop by temporarily removing the handler
                _isChangingSelection = true;
                SelectedPlan = oldValue;
                _isChangingSelection = false;
                return;
            }
            else if (result == true) // Yes - save
            {
                _ = Editor.SaveAsync();
            }
            else // No - discard
            {
                Editor.Discard();
            }
        }

        if (newValue is not null)
        {
            LoadPlanForEditing(newValue);
        }
        else
        {
            IsEditorVisible = false;
            Editor = null;
        }
    }

    private List<PlanListItemViewModel> _allPlans = new();

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Plans.Clear();
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allPlans
            : _allPlans.Where(p =>
                p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                p.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var p in filtered)
        {
            Plans.Add(p);
        }
        
        // Select first item by default if available and nothing is selected
        if (Plans.Count > 0 && SelectedPlan is null)
        {
            SelectedPlan = Plans[0];
        }
    }

    public async Task LoadAsync()
    {
        SetBusy(true, "Loading plans...");

        try
        {
            var plans = await _planRepository.GetAllAsync();
            _allPlans.Clear();

            foreach (var plan in plans.OrderBy(p => p.Manifest.Name))
            {
                _allPlans.Add(new PlanListItemViewModel
                {
                    Id = plan.Manifest.Id,
                    Name = plan.Manifest.Name,
                    Version = plan.Manifest.Version,
                    Description = plan.Manifest.Description,
                    Tags = plan.Manifest.Tags?.ToList() ?? new(),
                    SuiteCount = plan.Manifest.Suites.Count,
                    FolderPath = plan.FolderPath,
                    ManifestPath = plan.ManifestPath
                });
            }

            ApplyFilter();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void LoadPlanForEditing(PlanListItemViewModel item)
    {
        var planInfo = await _planRepository.GetByIdentityAsync(item.Identity);
        if (planInfo is null) return;

        Editor = new PlanEditorViewModel(
            _planRepository,
            _suiteRepository,
            _discoveryService,
            _fileDialogService,
            _navigationService);

        await Editor.LoadAsync(planInfo);
        Editor.Saved += OnEditorSaved;
        IsEditorVisible = true;
    }

    private async void OnEditorSaved(object? sender, EventArgs e)
    {
        // Remember current selection
        var currentIdentity = SelectedPlan?.Identity;
        
        await LoadAsync();
        
        // Restore selection after reload
        if (currentIdentity is not null)
        {
            SelectedPlan = Plans.FirstOrDefault(p => p.Identity == currentIdentity);
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        var manifest = new TestPlanManifest
        {
            SchemaVersion = "1.5.0",
            Id = $"NewPlan_{DateTime.Now:yyyyMMddHHmmss}",
            Name = "New Plan",
            Version = "1.0.0",
            Description = "New test plan"
        };

        var planInfo = new PlanInfo
        {
            Manifest = manifest
        };

        Editor = new PlanEditorViewModel(
            _planRepository,
            _suiteRepository,
            _discoveryService,
            _fileDialogService,
            _navigationService);

        await Editor.LoadAsync(planInfo, isNew: true);
        Editor.IsEditing = true;  // Enter edit mode immediately for new plans
        Editor.Saved += OnEditorSaved;
        IsEditorVisible = true;
    }

    [RelayCommand]
    private async Task DuplicateAsync()
    {
        if (SelectedPlan is null) return;

        var source = await _planRepository.GetByIdentityAsync(SelectedPlan.Identity);
        if (source is null) return;

        var manifest = JsonSerializer.Deserialize<TestPlanManifest>(
            JsonSerializer.Serialize(source.Manifest));

        if (manifest is null) return;

        manifest.Id = $"{manifest.Id}_Copy";
        manifest.Name = $"{manifest.Name} (Copy)";

        var planInfo = new PlanInfo { Manifest = manifest };

        Editor = new PlanEditorViewModel(
            _planRepository,
            _suiteRepository,
            _discoveryService,
            _fileDialogService,
            _navigationService);

        await Editor.LoadAsync(planInfo, isNew: true);
        Editor.IsEditing = true;  // Enter edit mode immediately for duplicated plans
        Editor.Saved += OnEditorSaved;
        IsEditorVisible = true;
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedPlan is null) return;

        var confirmed = _fileDialogService.ShowConfirmation(
            "Delete Plan",
            $"Are you sure you want to delete '{SelectedPlan.Name}'?");

        if (!confirmed) return;

        await _planRepository.DeleteAsync(SelectedPlan.Identity);
        await LoadAsync();
        IsEditorVisible = false;
        Editor = null;
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var filePath = _fileDialogService.ShowOpenFileDialog(
            "Import Plan",
            "Plan Files (*.plan.json)|*.plan.json|JSON Files (*.json)|*.json|All Files (*.*)|*.*");

        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            await _planRepository.ImportAsync(filePath);
            await LoadAsync();
            _fileDialogService.ShowInfo("Import Successful", "Plan imported successfully.");
        }
        catch (Exception ex)
        {
            _fileDialogService.ShowError("Import Failed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (SelectedPlan is null) return;

        if (Editor?.IsDirty == true)
        {
            var result = _fileDialogService.ShowYesNoCancel(
                "Unsaved Changes",
                "You have unsaved changes. Save before exporting?");

            if (result is null) return;
            if (result == true)
            {
                await Editor.SaveAsync();
            }
        }

        var defaultName = $"{SelectedPlan.Id}.plan.json";
        var filePath = _fileDialogService.ShowSaveFileDialog(
            "Export Plan",
            "Plan Files (*.plan.json)|*.plan.json|JSON Files (*.json)|*.json",
            defaultName);

        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            await _planRepository.ExportAsync(SelectedPlan.Identity, filePath);
            _fileDialogService.ShowInfo("Export Successful", $"Plan exported to {filePath}");
        }
        catch (Exception ex)
        {
            _fileDialogService.ShowError("Export Failed", ex.Message);
        }
    }
}

/// <summary>
/// ViewModel for a plan list item.
/// </summary>
public partial class PlanListItemViewModel : ViewModelBase
{
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private List<string> _tags = new();
    [ObservableProperty] private int _suiteCount;
    [ObservableProperty] private string _folderPath = string.Empty;
    [ObservableProperty] private string _manifestPath = string.Empty;

    public string Identity => $"{Id}@{Version}";
    public string TagsDisplay => string.Join(", ", Tags);
}
