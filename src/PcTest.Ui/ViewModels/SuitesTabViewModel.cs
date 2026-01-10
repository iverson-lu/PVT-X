using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Engine;
using PcTest.Ui.Services;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// ViewModel for the Suites tab.
/// </summary>
public partial class SuitesTabViewModel : ViewModelBase
{
    private readonly ISuiteRepository _suiteRepository;
    private readonly IDiscoveryService _discoveryService;
    private readonly IFileDialogService _fileDialogService;
    private readonly INavigationService _navigationService;
    private bool _isChangingSelection;

    [ObservableProperty]
    private ObservableCollection<SuiteListItemViewModel> _suites = new();

    [ObservableProperty]
    private SuiteListItemViewModel? _selectedSuite;

    [ObservableProperty]
    private SuiteEditorViewModel? _editor;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isEditorVisible;

    public SuitesTabViewModel(
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

    partial void OnSelectedSuiteChanged(SuiteListItemViewModel? oldValue, SuiteListItemViewModel? newValue)
    {
        // Prevent recursive calls when reverting selection
        if (_isChangingSelection) return;

        // Check for unsaved changes before switching
        if (Editor?.IsDirty == true)
        {
            var result = _fileDialogService.ShowYesNoCancel(
                "Unsaved Changes",
                "You have unsaved changes to the current suite. Do you want to save before switching?");

            if (result is null) // Cancel - revert selection
            {
                // Prevent infinite loop by temporarily removing the handler
                _isChangingSelection = true;
                SelectedSuite = oldValue;
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
            LoadSuiteForEditing(newValue);
        }
        else
        {
            IsEditorVisible = false;
            Editor = null;
        }
    }

    private List<SuiteListItemViewModel> _allSuites = new();

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Suites.Clear();
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allSuites
            : _allSuites.Where(s =>
                s.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                s.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var s in filtered)
        {
            Suites.Add(s);
        }
        
        // Select first item by default if available and nothing is selected
        if (Suites.Count > 0 && SelectedSuite is null)
        {
            SelectedSuite = Suites[0];
        }
    }

    public async Task LoadAsync()
    {
        SetBusy(true, "Loading suites...");
        
        try
        {
            var discovery = _discoveryService.CurrentDiscovery ?? await _discoveryService.DiscoverAsync();
            var suites = await _suiteRepository.GetAllAsync();
            _allSuites.Clear();
            
            foreach (var suite in suites.OrderBy(s => s.Manifest.Name))
            {
                _allSuites.Add(new SuiteListItemViewModel
                {
                    Id = suite.Manifest.Id,
                    Name = suite.Manifest.Name,
                    Version = suite.Manifest.Version,
                    Description = suite.Manifest.Description,
                    Tags = suite.Manifest.Tags?.ToList() ?? new(),
                    NodeCount = suite.Manifest.TestCases.Count,
                    FolderPath = suite.FolderPath,
                    ManifestPath = suite.ManifestPath,
                    Privilege = PrivilegeChecker.GetSuitePrivilege(suite.Manifest, discovery)
                });
            }
            
            ApplyFilter();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void LoadSuiteForEditing(SuiteListItemViewModel item)
    {
        var suiteInfo = await _suiteRepository.GetByIdentityAsync(item.Identity);
        if (suiteInfo is null) return;

        Editor = new SuiteEditorViewModel(
            _suiteRepository,
            _discoveryService,
            _fileDialogService,
            _navigationService);
        
        await Editor.LoadAsync(suiteInfo);
        Editor.Saved += OnEditorSaved;
        IsEditorVisible = true;
    }

    private async void OnEditorSaved(object? sender, EventArgs e)
    {
        // Store the current selection identity before reloading
        var currentIdentity = SelectedSuite?.Identity;
        
        await LoadAsync();
        
        // Restore selection after reload
        if (!string.IsNullOrEmpty(currentIdentity))
        {
            var matchingSuite = Suites.FirstOrDefault(s => s.Identity == currentIdentity);
            if (matchingSuite is not null)
            {
                SelectedSuite = matchingSuite;
            }
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        // Create new suite with defaults
        var manifest = new TestSuiteManifest
        {
            SchemaVersion = "1.5.0",
            Id = "suite.<domain>.<feature>.<action>",
            Name = "New Suite",
            Version = "1.0.0",
            Description = "New test suite"
        };

        var suiteInfo = new SuiteInfo
        {
            Manifest = manifest
        };

        Editor = new SuiteEditorViewModel(
            _suiteRepository,
            _discoveryService,
            _fileDialogService,
            _navigationService);
        
        await Editor.LoadAsync(suiteInfo, isNew: true);
        Editor.IsEditing = true;  // Enter edit mode immediately for new suites
        Editor.Saved += OnEditorSaved;
        IsEditorVisible = true;
    }

    [RelayCommand]
    private async Task DuplicateAsync()
    {
        if (SelectedSuite is null) return;

        var source = await _suiteRepository.GetByIdentityAsync(SelectedSuite.Identity);
        if (source is null) return;

        var manifest = JsonSerializer.Deserialize<TestSuiteManifest>(
            JsonSerializer.Serialize(source.Manifest));
        
        if (manifest is null) return;

        manifest.Id = $"{manifest.Id}_Copy";
        manifest.Name = $"{manifest.Name} (Copy)";

        var suiteInfo = new SuiteInfo { Manifest = manifest };

        Editor = new SuiteEditorViewModel(
            _suiteRepository,
            _discoveryService,
            _fileDialogService,
            _navigationService);
        
        await Editor.LoadAsync(suiteInfo, isNew: true);
        Editor.IsEditing = true;  // Enter edit mode immediately for duplicated suites
        Editor.Saved += OnEditorSaved;
        IsEditorVisible = true;
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedSuite is null) return;

        var confirmed = _fileDialogService.ShowConfirmation(
            "Delete Suite",
            $"Are you sure you want to delete '{SelectedSuite.Name}'?");

        if (!confirmed) return;

        await _suiteRepository.DeleteAsync(SelectedSuite.Identity);
        await LoadAsync();
        IsEditorVisible = false;
        Editor = null;
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var filePath = _fileDialogService.ShowOpenFileDialog(
            "Import Suite",
            "Suite Files (*.suite.json)|*.suite.json|JSON Files (*.json)|*.json|All Files (*.*)|*.*");

        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            await _suiteRepository.ImportAsync(filePath);
            await LoadAsync();
            _fileDialogService.ShowInfo("Import Successful", "Suite imported successfully.");
        }
        catch (Exception ex)
        {
            _fileDialogService.ShowError("Import Failed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (SelectedSuite is null) return;

        // Check if editor has unsaved changes
        if (Editor?.IsDirty == true)
        {
            var result = _fileDialogService.ShowYesNoCancel(
                "Unsaved Changes",
                "You have unsaved changes. Save before exporting?");

            if (result is null) return; // Cancel
            if (result == true)
            {
                await Editor.SaveAsync();
            }
        }

        var defaultName = $"{SelectedSuite.Id}.suite.json";
        var filePath = _fileDialogService.ShowSaveFileDialog(
            "Export Suite",
            "Suite Files (*.suite.json)|*.suite.json|JSON Files (*.json)|*.json",
            defaultName);

        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            await _suiteRepository.ExportAsync(SelectedSuite.Identity, filePath);
            _fileDialogService.ShowInfo("Export Successful", $"Suite exported to {filePath}");
        }
        catch (Exception ex)
        {
            _fileDialogService.ShowError("Export Failed", ex.Message);
        }
    }
}

/// <summary>
/// ViewModel for a suite list item.
/// </summary>
public partial class SuiteListItemViewModel : ViewModelBase
{
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private List<string> _tags = new();
    [ObservableProperty] private int _nodeCount;
    [ObservableProperty] private string _folderPath = string.Empty;
    [ObservableProperty] private string _manifestPath = string.Empty;
    [ObservableProperty] private Privilege _privilege = Privilege.User;

    public string Identity => $"{Id}@{Version}";
    public string TagsDisplay => string.Join(", ", Tags);
}
