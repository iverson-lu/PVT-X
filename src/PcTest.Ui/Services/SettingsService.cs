using System.IO;
using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Ui.Services;

/// <summary>
/// Service for managing application settings.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private AppSettings _currentSettings;

    public event EventHandler<AppSettings>? SettingsChanged;

    public AppSettings CurrentSettings => _currentSettings;

    public SettingsService()
    {
        // Default to app data folder
        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PcTestSystem");
        
        Directory.CreateDirectory(appDataFolder);
        _settingsPath = Path.Combine(appDataFolder, "settings.json");
        
        // Initialize with defaults and try to detect workspace
        _currentSettings = new AppSettings();
        DetectWorkspace();
    }

    private void DetectWorkspace()
    {
        // Try to detect workspace from current directory
        var currentDir = Directory.GetCurrentDirectory();
        
        // Look for typical project markers
        var markers = new[] { "pc-test-system.sln", "assets", "Runs" };
        
        foreach (var marker in markers)
        {
            if (File.Exists(Path.Combine(currentDir, marker)) || 
                Directory.Exists(Path.Combine(currentDir, marker)))
            {
                _currentSettings.WorkspaceRoot = currentDir;
                return;
            }
        }
        
        // Try parent directories
        var dir = new DirectoryInfo(currentDir);
        while (dir?.Parent != null)
        {
            dir = dir.Parent;
            foreach (var marker in markers)
            {
                if (File.Exists(Path.Combine(dir.FullName, marker)) || 
                    Directory.Exists(Path.Combine(dir.FullName, marker)))
                {
                    _currentSettings.WorkspaceRoot = dir.FullName;
                    return;
                }
            }
        }
        
        // Default to current directory
        _currentSettings.WorkspaceRoot = currentDir;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            // Use defaults with detected workspace
            return;
        }
        
        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath, cancellationToken);
            var loaded = JsonDefaults.Deserialize<AppSettings>(json);
            if (loaded is not null)
            {
                // Preserve workspace root if not set in loaded settings
                if (string.IsNullOrEmpty(loaded.WorkspaceRoot))
                {
                    loaded.WorkspaceRoot = _currentSettings.WorkspaceRoot;
                }
                _currentSettings = loaded;
            }
        }
        catch
        {
            // Use defaults on error
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var json = JsonDefaults.Serialize(_currentSettings);
        await File.WriteAllTextAsync(_settingsPath, json, cancellationToken);
        SettingsChanged?.Invoke(this, _currentSettings);
    }

    public async Task ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var imported = JsonDefaults.Deserialize<AppSettings>(json);
        if (imported is not null)
        {
            _currentSettings = imported;
            await SaveAsync(cancellationToken);
        }
    }

    public async Task ExportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var json = JsonDefaults.Serialize(_currentSettings);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }
}

