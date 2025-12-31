using System.Windows.Controls;
using PcTest.Contracts;
using PcTest.Ui.Views.Pages;

namespace PcTest.Ui.Services;

/// <summary>
/// Service for navigation between pages.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private Frame? _frame;
    private string _currentPage = string.Empty;
    private object? _currentParameter;

    public event EventHandler<NavigationEventArgs>? Navigated;

    public string CurrentPage => _currentPage;
    public object? CurrentParameter => _currentParameter;

    public void SetFrame(Frame frame)
    {
        _frame = frame;
    }

    public void NavigateTo(string pageName, object? parameter = null)
    {
        if (_frame is null) return;
        
        _currentPage = pageName;
        _currentParameter = parameter;
        
        Page page = pageName switch
        {
            "Plan" => App.GetService<PlanPage>(),
            "Run" => App.GetService<RunPage>(),
            "Runs" => App.GetService<RunsPage>(),
            "History" => App.GetService<HistoryPage>(),  // Backward compatibility
            "LogsResults" => App.GetService<LogsResultsPage>(),  // Backward compatibility
            "Settings" => App.GetService<SettingsPage>(),
            _ => App.GetService<PlanPage>()
        };
        
        _frame.Navigate(page);
        
        Navigated?.Invoke(this, new NavigationEventArgs
        {
            PageName = pageName,
            Parameter = parameter
        });
    }

    public void NavigateToRun(string targetIdentity, RunType runType)
    {
        NavigateTo("Run", new RunNavigationParameter
        {
            TargetIdentity = targetIdentity,
            RunType = runType
        });
    }

    public void NavigateToLogsResults(string? runId = null)
    {
        // Redirect to unified Runs page
        NavigateTo("Runs", runId);
    }
}

/// <summary>
/// Parameter for navigation to Run page.
/// </summary>
public sealed class RunNavigationParameter
{
    public string TargetIdentity { get; set; } = string.Empty;
    public RunType RunType { get; set; }
    public Dictionary<string, object?>? ParameterOverrides { get; set; }
    public bool AutoStart { get; set; }
}
