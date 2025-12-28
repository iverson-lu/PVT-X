using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    [ObservableProperty]
    private ObservableCollection<TestCaseItemViewModel> _cases = new();

    [ObservableProperty]
    private TestCaseItemViewModel? _selectedCase;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isDiscovering;

    public CasesTabViewModel(IDiscoveryService discoveryService, IFileSystemService fileSystemService)
    {
        _discoveryService = discoveryService;
        _fileSystemService = fileSystemService;
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
                Parameters = tc.Manifest.Parameters?.ToList() ?? new(),
                FolderPath = tc.FolderPath,
                ManifestPath = tc.ManifestPath
            });
        }
        
        ApplyFilter();
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
    [ObservableProperty] private List<ParameterDefinition> _parameters = new();
    [ObservableProperty] private string _folderPath = string.Empty;
    [ObservableProperty] private string _manifestPath = string.Empty;

    public string Identity => $"{Id}@{Version}";
    public string TagsDisplay => string.Join(", ", Tags);
    public bool HasParameters => Parameters.Count > 0;
}
