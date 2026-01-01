using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcTest.Ui.Services;

namespace PcTest.Ui.ViewModels;

/// <summary>
/// ViewModel for the Settings page.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IDiscoveryService _discoveryService;
    private readonly IThemeManager _themeManager;

    [ObservableProperty] private string _workspaceRoot = string.Empty;
    [ObservableProperty] private string _testCasesRoot = string.Empty;
    [ObservableProperty] private string _testSuitesRoot = string.Empty;
    [ObservableProperty] private string _testPlansRoot = string.Empty;
    [ObservableProperty] private string _runsRoot = string.Empty;
    [ObservableProperty] private bool _autoDiscoverOnStartup = true;
    [ObservableProperty] private int _defaultTimeoutSec = 300;
    [ObservableProperty] private int _runRetentionDays = 30;
    [ObservableProperty] private string _theme = "Light";
    [ObservableProperty] private double _fontScale = 1.0;
    [ObservableProperty] private string _defaultLandingPage = "Plan";
    [ObservableProperty] private bool _showDebugOutput = false;

    [ObservableProperty] private bool _hasChanges;

    public SettingsViewModel(
        ISettingsService settingsService,
        IFileDialogService fileDialogService,
        IDiscoveryService discoveryService,
        IThemeManager themeManager)
    {
        _settingsService = settingsService;
        _fileDialogService = fileDialogService;
        _discoveryService = discoveryService;
        _themeManager = themeManager;
    }

    // Track changes
    partial void OnWorkspaceRootChanged(string value) => HasChanges = true;
    partial void OnTestCasesRootChanged(string value) => HasChanges = true;
    partial void OnTestSuitesRootChanged(string value) => HasChanges = true;
    partial void OnTestPlansRootChanged(string value) => HasChanges = true;
    partial void OnRunsRootChanged(string value) => HasChanges = true;
    partial void OnAutoDiscoverOnStartupChanged(bool value) => HasChanges = true;
    partial void OnDefaultTimeoutSecChanged(int value) => HasChanges = true;
    partial void OnRunRetentionDaysChanged(int value) => HasChanges = true;
    partial void OnThemeChanged(string value)
    {
        // Apply theme immediately and auto-save
        _themeManager.ApplyTheme(value);
        
        var settings = _settingsService.CurrentSettings;
        settings.Theme = value;
        _ = _settingsService.SaveAsync(); // Fire and forget async save
        
        // Don't set HasChanges since we auto-saved the theme
    }
    partial void OnFontScaleChanged(double value) => HasChanges = true;
    partial void OnDefaultLandingPageChanged(string value) => HasChanges = true;
    partial void OnShowDebugOutputChanged(bool value) => HasChanges = true;

    public void Load()
    {
        var settings = _settingsService.CurrentSettings;
        
        WorkspaceRoot = settings.WorkspaceRoot;
        TestCasesRoot = settings.TestCasesRoot;
        TestSuitesRoot = settings.TestSuitesRoot;
        TestPlansRoot = settings.TestPlansRoot;
        RunsRoot = settings.RunsRoot;
        AutoDiscoverOnStartup = settings.AutoDiscoverOnStartup;
        DefaultTimeoutSec = settings.DefaultTimeoutSec;
        RunRetentionDays = settings.RunRetentionDays;
        Theme = settings.Theme;
        FontScale = settings.FontScale;
        DefaultLandingPage = settings.DefaultLandingPage;
        ShowDebugOutput = settings.ShowDebugOutput;

        HasChanges = false;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = _settingsService.CurrentSettings;
        
        settings.WorkspaceRoot = WorkspaceRoot;
        settings.TestCasesRoot = TestCasesRoot;
        settings.TestSuitesRoot = TestSuitesRoot;
        settings.TestPlansRoot = TestPlansRoot;
        settings.RunsRoot = RunsRoot;
        settings.AutoDiscoverOnStartup = AutoDiscoverOnStartup;
        settings.DefaultTimeoutSec = DefaultTimeoutSec;
        settings.RunRetentionDays = RunRetentionDays;
        settings.Theme = Theme;
        settings.FontScale = FontScale;
        settings.DefaultLandingPage = DefaultLandingPage;
        settings.ShowDebugOutput = ShowDebugOutput;

        await _settingsService.SaveAsync();
        
        // Apply theme immediately
        App.ApplyTheme(Theme);

        HasChanges = false;
        _fileDialogService.ShowInfo("Settings Saved", "Settings have been saved successfully.");
    }

    [RelayCommand]
    private void Discard()
    {
        Load();
    }

    [RelayCommand]
    private void BrowseWorkspaceRoot()
    {
        var folder = _fileDialogService.ShowFolderBrowserDialog("Select Workspace Root", WorkspaceRoot);
        if (!string.IsNullOrEmpty(folder))
        {
            WorkspaceRoot = folder;
        }
    }

    [RelayCommand]
    private void BrowseTestCasesRoot()
    {
        var folder = _fileDialogService.ShowFolderBrowserDialog("Select Test Cases Root", 
            Path.IsPathRooted(TestCasesRoot) ? TestCasesRoot : Path.Combine(WorkspaceRoot, TestCasesRoot));
        if (!string.IsNullOrEmpty(folder))
        {
            TestCasesRoot = folder;
        }
    }

    [RelayCommand]
    private void BrowseTestSuitesRoot()
    {
        var folder = _fileDialogService.ShowFolderBrowserDialog("Select Test Suites Root",
            Path.IsPathRooted(TestSuitesRoot) ? TestSuitesRoot : Path.Combine(WorkspaceRoot, TestSuitesRoot));
        if (!string.IsNullOrEmpty(folder))
        {
            TestSuitesRoot = folder;
        }
    }

    [RelayCommand]
    private void BrowseTestPlansRoot()
    {
        var folder = _fileDialogService.ShowFolderBrowserDialog("Select Test Plans Root",
            Path.IsPathRooted(TestPlansRoot) ? TestPlansRoot : Path.Combine(WorkspaceRoot, TestPlansRoot));
        if (!string.IsNullOrEmpty(folder))
        {
            TestPlansRoot = folder;
        }
    }

    [RelayCommand]
    private void BrowseRunsRoot()
    {
        var folder = _fileDialogService.ShowFolderBrowserDialog("Select Runs Root",
            Path.IsPathRooted(RunsRoot) ? RunsRoot : Path.Combine(WorkspaceRoot, RunsRoot));
        if (!string.IsNullOrEmpty(folder))
        {
            RunsRoot = folder;
        }
    }

    [RelayCommand]
    private async Task ImportSettingsAsync()
    {
        var filePath = _fileDialogService.ShowOpenFileDialog(
            "Import Settings",
            "Settings Files (*.json)|*.json|All Files (*.*)|*.*");

        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            await _settingsService.ImportAsync(filePath);
            Load();
            _fileDialogService.ShowInfo("Import Successful", "Settings imported successfully.");
        }
        catch (Exception ex)
        {
            _fileDialogService.ShowError("Import Failed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ExportSettingsAsync()
    {
        var filePath = _fileDialogService.ShowSaveFileDialog(
            "Export Settings",
            "Settings Files (*.json)|*.json",
            "settings.json");

        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            await _settingsService.ExportAsync(filePath);
            _fileDialogService.ShowInfo("Export Successful", $"Settings exported to {filePath}");
        }
        catch (Exception ex)
        {
            _fileDialogService.ShowError("Export Failed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task DiscoverNowAsync()
    {
        SetBusy(true, "Discovering assets...");
        try
        {
            await _discoveryService.DiscoverAsync();
            _fileDialogService.ShowInfo("Discovery Complete", "Assets have been discovered.");
        }
        catch (Exception ex)
        {
            _fileDialogService.ShowError("Discovery Failed", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    public IEnumerable<string> ThemeOptions => new[] { "Light", "Dark" };
    public IEnumerable<string> LandingPageOptions => new[] { "Plan", "Run", "History", "Settings" };
}

