using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Engine.Discovery;
using PcTest.Ui.Services;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// ViewModel for the Cases tab.
/// </summary>
public partial class CasesTabViewModel : ViewModelBase
{
    private readonly IDiscoveryService _discoveryService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IFileDialogService _fileDialogService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private ObservableCollection<TestCaseItemViewModel> _cases = new();

    [ObservableProperty]
    private TestCaseItemViewModel? _selectedCase;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _showLatestVersionOnly = true;

    [ObservableProperty]
    private bool _isDiscovering;

    public CasesTabViewModel(
        IDiscoveryService discoveryService,
        IFileSystemService fileSystemService,
        IFileDialogService fileDialogService,
        INavigationService navigationService)
    {
        _discoveryService = discoveryService;
        _fileSystemService = fileSystemService;
        _fileDialogService = fileDialogService;
        _navigationService = navigationService;
    }

    partial void OnSearchTextChanged(string value)
    {
        // Filter cases based on search text
        ApplyFilter();
    }

    partial void OnShowLatestVersionOnlyChanged(bool value)
    {
        // Re-apply filter when version filter changes
        ApplyFilter();
    }

    private List<TestCaseItemViewModel> _allCases = new();

    private void ApplyFilter()
    {
        Cases.Clear();
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allCases
            : _allCases.Where(c => 
                c.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                c.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                (c.Tags?.Any(t => t.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ?? false));
        
        // Apply version filtering if enabled
        if (ShowLatestVersionOnly)
        {
            filtered = filtered
                .GroupBy(c => c.Id)
                .Select(g => g.OrderByDescending(c => c.Version).First());
        }
        
        foreach (var c in filtered)
        {
            Cases.Add(c);
        }
        
        // Select first item by default if available and nothing is selected
        if (Cases.Count > 0 && SelectedCase is null)
        {
            SelectedCase = Cases[0];
        }
    }

    public async Task LoadAsync()
    {
        var discovery = _discoveryService.CurrentDiscovery;
        if (discovery is null)
        {
            await DiscoverAsync();
            return;
        }
        
        PopulateCases(discovery);
    }

    [RelayCommand]
    private async Task DiscoverAsync()
    {
        IsDiscovering = true;
        
        try
        {
            var discovery = await _discoveryService.DiscoverAsync();
            PopulateCases(discovery);
        }
        finally
        {
            IsDiscovering = false;
        }
    }

    private void PopulateCases(DiscoveryResult discovery)
    {
        _allCases.Clear();
        
        foreach (var tc in discovery.TestCases.Values.OrderBy(c => c.Manifest.Name))
        {
            var paramWrappers = tc.Manifest.Parameters?
                .Select(p => new ParameterViewModel(p))
                .ToList() ?? new();
            
            _allCases.Add(new TestCaseItemViewModel
            {
                Id = tc.Manifest.Id,
                Name = tc.Manifest.Name,
                Version = tc.Manifest.Version,
                Category = tc.Manifest.Category,
                Description = tc.Manifest.Description,
                Privilege = tc.Manifest.Privilege.ToString(),
                TimeoutSec = tc.Manifest.TimeoutSec,
                Tags = tc.Manifest.Tags?.ToList() ?? new(),
                ParameterWrappers = paramWrappers,
                FolderPath = tc.FolderPath,
                ManifestPath = tc.ManifestPath
            });
        }
        
        ApplyFilter();
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var filePath = _fileDialogService.ShowOpenFileDialog(
            "Import Test Case",
            "Zip Files (*.zip)|*.zip|All Files (*.*)|*.*");

        if (string.IsNullOrEmpty(filePath)) return;

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"PcTestCaseImport_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            ZipFile.ExtractToDirectory(filePath, tempDirectory, true);

            var manifestPaths = Directory.EnumerateFiles(tempDirectory, "test.manifest.json", SearchOption.AllDirectories)
                .ToList();

            if (manifestPaths.Count == 0)
            {
                _fileDialogService.ShowError("Import Failed", "No test.manifest.json was found in the zip file.");
                return;
            }

            if (manifestPaths.Count > 1)
            {
                _fileDialogService.ShowError("Import Failed", "Multiple test.manifest.json files were found in the zip file.");
                return;
            }

            var manifestPath = manifestPaths[0];
            var manifestJson = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonDefaults.Deserialize<TestCaseManifest>(manifestJson);

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Version))
            {
                _fileDialogService.ShowError("Import Failed", "The test case manifest is invalid.");
                return;
            }

            var discovery = _discoveryService.CurrentDiscovery ?? await _discoveryService.DiscoverAsync();
            if (string.IsNullOrWhiteSpace(discovery.ResolvedTestCaseRoot))
            {
                _fileDialogService.ShowError("Import Failed", "Test case root is not configured.");
                return;
            }

            var identityConflict = discovery.TestCases.ContainsKey(manifest.Identity);
            var nameConflict = discovery.TestCases.Values.Any(tc =>
                tc.Manifest.Name.Equals(manifest.Name, StringComparison.OrdinalIgnoreCase));

            if (identityConflict)
            {
                _fileDialogService.ShowError(
                    "Import Failed",
                    $"A test case with identity '{manifest.Identity}' already exists.");
                return;
            }

            if (nameConflict)
            {
                _fileDialogService.ShowError(
                    "Import Failed",
                    $"A test case named '{manifest.Name}' already exists.");
                return;
            }

            var sourceRoot = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrWhiteSpace(sourceRoot))
            {
                _fileDialogService.ShowError("Import Failed", "Unable to locate the test case folder in the zip file.");
                return;
            }

            Directory.CreateDirectory(discovery.ResolvedTestCaseRoot);
            var destinationRoot = Path.Combine(discovery.ResolvedTestCaseRoot, manifest.Id);

            if (Directory.Exists(destinationRoot))
            {
                _fileDialogService.ShowError(
                    "Import Failed",
                    $"A folder already exists at '{destinationRoot}'.");
                return;
            }

            CopyDirectory(sourceRoot, destinationRoot);

            await DiscoverAsync();
            _fileDialogService.ShowInfo("Import Successful", $"Test case '{manifest.Name}' imported successfully.");
        }
        catch (Exception ex)
        {
            _fileDialogService.ShowError("Import Failed", ex.Message);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destinationFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destinationFile);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            var destinationSubDir = Path.Combine(destinationDir, Path.GetFileName(directory));
            CopyDirectory(directory, destinationSubDir);
        }
    }

    [RelayCommand]
    private async Task Run()
    {
        if (SelectedCase is null) return;

        // Check privilege requirements before navigation
        var discovery = _discoveryService.CurrentDiscovery;
        if (discovery is null)
        {
            discovery = await _discoveryService.DiscoverAsync();
        }

        var (isValid, requiredPrivilege, message) = PcTest.Engine.PrivilegeChecker.ValidatePrivilege(
            PcTest.Contracts.RunType.TestCase, SelectedCase.Identity, discovery);

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

        // Build parameter overrides from edited values
        var overrides = new Dictionary<string, object?>();
        foreach (var param in SelectedCase.ParameterWrappers)
        {
            if (!string.IsNullOrEmpty(param.CurrentValue))
            {
                overrides[param.Name] = param.CurrentValue;
            }
        }

        var navParam = new RunNavigationParameter
        {
            TargetIdentity = SelectedCase.Identity,
            RunType = PcTest.Contracts.RunType.TestCase,
            ParameterOverrides = overrides.Count > 0 ? overrides : null,
            AutoStart = true,
            SourcePage = "Plan",
            SourceTabIndex = 0  // Cases tab is index 0
        };

        _navigationService.NavigateTo("Run", navParam);
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        if (SelectedCase is not null)
        {
            _fileSystemService.OpenInExplorer(SelectedCase.FolderPath);
        }
    }
}

/// <summary>
/// ViewModel for a single test case item.
/// </summary>
public partial class TestCaseItemViewModel : ViewModelBase
{
    [ObservableProperty] private bool _isEditing;
    
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string _category = string.Empty;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private string _privilege = "User";
    [ObservableProperty] private int? _timeoutSec;
    [ObservableProperty] private List<string> _tags = new();
    [ObservableProperty] private List<ParameterViewModel> _parameterWrappers = new();
    [ObservableProperty] private string _folderPath = string.Empty;
    [ObservableProperty] private string _manifestPath = string.Empty;

    // Store original values for cancel operation
    private string _originalId = string.Empty;
    private string _originalName = string.Empty;
    private string _originalVersion = string.Empty;
    private string _originalCategory = string.Empty;
    private string? _originalDescription;
    private string _originalPrivilege = "User";
    private int? _originalTimeoutSec;
    private List<string> _originalTags = new();

    public string Identity => $"{Id}@{Version}";
    public string TagsDisplay => string.Join(", ", Tags);
    public bool HasParameters => ParameterWrappers.Count > 0;
    public bool IsAdminRequired => Privilege == "AdminRequired";
    public bool IsAdminPreferred => Privilege == "AdminPreferred";
    
    // Expose parameters for binding - allows both get and set
    public List<ParameterViewModel> Parameters
    {
        get => ParameterWrappers;
        set => ParameterWrappers = value;
    }

    public void StoreOriginalValues()
    {
        _originalId = Id;
        _originalName = Name;
        _originalVersion = Version;
        _originalCategory = Category;
        _originalDescription = Description;
        _originalPrivilege = Privilege;
        _originalTimeoutSec = TimeoutSec;
        _originalTags = new List<string>(Tags);
    }

    [RelayCommand]
    private void Run()
    {
        // Placeholder - actual run logic is in CasesTabViewModel
    }

    [RelayCommand]
    private void Edit()
    {
        StoreOriginalValues();
        IsEditing = true;
    }

    [RelayCommand]
    private void Save()
    {
        // TODO: Implement save logic to persist changes to manifest file
        IsEditing = false;
        StoreOriginalValues(); // Update stored values after save
    }

    [RelayCommand]
    private void Validate()
    {
        // TODO: Implement validation logic
    }

    [RelayCommand]
    private void Cancel()
    {
        // Restore original values
        Id = _originalId;
        Name = _originalName;
        Version = _originalVersion;
        Category = _originalCategory;
        Description = _originalDescription;
        Privilege = _originalPrivilege;
        TimeoutSec = _originalTimeoutSec;
        Tags = new List<string>(_originalTags);
        IsEditing = false;
    }
}

/// <summary>
/// Wrapper for parameter definition with editable value.
/// </summary>
public partial class ParameterViewModel : ViewModelBase
{
    private readonly ParameterDefinition _definition;
    
    [ObservableProperty] private string _currentValue = string.Empty;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string? _errorMessage;
    
    public ParameterViewModel(ParameterDefinition definition)
    {
        _definition = definition;
        
        // Initialize with default value
        if (definition.Default.HasValue)
        {
            try
            {
                _currentValue = definition.Default.Value.ValueKind switch
                {
                    JsonValueKind.String => definition.Default.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => definition.Default.Value.ToString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => definition.Default.Value.ToString()
                };
            }
            catch
            {
                _currentValue = string.Empty;
            }
        }
    }
    
    partial void OnCurrentValueChanged(string value)
    {
        ValidateValue();
    }
    
    public string Name => _definition.Name;
    public string Type => _definition.Type;
    public bool Required => _definition.Required;
    public string? Help => _definition.Help;
    public string Default => CurrentValue;  // For binding in XAML
    
    // Enum-specific properties
    public bool IsEnum => Type == ParameterTypes.Enum;
    public List<string>? EnumValues => _definition.EnumValues;
    
    // MultiSelect (json + enumValues)
    // Priority: uiHint "multiselect" > uiHint "textarea" (force textbox) > default (enumValues present = multiselect)
    public bool IsMultiSelect
    {
        get
        {
            if (Type != ParameterTypes.Json || _definition.EnumValues == null || _definition.EnumValues.Count == 0)
                return false;

            // Explicit uiHint takes priority
            if (!string.IsNullOrEmpty(_definition.UiHint))
            {
                var hint = _definition.UiHint.ToLowerInvariant();
                if (hint.Contains("multiselect"))
                    return true;
                if (hint.Contains("textarea") || hint.Contains("textbox"))
                    return false;
            }

            // Default: json + enumValues = multiselect (backward compatible)
            return true;
        }
    }
    
    // Boolean-specific properties
    public bool IsBoolean => Type == ParameterTypes.Boolean;
    public bool UsePlainCheckBox => 
        IsBoolean && 
        _definition.UiHint != null && 
        _definition.UiHint.Contains("checkbox", StringComparison.OrdinalIgnoreCase);
    
    /// <summary>
    /// Validates the current value against parameter constraints.
    /// </summary>
    public void ValidateValue()
    {
        HasError = false;
        ErrorMessage = null;
        
        // Required validation
        if (Required && string.IsNullOrWhiteSpace(CurrentValue))
        {
            HasError = true;
            ErrorMessage = $"{Name} is required";
            return;
        }
        
        // Skip validation for empty optional parameters
        if (!Required && string.IsNullOrWhiteSpace(CurrentValue))
        {
            return;
        }
        
        // Enum validation
        if (IsEnum && EnumValues != null && EnumValues.Count > 0)
        {
            if (!EnumValues.Contains(CurrentValue))
            {
                HasError = true;
                ErrorMessage = $"Value must be one of: {string.Join(", ", EnumValues)}";
                return;
            }
        }
        
        // Boolean validation
        if (IsBoolean)
        {
            var lower = CurrentValue.ToLowerInvariant();
            if (lower != "true" && lower != "false" && lower != "1" && lower != "0")
            {
                HasError = true;
                ErrorMessage = "Value must be true/false or 1/0";
                return;
            }
        }
    }
}
