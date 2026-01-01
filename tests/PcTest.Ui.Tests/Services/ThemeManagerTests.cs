using FluentAssertions;
using PcTest.Ui.Services;
using System.Windows;
using Xunit;

namespace PcTest.Ui.Tests.Services;

/// <summary>
/// Tests for ThemeManager service.
/// </summary>
public class ThemeManagerTests : IDisposable
{
    private readonly Application _testApp;
    private readonly ThemeManager _themeManager;

    public ThemeManagerTests()
    {
        // Create a test WPF application instance
        if (Application.Current == null)
        {
            _testApp = new Application();
        }
        else
        {
            _testApp = Application.Current;
        }

        _themeManager = new ThemeManager();
    }

    public void Dispose()
    {
        // Cleanup is handled by xUnit
    }

    [Fact]
    public void Constructor_ShouldSetDefaultThemeToLight()
    {
        // Assert
        _themeManager.CurrentTheme.Should().Be("Light");
    }

    [Fact]
    public void Initialize_ShouldLoadSharedTheme()
    {
        // Act
        _themeManager.Initialize();

        // Assert
        var sharedThemeLoaded = _testApp.Resources.MergedDictionaries
            .Any(d => d.Source?.ToString().Contains("Theme.Shared.xaml") == true);
        
        sharedThemeLoaded.Should().BeTrue("Theme.Shared.xaml should be loaded");
    }

    [Fact]
    public void ApplyTheme_WithLightTheme_ShouldLoadLightColors()
    {
        // Arrange
        _themeManager.Initialize();

        // Act
        _themeManager.ApplyTheme("Light");

        // Assert
        _themeManager.CurrentTheme.Should().Be("Light");
        
        var lightThemeLoaded = _testApp.Resources.MergedDictionaries
            .Any(d => d.Source?.ToString().Contains("Colors.Light.xaml") == true);
        
        lightThemeLoaded.Should().BeTrue("Colors.Light.xaml should be loaded");
    }

    [Fact]
    public void ApplyTheme_WithDarkTheme_ShouldLoadDarkColors()
    {
        // Arrange
        _themeManager.Initialize();

        // Act
        _themeManager.ApplyTheme("Dark");

        // Assert
        _themeManager.CurrentTheme.Should().Be("Dark");
        
        var darkThemeLoaded = _testApp.Resources.MergedDictionaries
            .Any(d => d.Source?.ToString().Contains("Colors.Dark.xaml") == true);
        
        darkThemeLoaded.Should().BeTrue("Colors.Dark.xaml should be loaded");
    }

    [Fact]
    public void ApplyTheme_ShouldReplaceExistingTheme()
    {
        // Arrange
        _themeManager.Initialize();
        _themeManager.ApplyTheme("Light");
        
        var lightThemeCount = _testApp.Resources.MergedDictionaries
            .Count(d => d.Source?.ToString().Contains("Colors.Light.xaml") == true);

        // Act
        _themeManager.ApplyTheme("Dark");

        // Assert
        var darkThemeLoaded = _testApp.Resources.MergedDictionaries
            .Any(d => d.Source?.ToString().Contains("Colors.Dark.xaml") == true);
        
        var lightThemeStillPresent = _testApp.Resources.MergedDictionaries
            .Any(d => d.Source?.ToString().Contains("Colors.Light.xaml") == true);

        darkThemeLoaded.Should().BeTrue("Dark theme should be loaded");
        lightThemeStillPresent.Should().BeFalse("Light theme should be removed");
    }

    [Fact]
    public void ApplyTheme_WithInvalidTheme_ShouldDefaultToLight()
    {
        // Arrange
        _themeManager.Initialize();

        // Act
        _themeManager.ApplyTheme("InvalidTheme");

        // Assert
        _themeManager.CurrentTheme.Should().Be("Light");
    }

    [Fact]
    public void ApplyTheme_WithNullTheme_ShouldDefaultToLight()
    {
        // Arrange
        _themeManager.Initialize();

        // Act
        _themeManager.ApplyTheme(null!);

        // Assert
        _themeManager.CurrentTheme.Should().Be("Light");
    }

    [Fact]
    public void ApplyTheme_WithEmptyTheme_ShouldDefaultToLight()
    {
        // Arrange
        _themeManager.Initialize();

        // Act
        _themeManager.ApplyTheme(string.Empty);

        // Assert
        _themeManager.CurrentTheme.Should().Be("Light");
    }

    [Fact]
    public void ApplyTheme_ShouldBeCaseInsensitive()
    {
        // Arrange
        _themeManager.Initialize();

        // Act
        _themeManager.ApplyTheme("dark");

        // Assert
        _themeManager.CurrentTheme.Should().Be("Dark");
        
        // Act
        _themeManager.ApplyTheme("LIGHT");

        // Assert
        _themeManager.CurrentTheme.Should().Be("Light");
    }

    [Fact]
    public void ToggleTheme_FromLightToDark_ShouldSwitchTheme()
    {
        // Arrange
        _themeManager.Initialize();
        _themeManager.ApplyTheme("Light");

        // Act
        _themeManager.ToggleTheme();

        // Assert
        _themeManager.CurrentTheme.Should().Be("Dark");
    }

    [Fact]
    public void ToggleTheme_FromDarkToLight_ShouldSwitchTheme()
    {
        // Arrange
        _themeManager.Initialize();
        _themeManager.ApplyTheme("Dark");

        // Act
        _themeManager.ToggleTheme();

        // Assert
        _themeManager.CurrentTheme.Should().Be("Light");
    }

    [Fact]
    public void ToggleTheme_MultipleTimes_ShouldAlternateThemes()
    {
        // Arrange
        _themeManager.Initialize();
        _themeManager.ApplyTheme("Light");

        // Act & Assert
        _themeManager.ToggleTheme();
        _themeManager.CurrentTheme.Should().Be("Dark");

        _themeManager.ToggleTheme();
        _themeManager.CurrentTheme.Should().Be("Light");

        _themeManager.ToggleTheme();
        _themeManager.CurrentTheme.Should().Be("Dark");
    }

    [Fact]
    public void ThemeChanged_ShouldRaiseEvent_WhenThemeApplied()
    {
        // Arrange
        _themeManager.Initialize();
        string? raisedTheme = null;
        _themeManager.ThemeChanged += (sender, theme) => raisedTheme = theme;

        // Act
        _themeManager.ApplyTheme("Dark");

        // Assert
        raisedTheme.Should().Be("Dark");
    }

    [Fact]
    public void ThemeChanged_ShouldRaiseEvent_WhenThemeToggled()
    {
        // Arrange
        _themeManager.Initialize();
        _themeManager.ApplyTheme("Light");
        
        string? raisedTheme = null;
        _themeManager.ThemeChanged += (sender, theme) => raisedTheme = theme;

        // Act
        _themeManager.ToggleTheme();

        // Assert
        raisedTheme.Should().Be("Dark");
    }

    [Fact]
    public void ThemeChanged_ShouldRaiseMultipleEvents_ForMultipleChanges()
    {
        // Arrange
        _themeManager.Initialize();
        var raisedThemes = new List<string>();
        _themeManager.ThemeChanged += (sender, theme) => raisedThemes.Add(theme);

        // Act
        _themeManager.ApplyTheme("Dark");
        _themeManager.ApplyTheme("Light");
        _themeManager.ApplyTheme("Dark");

        // Assert
        raisedThemes.Should().HaveCount(3);
        raisedThemes[0].Should().Be("Dark");
        raisedThemes[1].Should().Be("Light");
        raisedThemes[2].Should().Be("Dark");
    }

    [Fact]
    public void ApplyTheme_ShouldLoadResourcesSuccessfully()
    {
        // Arrange
        _themeManager.Initialize();

        // Act
        _themeManager.ApplyTheme("Light");

        // Assert - Check that key resources are available
        var resource = _testApp.Resources["AppBackgroundBrush"];
        resource.Should().NotBeNull("AppBackgroundBrush should be available in theme");
    }

    [Fact]
    public void ApplyTheme_ShouldUpdateResourcesForDynamicBinding()
    {
        // Arrange
        _themeManager.Initialize();
        _themeManager.ApplyTheme("Light");
        
        var lightBackground = _testApp.Resources["AppBackgroundBrush"];

        // Act
        _themeManager.ApplyTheme("Dark");
        var darkBackground = _testApp.Resources["AppBackgroundBrush"];

        // Assert
        lightBackground.Should().NotBeNull();
        darkBackground.Should().NotBeNull();
        darkBackground.Should().NotBe(lightBackground, "Theme colors should be different");
    }

    [Fact]
    public void Initialize_CalledMultipleTimes_ShouldNotDuplicateSharedResources()
    {
        // Act
        _themeManager.Initialize();
        _themeManager.Initialize();
        _themeManager.Initialize();

        // Assert
        var sharedThemeCount = _testApp.Resources.MergedDictionaries
            .Count(d => d.Source?.ToString().Contains("Theme.Shared.xaml") == true);
        
        sharedThemeCount.Should().Be(1, "Theme.Shared.xaml should only be loaded once");
    }

    [Fact]
    public void ApplyTheme_SameTwice_ShouldNotDuplicateResources()
    {
        // Arrange
        _themeManager.Initialize();

        // Act
        _themeManager.ApplyTheme("Light");
        var countAfterFirst = _testApp.Resources.MergedDictionaries
            .Count(d => d.Source?.ToString().Contains("Colors.Light.xaml") == true);

        _themeManager.ApplyTheme("Light");
        var countAfterSecond = _testApp.Resources.MergedDictionaries
            .Count(d => d.Source?.ToString().Contains("Colors.Light.xaml") == true);

        // Assert
        countAfterFirst.Should().Be(1);
        countAfterSecond.Should().Be(1, "Applying same theme twice should not duplicate resources");
    }
}
