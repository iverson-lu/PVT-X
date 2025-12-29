using System.IO;
namespace PcTest.Ui.Services;

/// <summary>
/// Service for managing application settings.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Gets the current settings.
    /// </summary>
    AppSettings CurrentSettings { get; }
    
    /// <summary>
    /// Loads settings from disk.
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Saves settings to disk.
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Imports settings from a file.
    /// </summary>
    Task ImportAsync(string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Exports settings to a file.
    /// </summary>
    Task ExportAsync(string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Event raised when settings change.
    /// </summary>
    event EventHandler<AppSettings>? SettingsChanged;
}

/// <summary>
/// Application settings.
/// </summary>
public sealed class AppSettings
{
    // Workspace paths
    public string WorkspaceRoot { get; set; } = string.Empty;
    public string TestCasesRoot { get; set; } = "assets/TestCases";
    public string TestSuitesRoot { get; set; } = "assets/TestSuites";
    public string TestPlansRoot { get; set; } = "assets/TestPlans";
    public string RunsRoot { get; set; } = "Runs";
    
    // Discovery
    public bool AutoDiscoverOnStartup { get; set; } = true;
    
    // Run policies
    public int DefaultTimeoutSec { get; set; } = 300;
    public int RunRetentionDays { get; set; } = 30;
    
    // UI preferences
    public string Theme { get; set; } = "Light";
    public double FontScale { get; set; } = 1.0;
    public string DefaultLandingPage { get; set; } = "Plan";
    public bool ShowDebugOutput { get; set; } = false;
    
    // Resolved paths
    public string ResolvedTestCasesRoot => Path.IsPathRooted(TestCasesRoot) 
        ? TestCasesRoot 
        : Path.Combine(WorkspaceRoot, TestCasesRoot);
    
    public string ResolvedTestSuitesRoot => Path.IsPathRooted(TestSuitesRoot) 
        ? TestSuitesRoot 
        : Path.Combine(WorkspaceRoot, TestSuitesRoot);
    
    public string ResolvedTestPlansRoot => Path.IsPathRooted(TestPlansRoot) 
        ? TestPlansRoot 
        : Path.Combine(WorkspaceRoot, TestPlansRoot);
    
    public string ResolvedRunsRoot => Path.IsPathRooted(RunsRoot) 
        ? RunsRoot 
        : Path.Combine(WorkspaceRoot, RunsRoot);
}

