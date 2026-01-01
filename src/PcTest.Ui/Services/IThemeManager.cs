namespace PcTest.Ui.Services;

/// <summary>
/// Interface for theme management.
/// </summary>
public interface IThemeManager
{
    /// <summary>
    /// Gets the current theme name ("Light" or "Dark").
    /// </summary>
    string CurrentTheme { get; }
    
    /// <summary>
    /// Applies the specified theme.
    /// </summary>
    /// <param name="theme">Theme name: "Light" or "Dark"</param>
    void ApplyTheme(string theme);
    
    /// <summary>
    /// Toggles between Light and Dark themes.
    /// </summary>
    void ToggleTheme();
    
    /// <summary>
    /// Initializes the theme system.
    /// </summary>
    void Initialize();
    
    /// <summary>
    /// Event raised when theme changes.
    /// </summary>
    event EventHandler<string>? ThemeChanged;
}
