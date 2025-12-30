using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// ViewModel for the test suite picker dialog.
/// </summary>
public partial class SuitePickerViewModel : ViewModelBase
{
    private readonly List<SelectableSuiteViewModel> _allSuites = new();

    [ObservableProperty]
    private ObservableCollection<SelectableSuiteViewModel> _filteredSuites = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private bool _hasSelection;

    public SuitePickerViewModel()
    {
    }

    /// <summary>
    /// Loads available test suites from the suite list.
    /// </summary>
    public void LoadSuites(IEnumerable<SuiteListItemViewModel> suites, IEnumerable<string>? excludeIdentities = null)
    {
        var excludeSet = excludeIdentities?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();
        
        _allSuites.Clear();
        
        foreach (var suite in suites.OrderBy(s => s.Name))
        {
            // Skip suites that are already in the plan
            if (excludeSet.Contains(suite.Identity))
                continue;
                
            var vm = new SelectableSuiteViewModel
            {
                Id = suite.Id,
                Name = suite.Name,
                Version = suite.Version,
                Description = suite.Description,
                NodeCount = suite.NodeCount
            };
            
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SelectableSuiteViewModel.IsSelected))
                {
                    UpdateSelectionCount();
                }
            };
            
            _allSuites.Add(vm);
        }
        
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredSuites.Clear();
        
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allSuites
            : _allSuites.Where(suite =>
                suite.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                suite.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                (suite.Description?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
        
        foreach (var suite in filtered)
        {
            FilteredSuites.Add(suite);
        }
    }

    private void UpdateSelectionCount()
    {
        SelectedCount = _allSuites.Count(s => s.IsSelected);
        HasSelection = SelectedCount > 0;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var suite in _allSuites)
        {
            suite.IsSelected = false;
        }
        UpdateSelectionCount();
    }

    /// <summary>
    /// Gets the list of selected test suites.
    /// </summary>
    public IReadOnlyList<SelectableSuiteViewModel> GetSelectedSuites()
    {
        return _allSuites.Where(s => s.IsSelected).ToList();
    }
}

/// <summary>
/// ViewModel for a selectable test suite in the picker.
/// </summary>
public partial class SelectableSuiteViewModel : ViewModelBase
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private int _nodeCount;
    
    /// <summary>
    /// The suite identity (id@version).
    /// </summary>
    public string Identity => $"{Id}@{Version}";
}
