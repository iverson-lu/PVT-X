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
/// ViewModel for the Cases tab.
/// </summary>
public partial class CasesTabViewModel : ViewModelBase
{
    private readonly IDiscoveryService _discoveryService;
    private readonly IFileSystemService _fileSystemService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private ObservableCollection<TestCaseItemViewModel> _cases = new();

    [ObservableProperty]
    private TestCaseItemViewModel? _selectedCase;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isDiscovering;

    public CasesTabViewModel(IDiscoveryService discoveryService, IFileSystemService fileSystemService, INavigationService navigationService)
    {
        _discoveryService = discoveryService;
        _fileSystemService = fileSystemService;
        _navigationService = navigationService;
    }

    partial void OnSearchTextChanged(string value)
    {
        // Filter cases based on search text
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
    private void Run()
    {
        if (SelectedCase is null) return;

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

    public string Identity => $"{Id}@{Version}";
    public string TagsDisplay => string.Join(", ", Tags);
    public bool HasParameters => ParameterWrappers.Count > 0;
    
    // Expose parameters for binding - allows both get and set
    public List<ParameterViewModel> Parameters
    {
        get => ParameterWrappers;
        set => ParameterWrappers = value;
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
