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
    /// Event raised before navigation to allow cancellation.
    /// </summary>
    event EventHandler<NavigatingEventArgs>? Navigating;
    
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

/// <summary>
/// Event args for navigating (before navigation).
/// </summary>
public sealed class NavigatingEventArgs : EventArgs
{
    public string FromPage { get; set; } = string.Empty;
    public string ToPage { get; set; } = string.Empty;
    public object? Parameter { get; set; }
    public bool Cancel { get; set; }
}
