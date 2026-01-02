# PC Test System - Theme System Documentation

## Overview

The PC Test System implements a comprehensive theme system that supports Light and Dark themes with runtime switching. The theme system is built using WPF ResourceDictionaries and follows modern design token principles for maintainability and consistency.

## Architecture

### File Structure

```
src/PcTest.Ui/
├── Themes/
│   ├── Light/
│   │   └── Colors.Light.xaml       # Light theme color definitions
│   ├── Dark/
│   │   └── Colors.Dark.xaml        # Dark theme color definitions
│   └── Theme.Shared.xaml           # Theme-agnostic tokens (spacing, typography)
├── Resources/
│   ├── DesignTokens.xaml           # Text styles and card styles
│   └── Styles.xaml                 # Component-specific styles
└── Services/
    ├── IThemeManager.cs            # Theme manager interface
    └── ThemeManager.cs             # Theme switching implementation
```

### Loading Order

Resources are loaded in the following order in [App.xaml](../src/PcTest.Ui/App.xaml):

1. **WPF UI Dictionaries** - Base WPF UI library themes and controls
2. **Theme.Shared.xaml** - Theme-agnostic tokens (spacing, radius, typography)
3. **Colors.Light.xaml** or **Colors.Dark.xaml** - Active theme colors
4. **DesignTokens.xaml** - Text styles and card styles
5. **Styles.xaml** - Component-specific styles (navigation, badges, tabs)

## Theme Tokens

### Semantic Color Tokens

All colors follow a semantic naming convention: `{Semantic}{Property}Color/Brush`

#### Application Colors
- `AppBackground` - Main application background
- `SurfaceBackground` - Page/surface background
- `CardBackground` - Card container background
- `OverlayBackground` - Modal overlay background

#### Text Colors
- `TextPrimary` - Primary body text
- `TextSecondary` - Secondary/muted text
- `TextTertiary` - Tertiary/disabled text
- `TextOnPrimary` - Text on primary brand color
- `TextOnDark` - Text on dark backgrounds

#### Border Colors
- `Border` - Default border color
- `BorderSubtle` - Subtle/light borders
- `BorderEmphasis` - Emphasized borders
- `BorderHover` - Border hover state

#### Brand Colors
- `Primary` - Primary brand color (#2563EB light, #3B82F6 dark)
- `PrimaryHover` - Primary hover state
- `PrimaryPressed` - Primary pressed state
- `PrimaryLight` - Light primary background
- `PrimarySubtle` - Subtle primary background

#### Selection Colors
- `SelectionBackground` - Selection background
- `SelectionIndicator` - Selection indicator
- `SelectionHover` - Selection hover state

#### Navigation Colors
- `NavBackground` - Navigation sidebar background
- `NavItemHover` - Navigation item hover state
- `NavItemSelectedBackground` - Selected navigation item
- `NavIconNormal` - Navigation icon normal state
- `NavIconHover` - Navigation icon hover state

#### Control Colors
- `ControlBackground` - Input control background
- `ControlBorder` - Input control border
- `ControlHover` - Control hover state
- `ControlFocusBorder` - Control focus border

#### Status Colors
- `StatusSuccess` - Success state (green)
- `StatusWarning` - Warning state (amber)
- `StatusError` - Error state (red)
- `StatusRunning` - Running state (blue)
- `StatusPending` - Pending state (gray)
- `StatusInfo` - Info state (cyan)

#### Badge Colors
- `BadgeBackground` - Badge background
- `BadgeBorder` - Badge border
- `BadgeText` - Badge text

#### Tab Colors
- `TabContainerBackground` - Tab container background
- `TabSelectedBackground` - Selected tab background
- `TabHoverBackground` - Tab hover background
- `TabText` - Tab text color
- `TabSelectedText` - Selected tab text

#### Grid Colors
- `GridLine` - DataGrid line color
- `GridRowHover` - Grid row hover background
- `GridRowSelected` - Grid row selected background
- `GridRowAlternate` - Grid alternate row background
- `GridHeader` - Grid header background

#### Terminal Colors
- `TerminalBackground` - Terminal/console background (always dark)
- `TerminalText` - Terminal text color

### Spacing Tokens

Defined in [Theme.Shared.xaml](../src/PcTest.Ui/Themes/Theme.Shared.xaml):

- `SpaceXS`: 4px
- `SpaceS`: 8px
- `SpaceM`: 12px
- `SpaceL`: 16px
- `SpaceXL`: 24px
- `Space2XL`: 32px

Thickness variants (ThicknessXS, ThicknessS, etc.) are also available.

### Typography Tokens

Font sizes:
- `FontSizeXS`: 10px
- `FontSizeS`: 11px
- `FontSizeM`: 13px (default)
- `FontSizeL`: 14px
- `FontSizeXL`: 16px
- `FontSize2XL`: 20px
- `FontSize3XL`: 24px

Font weights:
- `FontWeightNormal`: Normal
- `FontWeightMedium`: Medium
- `FontWeightSemiBold`: SemiBold
- `FontWeightBold`: Bold

### Corner Radius Tokens

- `RadiusS`: 6px - Small controls
- `RadiusM`: 10px - Cards, buttons
- `RadiusL`: 14px - Large containers

## Text Styles

Defined in [DesignTokens.xaml](../src/PcTest.Ui/Resources/DesignTokens.xaml):

### Title Styles
- `PageTitleStyle` - 24px SemiBold for page titles
- `PageSubtitleStyle` - 14px Normal for page subtitles
- `SectionTitleStyle` - 16px SemiBold for section headers
- `CardTitleStyle` - 14px SemiBold for card titles

### Body Styles
- `BodyTextStyle` - 13px Normal for body text
- `SecondaryTextStyle` - 11px for secondary text
- `TertiaryTextStyle` - 10px for tertiary text
- `LabelTextStyle` - 13px SemiBold for labels

### Form Styles
- `FieldLabelStyle` - Form field labels
- `SubsectionHeaderStyle` - Subsection headers

## Component Styles

Defined in [Styles.xaml](../src/PcTest.Ui/Resources/Styles.xaml):

### Navigation Styles
- `CompactNavButtonStyle` - Compact navigation buttons
- `CompactNavButtonSelectedStyle` - Selected navigation button state

### Badge Styles
- Custom Badge template for Secondary appearance

### Tab Styles
- `SegmentedControlTabItem` - Segmented tab control items
- `SegmentedControlTabControl` - Segmented tab control container

## Runtime Theme Switching

### ThemeManager Service

The [ThemeManager](../src/PcTest.Ui/Services/ThemeManager.cs) service handles runtime theme switching:

```csharp
public interface IThemeManager
{
    string CurrentTheme { get; }
    event EventHandler<string> ThemeChanged;
    
    void ApplyTheme(string theme);
    void ToggleTheme();
    void Initialize();
}
```

### Usage Example

Theme switching is integrated into the Settings page:

```csharp
// In SettingsViewModel.cs
private readonly IThemeManager _themeManager;

partial void OnThemeChanged(string value)
{
    _themeManager.ApplyTheme(value);
}
```

### How It Works

1. **Initialization**: `ThemeManager.Initialize()` is called in [App.xaml.cs](../src/PcTest.Ui/App.xaml.cs) startup
2. **Theme Application**: `ApplyTheme(theme)` dynamically swaps resource dictionaries
3. **Immediate Update**: Uses `DynamicResource` bindings for instant UI updates
4. **Persistence**: Theme preference is saved to settings file via `ISettingsService`

## Using the Theme System

### XAML Binding

Use `DynamicResource` for all theme-dependent properties:

```xml
<!-- Backgrounds -->
<Border Background="{DynamicResource CardBackgroundBrush}">

<!-- Borders -->
<Border BorderBrush="{DynamicResource BorderSubtleBrush}">

<!-- Text -->
<TextBlock Foreground="{DynamicResource TextPrimaryBrush}">

<!-- Status Colors -->
<TextBlock Foreground="{DynamicResource StatusSuccessBrush}">
```

Use `StaticResource` for theme-agnostic tokens:

```xml
<!-- Spacing -->
<StackPanel Margin="{StaticResource ThicknessL}">

<!-- Typography -->
<TextBlock FontSize="{StaticResource FontSizeL}"
           FontWeight="{StaticResource FontWeightSemiBold}">

<!-- Corner Radius -->
<Border CornerRadius="{StaticResource RadiusM}">
```

### Text Styles

Apply predefined text styles:

```xml
<TextBlock Text="Page Title" 
           Style="{StaticResource PageTitleStyle}"/>

<TextBlock Text="Section Title" 
           Style="{StaticResource SectionTitleStyle}"/>

<TextBlock Text="Body Text" 
           Style="{StaticResource BodyTextStyle}"/>
```

### Card Containers

Use the card container style for consistent card appearance:

```xml
<Border Style="{StaticResource CardContainerStyle}">
    <!-- Card content -->
</Border>
```

## Creating a New Theme

To add a new theme variant:

1. Create a new color dictionary file (e.g., `Themes/HighContrast/Colors.HighContrast.xaml`)
2. Define all semantic color tokens with appropriate values
3. Update `ThemeManager.cs` to include the new theme path
4. Add the theme option to `SettingsViewModel.ThemeOptions`

Example theme file structure:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- Application Colors -->
    <Color x:Key="AppBackgroundColor">#FFFFFF</Color>
    <SolidColorBrush x:Key="AppBackgroundBrush" Color="{StaticResource AppBackgroundColor}"/>
    
    <!-- Define all other semantic tokens... -->
    
    <!-- Legacy Key Mappings (for backward compatibility) -->
    <SolidColorBrush x:Key="BrandPrimaryBrush" Color="{StaticResource PrimaryColor}"/>
    <!-- ... -->
</ResourceDictionary>
```

## Legacy Key Mapping

For backward compatibility, legacy keys from the original `Colors.xaml` are mapped to new semantic tokens:

```xml
<!-- Legacy mappings -->
<SolidColorBrush x:Key="BrandPrimaryBrush" Color="{StaticResource PrimaryColor}"/>
<SolidColorBrush x:Key="BrandSecondaryBrush" Color="{StaticResource TextSecondaryColor}"/>
<SolidColorBrush x:Key="SystemBackgroundBrush" Color="{StaticResource AppBackgroundColor}"/>
<!-- ... -->
```

This ensures existing code continues to work while new code uses semantic token names.

## Color Values

### Light Theme

Key colors in Light theme:
- Background: `#F8F9FA` (light gray)
- Surface: `#FFFFFF` (white)
- Primary: `#2563EB` (blue-600)
- Text Primary: `#0C0C0C` (near black)
- Text Secondary: `#525252` (gray-600)
- Border: `#E0E0E0` (gray-300)

### Dark Theme

Key colors in Dark theme:
- Background: `#1A1A1A` (dark gray)
- Surface: `#242424` (slightly lighter dark)
- Primary: `#3B82F6` (brighter blue-500)
- Text Primary: `#E4E4E4` (light gray)
- Text Secondary: `#A3A3A3` (gray-400)
- Border: `#3F3F3F` (dark border)

## Known Limitations

1. **DropShadowEffect.Color**: WPF's `DropShadowEffect.Color` property does not support `DynamicResource`. Shadow colors remain fixed across themes.

2. **Terminal Backgrounds**: Terminal/console viewers always use dark backgrounds (`#0C0C0C`) in both themes for readability, which is intentional.

3. **WPF UI Control Colors**: Some WPF UI library controls have built-in color schemes that may not fully adapt to custom theme tokens.

## Theme Toggle Location

Users can toggle between Light and Dark themes:

**Settings Page → Appearance Section → Theme Dropdown**

The theme change takes effect immediately without requiring an application restart.

## Best Practices

1. **Always use semantic tokens**: Never use hex color values directly in XAML
2. **Use DynamicResource for colors**: Enables runtime theme switching
3. **Use StaticResource for layout tokens**: Spacing, typography, etc. don't change with theme
4. **Follow naming conventions**: `{Semantic}{Property}Color/Brush`
5. **Test in both themes**: Ensure readability and contrast in Light and Dark modes
6. **Maintain legacy mappings**: Preserve backward compatibility when adding new tokens

## Testing

To verify theme system functionality:

1. Build the application: `dotnet build src/PcTest.Ui/PcTest.Ui.csproj`
2. Run the application: `dotnet run --project src/PcTest.Ui/PcTest.Ui.csproj`
3. Navigate to Settings page
4. Change Theme dropdown between "Light" and "Dark"
5. Verify all UI elements update immediately
6. Test all pages: Plan, Run, History, Settings

## Future Enhancements

Potential improvements to consider:

- **System Theme**: Follow OS theme preference
- **Custom Themes**: Allow users to create/import custom color schemes
- **Theme Transitions**: Smooth fade animations during theme switches
- **High Contrast Mode**: Additional theme for accessibility
- **Per-Page Overrides**: Allow pages to override specific theme colors
- **Theme Preview**: Live preview in settings before applying

---

**Version**: 1.0  
**Last Updated**: January 2026  
**Maintainer**: PC Test System Team
