# Settings Page Redesign - Control Mapping

## Overview
The Settings page has been redesigned with a modern card-based layout while maintaining **all original controls and bindings unchanged**.

---

## Control Mapping

### Path Configuration Card

| Setting | Control Type | Binding | Status |
|---------|--------------|---------|--------|
| **Assets Root** | `ui:TextBox` | `{Binding AssetsRoot, UpdateSourceTrigger=PropertyChanged}` | ✓ Preserved |
| **Assets Root Browse** | `ui:Button` | `Command="{Binding BrowseAssetsRootCommand}"` | ✓ Preserved (icon-only) |
| **Runs Directory** | `ui:TextBox` | `{Binding RunsDirectory, UpdateSourceTrigger=PropertyChanged}` | ✓ Preserved |
| **Runs Directory Browse** | `ui:Button` | `Command="{Binding BrowseRunsDirectoryCommand}"` | ✓ Preserved (icon-only) |
| **Runner Executable** | `ui:TextBox` | `{Binding RunnerExecutable, UpdateSourceTrigger=PropertyChanged}` | ✓ Preserved |
| **Runner Executable Browse** | `ui:Button` | `Command="{Binding BrowseRunnerExecutableCommand}"` | ✓ Preserved (icon-only) |

---

### Appearance Card

| Setting | Control Type | Binding | Status |
|---------|--------------|---------|--------|
| **Theme** | `ComboBox` | `ItemsSource="{Binding ThemeOptions}"` <br/> `SelectedItem="{Binding SelectedTheme}"` | ✓ Preserved |
| **Show Console Window** | `CheckBox` | `IsChecked="{Binding ShowConsoleWindow}"` | ✓ Preserved |

---

### Execution Card

| Setting | Control Type | Binding | Status |
|---------|--------------|---------|--------|
| **Default Timeout** | `ui:NumberBox` | `Value="{Binding DefaultTimeout}"` <br/> `Minimum="0"` `Maximum="3600"` | ✓ Preserved |
| **Max Retry Count** | `ui:NumberBox` | `Value="{Binding MaxRetryCount}"` <br/> `Minimum="0"` `Maximum="10"` | ✓ Preserved |
| **Auto-refresh History** | `CheckBox` | `IsChecked="{Binding AutoRefreshHistory}"` | ✓ Preserved |
| **Show Debug Output** | `CheckBox` | `IsChecked="{Binding ShowDebugOutput}"` | ✓ Preserved |

---

### Import/Export Card

| Action | Control Type | Binding | Status |
|--------|--------------|---------|--------|
| **Import Settings** | `ui:Button` | `Command="{Binding ImportSettingsCommand}"` | ✓ Preserved |
| **Export Settings** | `ui:Button` | `Command="{Binding ExportSettingsCommand}"` | ✓ Preserved |
| **Reset to Defaults** | `ui:Button` | `Command="{Binding ResetSettingsCommand}"` <br/> `Appearance="Caution"` | ✓ Preserved |

---

### Save Changes Section

| Element | Control Type | Binding | Status |
|---------|--------------|---------|--------|
| **Unsaved Changes Warning** | `TextBlock` | Conditional display based on `{Binding HasUnsavedChanges}` | ✓ Preserved |
| **Save Button** | `ui:Button` | `Command="{Binding SaveCommand}"` <br/> `IsEnabled="{Binding HasUnsavedChanges}"` <br/> `Appearance="Primary"` | ✓ Preserved |

---

## Design Changes Applied

### Layout Improvements
- ✅ **Page background**: Changed to `SurfaceBackgroundBrush` (subtle neutral)
- ✅ **Card-based layout**: Replaced `CardExpander` with `Border` cards
- ✅ **Card styling**: 
  - Corner radius: 10px (`RadiusM`)
  - Padding: 20px
  - Subtle elevation with `DropShadowEffect`
  - Consistent 16px vertical spacing between cards

### Typography Hierarchy
- ✅ **Page title**: "Settings" with `PageTitleStyle` (24px, SemiBold)
- ✅ **Card headers**: `SectionTitleStyle` (16px, SemiBold)
- ✅ **Field labels**: Muted secondary text (13px)
- ✅ **Subsection headers**: Small uppercase-style headers (11px, SemiBold, tertiary color)

### Grid Alignment
- ✅ **Consistent label column**: 180px width across all cards
- ✅ **Vertical alignment**: All inputs align consistently

### Browse Button Updates
- ✅ **Icon-only buttons**: Removed "Browse" text, kept folder icon
- ✅ **Secondary appearance**: Less visually dominant
- ✅ **Tooltips added**: "Browse..." for clarity

### Checkbox Grouping
- ✅ **Console subsection**: Groups console-related checkbox under "Console" header
- ✅ **Automation subsection**: Groups automation checkboxes under "Automation" header
- ✅ **Improved spacing**: 8px between checkboxes, clear visual separation

### Spacing Refinements
- ✅ **Between cards**: 16px
- ✅ **Within cards**: 16-20px between settings
- ✅ **Card padding**: 20px
- ✅ **Page margins**: 16px with 900px max-width for optimal readability

---

## New Shared Styles Added

Added to `DesignTokens.xaml`:

```xaml
<!-- Field Label Style -->
<Style x:Key="FieldLabelStyle" TargetType="TextBlock">
    <Setter Property="FontSize" Value="{StaticResource FontSizeM}"/>
    <Setter Property="FontWeight" Value="{StaticResource FontWeightNormal}"/>
    <Setter Property="Foreground" Value="{StaticResource TextSecondaryBrush}"/>
    <Setter Property="VerticalAlignment" Value="Center"/>
</Style>

<!-- Subsection Header Style -->
<Style x:Key="SubsectionHeaderStyle" TargetType="TextBlock">
    <Setter Property="FontSize" Value="{StaticResource FontSizeS}"/>
    <Setter Property="FontWeight" Value="{StaticResource FontWeightSemiBold}"/>
    <Setter Property="Foreground" Value="{StaticResource TextTertiaryBrush}"/>
    <Setter Property="Margin" Value="0,0,0,8"/>
</Style>
```

---

## Summary

✅ **All controls preserved** - No functionality removed  
✅ **All bindings intact** - ViewModel contract unchanged  
✅ **Modern card-based design** - Matches Runs/Plan pages  
✅ **Professional hierarchy** - Clear visual organization  
✅ **Improved scannability** - Better spacing and grouping  
✅ **Icon-optimized buttons** - Less visual noise  

**Result**: A calm, professional, scannable Settings page that maintains full backwards compatibility while matching the modern design system.
