using System.Windows.Controls;

namespace PcTest.Ui.Services;

/// <summary>
/// Service for navigation between pages.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Navigates to the specified page.
    /// </summary>
    void NavigateTo(string pageName, object? parameter = null);
    
    /// <summary>
    /// Navigates to the Run page with a specific target.
    /// </summary>
    void NavigateToRun(string targetIdentity, PcTest.Contracts.RunType runType);
    
    /// <summary>
    /// Navigates to the Logs & Results page with a specific run.
    /// </summary>
    void NavigateToLogsResults(string? runId = null);
    
    /// <summary>
    /// Gets the current page name.
    /// </summary>
    string CurrentPage { get; }
    
    /// <summary>
    /// Gets the navigation parameter.
    /// </summary>
    object? CurrentParameter { get; }
    
    /// <summary>
    /// Event raised when navigation occurs.
    /// </summary>
    event EventHandler<NavigationEventArgs>? Navigated;
    
    /// <summary>
    /// Sets the frame for navigation.
    /// </summary>
    void SetFrame(Frame frame);
}

/// <summary>
/// Event args for navigation.
/// </summary>
public sealed class NavigationEventArgs : EventArgs
{
    public string PageName { get; set; } = string.Empty;
    public object? Parameter { get; set; }
}
