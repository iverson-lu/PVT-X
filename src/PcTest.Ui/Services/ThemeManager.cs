using System.Windows;

namespace PcTest.Ui.Services;

/// <summary>
/// Manages application theme switching at runtime.
/// </summary>
public sealed class ThemeManager : IThemeManager
{
    private const string LightThemePath = "Themes/Light/Colors.Light.xaml";
    private const string DarkThemePath = "Themes/Dark/Colors.Dark.xaml";
    private const string SharedThemePath = "Themes/Theme.Shared.xaml";
    
    private string _currentTheme = "Light";
    private ResourceDictionary? _currentThemeDictionary;
    
    public event EventHandler<string>? ThemeChanged;
    
    public string CurrentTheme => _currentTheme;
    
    /// <summary>
    /// Applies the specified theme at runtime.
    /// </summary>
    /// <param name="theme">Theme name: "Light" or "Dark"</param>
    public void ApplyTheme(string theme)
    {
        if (string.IsNullOrEmpty(theme))
            theme = "Light";
        
        var normalizedTheme = theme.Trim();
        if (!normalizedTheme.Equals("Light", StringComparison.OrdinalIgnoreCase) &&
            !normalizedTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase))
        {
            normalizedTheme = "Light";
        }
        
        var app = Application.Current;
        if (app == null) return;
        
        // Determine theme path
        var themePath = normalizedTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase)
            ? DarkThemePath
            : LightThemePath;
        
        // Create new theme dictionary
        var newThemeDictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/{themePath}", UriKind.Absolute)
        };
        
        // Remove old theme dictionary if exists
        if (_currentThemeDictionary != null)
        {
            app.Resources.MergedDictionaries.Remove(_currentThemeDictionary);
        }
        
        // Find and remove any existing theme dictionaries by path pattern
        var toRemove = app.Resources.MergedDictionaries
            .Where(d => d.Source?.ToString().Contains("Colors.Light.xaml") == true ||
                       d.Source?.ToString().Contains("Colors.Dark.xaml") == true ||
                       d.Source?.ToString().Contains("Resources/Colors.xaml") == true)
            .ToList();
        
        foreach (var dict in toRemove)
        {
            app.Resources.MergedDictionaries.Remove(dict);
        }
        
        // Add new theme dictionary at the end (highest priority)
        app.Resources.MergedDictionaries.Add(newThemeDictionary);
        _currentThemeDictionary = newThemeDictionary;
        
        // Update current theme
        _currentTheme = normalizedTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
        
        // Apply WPF UI theme as well
        var wpfUiTheme = _currentTheme == "Dark" 
            ? Wpf.Ui.Appearance.ApplicationTheme.Dark 
            : Wpf.Ui.Appearance.ApplicationTheme.Light;
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(wpfUiTheme);
        
        ThemeChanged?.Invoke(this, _currentTheme);
    }
    
    /// <summary>
    /// Toggles between Light and Dark themes.
    /// </summary>
    public void ToggleTheme()
    {
        var newTheme = _currentTheme == "Dark" ? "Light" : "Dark";
        ApplyTheme(newTheme);
    }
    
    /// <summary>
    /// Initializes the theme system with shared resources.
    /// Should be called once during app startup before ApplyTheme.
    /// </summary>
    public void Initialize()
    {
        var app = Application.Current;
        if (app == null) return;
        
        // Add shared theme resources if not already present
        var hasShared = app.Resources.MergedDictionaries
            .Any(d => d.Source?.ToString().Contains("Theme.Shared.xaml") == true);
        
        if (!hasShared)
        {
            var sharedDict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/{SharedThemePath}", UriKind.Absolute)
            };
            app.Resources.MergedDictionaries.Add(sharedDict);
        }
    }
}
