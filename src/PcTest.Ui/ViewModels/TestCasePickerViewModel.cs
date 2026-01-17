using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTest.Engine.Discovery;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// ViewModel for the test case picker dialog.
/// </summary>
public partial class TestCasePickerViewModel : ViewModelBase
{
    private readonly List<SelectableTestCaseViewModel> _allTestCases = new();

    [ObservableProperty]
    private ObservableCollection<SelectableTestCaseViewModel> _filteredTestCases = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _showLatestVersionOnly = true;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private bool _hasSelection;

    public TestCasePickerViewModel()
    {
    }

    /// <summary>
    /// Loads available test cases from the discovery result.
    /// </summary>
    public void LoadTestCases(DiscoveryResult discovery, IEnumerable<string>? excludeRefs = null)
    {
        var excludeSet = excludeRefs?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();
        
        _allTestCases.Clear();
        
        foreach (var tc in discovery.TestCases.Values.OrderBy(c => c.Manifest.Name))
        {
            // Skip test cases that are already in the suite
            if (excludeSet.Contains(tc.Manifest.Id))
                continue;
                
            var vm = new SelectableTestCaseViewModel
            {
                Id = tc.Manifest.Id,
                Name = tc.Manifest.Name,
                Version = tc.Manifest.Version,
                Description = tc.Manifest.Description,
                Category = tc.Manifest.Category,
                FolderName = Path.GetFileName(tc.FolderPath),
                Privilege = tc.Manifest.Privilege
            };
            
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SelectableTestCaseViewModel.IsSelected))
                {
                    UpdateSelectionCount();
                }
            };
            
            _allTestCases.Add(vm);
        }
        
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnShowLatestVersionOnlyChanged(bool value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredTestCases.Clear();
        
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allTestCases
            : _allTestCases.Where(tc =>
                tc.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                tc.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                (tc.Description?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (tc.Category?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
        
        // Apply version filtering if enabled
        if (ShowLatestVersionOnly)
        {
            filtered = filtered
                .GroupBy(tc => tc.Id)
                .Select(g => g.OrderByDescending(tc => tc.Version).First());
        }
        
        foreach (var tc in filtered)
        {
            FilteredTestCases.Add(tc);
        }
    }

    private void UpdateSelectionCount()
    {
        SelectedCount = _allTestCases.Count(tc => tc.IsSelected);
        HasSelection = SelectedCount > 0;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var tc in _allTestCases)
        {
            tc.IsSelected = false;
        }
        UpdateSelectionCount();
    }

    /// <summary>
    /// Gets the list of selected test cases.
    /// </summary>
    public IReadOnlyList<SelectableTestCaseViewModel> GetSelectedTestCases()
    {
        return _allTestCases.Where(tc => tc.IsSelected).ToList();
    }
}

/// <summary>
/// ViewModel for a selectable test case in the picker.
/// </summary>
public partial class SelectableTestCaseViewModel : ViewModelBase
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private string? _category;
    [ObservableProperty] private string _folderName = string.Empty;
    [ObservableProperty] private PcTest.Contracts.Privilege _privilege = PcTest.Contracts.Privilege.User;
    
    /// <summary>
    /// The reference path to use in the suite (folder name under TestCases root).
    /// </summary>
    public string Ref => FolderName;

    public bool IsAdminRequired => Privilege == PcTest.Contracts.Privilege.AdminRequired;
    public bool IsAdminPreferred => Privilege == PcTest.Contracts.Privilege.AdminPreferred;

    partial void OnPrivilegeChanged(PcTest.Contracts.Privilege value)
    {
        OnPropertyChanged(nameof(IsAdminRequired));
        OnPropertyChanged(nameof(IsAdminPreferred));
    }
}
