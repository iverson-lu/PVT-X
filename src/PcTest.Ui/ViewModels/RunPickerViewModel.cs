using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTest.Contracts;
using PcTest.Ui.Services;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// ViewModel for the run picker.
/// </summary>
public partial class RunPickerViewModel : ViewModelBase
{
    private readonly IRunRepository _runRepository;

    public event EventHandler<string>? RunSelected;

    [ObservableProperty]
    private ObservableCollection<RunIndexEntryViewModel> _recentRuns = new();

    [ObservableProperty]
    private RunIndexEntryViewModel? _selectedRun;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private RunStatus? _statusFilter;

    [ObservableProperty]
    private DateTime? _startTimeFrom;

    [ObservableProperty]
    private DateTime? _startTimeTo;

    private List<RunIndexEntryViewModel> _allRuns = new();

    public RunPickerViewModel(IRunRepository runRepository)
    {
        _runRepository = runRepository;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnStatusFilterChanged(RunStatus? value) => _ = LoadRecentRunsAsync();
    partial void OnStartTimeFromChanged(DateTime? value) => _ = LoadRecentRunsAsync();
    partial void OnStartTimeToChanged(DateTime? value) => _ = LoadRecentRunsAsync();

    private void ApplyFilter()
    {
        RecentRuns.Clear();
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allRuns
            : _allRuns.Where(r =>
                r.RunId.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                r.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                (r.TestId?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.SuiteId?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.PlanId?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));

        foreach (var r in filtered.Take(50))
        {
            RecentRuns.Add(r);
        }
    }

    public async Task LoadRecentRunsAsync()
    {
        SetBusy(true, "Loading recent runs...");

        try
        {
            var filter = new RunFilter
            {
                StartTimeFrom = StartTimeFrom,
                StartTimeTo = StartTimeTo,
                Status = StatusFilter,
                TopLevelOnly = true,
                MaxResults = 100
            };

            var runs = await _runRepository.GetRunsAsync(filter);
            _allRuns.Clear();

            foreach (var run in runs)
            {
                _allRuns.Add(new RunIndexEntryViewModel
                {
                    RunId = run.RunId,
                    RunType = run.RunType,
                    NodeId = run.NodeId,
                    TestId = run.TestId,
                    TestVersion = run.TestVersion,
                    SuiteId = run.SuiteId,
                    SuiteVersion = run.SuiteVersion,
                    PlanId = run.PlanId,
                    PlanVersion = run.PlanVersion,
                    StartTime = run.StartTime,
                    EndTime = run.EndTime,
                    Status = run.Status,
                    DisplayName = run.DisplayName,
                    Duration = run.Duration
                });
            }

            ApplyFilter();
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    private void SelectRun()
    {
        if (SelectedRun is not null)
        {
            RunSelected?.Invoke(this, SelectedRun.RunId);
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        StatusFilter = null;
        StartTimeFrom = null;
        StartTimeTo = null;
    }

    public IEnumerable<RunStatus?> StatusOptions => new RunStatus?[] { null }
        .Concat(Enum.GetValues<RunStatus>().Cast<RunStatus?>());
}
