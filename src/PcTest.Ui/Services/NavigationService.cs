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
    public event EventHandler<NavigatingEventArgs>? Navigating;

    public string CurrentPage => _currentPage;
    public object? CurrentParameter => _currentParameter;

    public void SetFrame(Frame frame)
    {
        _frame = frame;
    }

    public void NavigateTo(string pageName, object? parameter = null)
    {
        if (_frame is null) return;
        
        // Raise Navigating event to allow cancellation
        var navigatingArgs = new NavigatingEventArgs
        {
            FromPage = _currentPage,
            ToPage = pageName,
            Parameter = parameter,
            Cancel = false
        };
        Navigating?.Invoke(this, navigatingArgs);
        
        // If navigation was cancelled, return
        if (navigatingArgs.Cancel)
        {
            return;
        }
        
        _currentPage = pageName;
        _currentParameter = parameter;
        
        Page page = pageName switch
        {
            "Plan" => App.GetService<PlanPage>(),
            "Run" => App.GetService<RunPage>(),
            "History" => App.GetService<HistoryPage>(),
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
    /// <summary>
    /// Source page where the execution was triggered from (e.g., "Plan")
    /// </summary>
    public string? SourcePage { get; set; }
    /// <summary>
    /// Source tab index in the Plan page (0=Cases, 1=Suites, 2=Plans)
    /// </summary>
    public int? SourceTabIndex { get; set; }
}

/// <summary>
/// Parameter for navigation to Plan page with item selection.
/// </summary>
public sealed class PlanNavigationParameter
{
    /// <summary>
    /// Tab index (0=Cases, 1=Suites, 2=Plans)
    /// </summary>
    public int TabIndex { get; set; }
    /// <summary>
    /// Target identity to select (e.g., "test-id@1.0.0")
    /// </summary>
    public string? TargetIdentity { get; set; }
}
