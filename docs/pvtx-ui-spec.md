# PC Test System – UI Specification (v0.5)

> Based on PC Test System Core Spec v1.5.1  
> UI Platform: WPF (.NET 10) + MVVM + Fluent (WPF-UI)  
> Status: **UI SPEC FROZEN – READY FOR CODING**

---

## 0. Goals and Principles

### 0.1 Goals
- Provide a **modern Windows 10/11 native application** experience
- Cover the complete workflow: **Plan → Run → Diagnose → History**
- Emphasize **diagnosability, reproducibility, and traceability**

### 0.2 Core Principles
- UI is decoupled from Engine / Runner
- Test Cases are discovered from assets, not created in UI
- Suites and Plans are container/configuration entities
- UI is data-driven by manifest, events, and artifacts
- ViewModels own state and orchestration; Views contain no business logic

---

## 1. Technology Stack (Frozen)

- UI Framework: WPF (`net10.0-windows`)
- Visual Style: Fluent / Windows 11 (WPF-UI)
- Architecture: MVVM
- Commands: `ICommand` / `RelayCommand`
- Dependency Injection: `Microsoft.Extensions.DependencyInjection`
- JSON: `System.Text.Json`

### 1.1 Naming Conventions

- Suite IDs SHOULD use prefix `suite.` (e.g., `suite.hw.full_test`)
- Plan IDs SHOULD use prefix `plan.` (e.g., `plan.sys.regression`)
- UI pre-fills these prefixes when creating new Suites/Plans
- Identity format: `id@version` is used for uniqueness validation and discovery keys

---

## 2. Top-Level Information Architecture (Frozen)

### 2.1 Navigation (4 Items - Updated)

1. **Plan** – Asset and execution planning workspace  
2. **Run** – Execution and real-time monitoring  
3. **History** – Unified run browser with master-detail inspector (replaces separate "Logs & Results")
4. **Settings** – Tool-level global configuration  

> **CHANGE**: History page now combines run browsing and detailed inspection in a single unified view with master-detail layout.
> The separate "Logs & Results" page has been consolidated into the History page's detail panel.

### 2.2 Navigation UI (Updated)

- **Compact Icon-Only Sidebar**: 68px wide borderless navigation panel
- **Visual Selection Indicator**: Left accent bar (2px) shows current page context
- **Icons**: Plan (Clipboard24), Run (Play24), History (History24), Settings (Settings24)
- **Interaction Model**:
  - No button-pressed visual state (buttons don't appear "pushed in")
  - Selection is contextual (shows current location, not button state)
  - Hover provides subtle feedback without visual button press
  - Settings button separated at bottom with divider

---

## 3. Page Specifications

## 3.1 Plan Page (Primary Workspace)

### 3.1.1 Purpose
- Central workspace for managing Cases, Suites, and Plans
- Entry point for asset discovery
- Create, edit, import, and export Suites and Plans
- Perform most Suite/Plan editing inline

### 3.1.2 Layout
- **Segmented Tab Control**: Modern pill-style tab navigation (top-aligned)
- Tabs: Cases / Suites / Plans
- Each tab contains:
  - Left: Resource list (search, filter, actions)
  - Right: **Inline editor panel** (when item selected)

### 3.1.3 Visual Design Updates
- **Segmented Control Style**: Pills with rounded corners, selected state with colored background
- **Card-Based Layout**: Elevated cards with drop shadows for content sections
- **Improved Spacing**: Consistent margins and padding using design tokens
- **Badge Styling**: Status and type indicators with custom secondary appearance

---

### 3.1.3 Cases Tab (Read-only)
- Display fields: name, id, version, tags, timeout, privilege
- **Privilege indicators**:
  - Shield icon (filled, blue) for `AdminRequired`
  - Shield icon (outline, blue) for `AdminPreferred`
  - No icon for `User` (default)
- Detail panel: manifest metadata, parameter definitions summary
- Actions:
  - **Discover…** (scan and refresh Case assets)
  - **Import** (import test case from zip archive)
  - Open in Explorer

> Cases are not created in UI. They originate from discovered manifests or imported from zip archives.

---

### 3.1.4 Suites Tab (Editable)

#### Supported Operations
- Create Suite
- Edit Suite metadata
- Manage execution pipeline nodes
- Import Suite (`*.suite.json`)
- Export Suite (`*.suite.json`)
- Duplicate / Rename (recommended)
- Validate Suite

#### Suite Inline Editor
- Header: Suite id@version, unsaved (dirty) indicator
- Metadata:
  - name, description, tags
  - timeout, privilege (if supported)
- Test Flow:
  - Add / remove / reorder nodes
  - Each node references a Case
- Node Inputs:
  - Dynamic Parameter Editor (see 3.1.6)

#### Actions
- Save
- Discard Changes
- Validate
- Run…

---

### 3.1.5 Plans Tab (Editable)

#### Supported Operations
- Create Plan
- Edit Plan metadata
- Manage referenced Suites
- Import Plan (`*.plan.json`)
- Export Plan (`*.plan.json`)
- Validate Plan

#### Plan Inline Editor
- Metadata: name, description, tags
- Suite references:
  - Add Suite (from existing Suites)
  - Remove / reorder
  - Display suiteId@version

#### Actions
- Save
- Discard Changes
- Validate
- Run…

---

### 3.1.6 Dynamic Parameter Editor (Reusable)

Used for:
- Suite node input overrides
- Any future Suite/Plan-level overrides

#### Each parameter row must display
- Parameter name
- Editing control (by type)
- Validation state
- **Value Source Badge**

#### Value Source Badges (Frozen)
- Default
- Preset (if applicable)
- SuiteOverride
- RunOverride
- EnvDerived
- Resolved

#### Supported behaviors
- Reset to Default
- Show Resolution (merge chain visualization)
- EnvDerived shows environment key and resolved value

---

## 3.2 Run Page

### Purpose
- Start execution
- Monitor pipeline progress
- Control Stop / Abort
- **Navigate back to source** (when triggered from Plan page)

### Privilege Checking
Before execution starts, the UI validates privilege requirements:
- **AdminRequired**: Blocks execution if not elevated, shows error dialog
- **AdminPreferred**: Shows warning dialog, allows user to continue or cancel
- Suite/Plan privilege = max(child privileges): `AdminRequired > AdminPreferred > User`

### Layout
- **Back button** (top-left, shown when navigated from Plan page)
- Left: Node list (Suite or Plan execution)
- Right: Live output (Console / simplified Events)
- Top: Run control bar

### Navigation Context
- **Back Navigation**: When execution is triggered from Plan page (Cases/Suites/Plans tab), a back button appears allowing return to the exact source item
- **Context Preservation**: Back navigation restores the originating tab and selects the item that was executed
- **Source Tracking**: Navigation parameter includes source page, tab index, and target identity for precise context restoration

### Console Output
- Real-time streaming: UI tails stdout.log/stderr.log with polling (200ms interval)
- Auto-scroll to bottom when new content arrives
- Displays combined stdout/stderr with node headers
- Throttled UI updates (100ms) to prevent excessive redraws

### Node Fields
- nodeId
- testId@version
- status
- duration
- retry count

---

## 3.3 History Page (Updated - Unified View)

### Purpose
- **Unified run browser and inspector** combining browsing and detailed diagnostics
- Browse and filter past runs in hierarchical tree view
- Inspect run details without navigation to separate page

### Layout (Master-Detail)
- **Left Panel (Master)**: Tree view of runs (550px default, resizable)
- **Center**: Drag splitter (16px)
- **Right Panel (Detail)**: Run inspector with tabbed views

---

### Master Panel: Run Tree

#### Data Source
- `Runs/index.jsonl`

#### Tree Structure
- **Hierarchical Display**: Plan → Suite → Case relationships
- **Visual Hierarchy**:
  - Expand/collapse buttons (chevron right/down icons)
  - Indentation based on depth (24px per level)
  - Tree lines connecting parent-child nodes

#### Display Columns
- **Status Icon**: Color-coded status indicator (18px)
- **Target**: Name + version badge (with expand control)
- **Type Icon**: Document/Folder/Board for Case/Suite/Plan (18px)
- **Start Time**: MM-dd / HH:mm format
- **Duration**: mm:ss format (monospace, with tooltip showing full hh:mm:ss)
- **Run ID**: Shortened 8-char display (with tooltip showing full RunId)

#### Filters (Top Card)
- Search box (searches RunId, DisplayName, TestId, SuiteId, PlanId)
- Status dropdown: ALL / Passed / Failed / Error / Timeout / Aborted
- RunType dropdown: ALL / TestCase / TestSuite / TestPlan
- Top-level only checkbox (hides nested runs when checked)
- Date range: Start Time From / Start Time To
- **Reset button**: Clears all filters
- **Refresh button**: Reloads run index

#### Tree Behavior
- **Expand/Collapse**: Click chevron or entire row
- **Selection**: Single-select, loads detail in right panel
- **Search Mode**: Auto-expands ancestors of matching nodes
- **Virtualization**: Flattened visible node list for performance

---

### Detail Panel: Run Inspector

#### No Selection State
- Center-aligned placeholder: "Select a run to view details"
- Document search icon

#### Inspector Header (When Selected)
- **Run ID Display**: Monospace, with copy button
- **Status Badge**: Color-coded (Passed/Failed/Error/etc.)
- **Action Buttons**: 
  - **Go to Source**: Navigate to Plan page and select the corresponding Case/Suite/Plan that was executed
  - **Open Folder**: Opens run directory in File Explorer
  - **Rerun**: Re-execute the same target

#### Detail View Selector (Radio Button Segmented Control)
- **5 Views**: Summary / Stdout / Stderr / Structured Events / Artifacts
- **UI Style**: Horizontal radio buttons styled as segmented pills
- **Binding**: Uses `EnumToBooleanConverter` and `EnumToVisibilityConverter`
- **Selection persists** across different run selections

---

### Detail View: Summary
- **Run Information Card** (expandable):
  - Status
  - Start Time (yyyy-MM-dd HH:mm:ss)
  - End Time
  - Duration (hh:mm:ss)
  - Exit Code
  - Error Message (if applicable)

### Detail View: Stdout
- **Terminal-styled viewer**: Monospace font (Cascadia Code, Consolas)
- Read-only TextBox with terminal background color
- Horizontal and vertical scrolling
- Full console output from `stdout.log`

### Detail View: Stderr
- **Terminal-styled viewer**: Same as Stdout
- Full error output from `stderr.log`

### Detail View: Structured Events
- **Event Table**: Virtualized list of events from `events.jsonl`
- **Columns**: Time, Level, Source, NodeId, Type, Message
- **Filters**:
  - Search box (searches message text)
  - Level multi-select (trace/debug/info/warning/error)
  - NodeId filter dropdown
  - Errors-only toggle
- **Event Details Panel**: Selected event shown as formatted JSON
- **Performance**: Streaming load with batched UI updates

### Detail View: Artifacts
- **Tree Browser**: Hierarchical file/folder view
- **File Preview**: Selected file content shown in viewer
- **Content Type Detection**: Text files shown with syntax, binaries show info
- **Quick Actions**: Open in external viewer, copy path

---

## 3.4 Logs & Results Page (Deprecated - Merged into History)

**NOTE**: This page specification is now obsolete. Its functionality has been consolidated into the History page's detail panel (Section 3.3).

The unified History page provides:
- Run browsing (formerly History page)
- Detailed diagnostics (formerly Logs & Results page)
- Master-detail layout eliminates navigation overhead

---

## 3.5 Settings Page (Updated)

### Purpose
- Configure tool-level defaults and behavior

### Visual Design (Updated)
- **Modern Card-Based Layout**: Organized settings in elevated cards with shadows
- **Grouped Sections**: Path Configuration, Appearance, Console, etc.
- **Improved Spacing**: Consistent padding and margins using design tokens
- **Inline Labels**: Left-aligned labels with right-aligned controls (180px label width)

### Settings Categories

#### Path Configuration Card
- Assets Root (browse button)
- Runs Directory (browse button)
- Runner Executable (browse button)

#### Appearance Card
- **Theme Dropdown**: Light / Dark
  - **Auto-Save Behavior**: Theme changes apply and save **immediately**
  - No manual save required for theme preference
  - Theme applies to entire application instantly
- Font Scale (future)
- Default Landing Page (future)

#### Console Card
- Show Console Window (checkbox)

#### Execution Settings (future)
- Default Timeout
- Max Retry Count
- Run Retention Days

### Special Behaviors

#### Theme Auto-Save
- **Immediate Application**: Theme applies without restart
- **Automatic Persistence**: Theme preference saved automatically
- **No Dirty State**: Theme changes don't set `HasChanges` flag
- **Independent of Other Settings**: Other setting changes still require manual save

#### Save/Discard Controls
- **Save Button**: Persists non-theme settings (paths, console, etc.)
- **Discard Button**: Reloads settings from disk
- **HasChanges Indicator**: Shows when non-theme settings modified
- **Theme exclusion**: Theme changes saved immediately, not part of save/discard flow

### Import / Export (future)
- Export settings (`settings.json`)
- Import settings (`settings.json`)

---

## 4. Import / Export Rules

### 4.1 File Types
- Test Case: `*.zip` (contains test.manifest.json and test files)
- Suite: `*.suite.json`
- Plan: `*.plan.json`

### 4.2 Behavior

#### Test Case Import
- Import from zip archive containing test.manifest.json
- Import flow:
  1. Select zip file
  2. Extract and validate manifest
  3. Check for identity conflicts (id@version)
  4. Check for name conflicts
  5. Copy to test case root directory
  6. Refresh discovery
- Validation rules:
  - Zip must contain exactly one test.manifest.json
  - Manifest must have valid id and version
  - No existing case with same identity (id@version)
  - No existing case with same name (case-insensitive)
  - Target folder (id) must not already exist

#### Suite and Plan Import
- File location is chosen by user each time
- Import flow:
  1. Parse
  2. Validate
  3. Add to list and select
- Export flow:
  - Export saved state only
  - Prompt to save if dirty

---

## 5. ViewModel and Service Boundaries (Updated)

### Services
- `IDiscoveryService`
- `ISuiteRepository`
- `IPlanRepository`
- `IRunService`
- `IRunRepository`
- `ISettingsService`
- `IFileDialogService`
- `IFileSystemService`
- `IThemeManager` *(new)* - Theme switching and management
- `INavigationService` - Page navigation coordination

### ViewModels
- `MainWindowViewModel` - Navigation state and status bar
- `PlanViewModel`
  - `CasesTabViewModel`
  - `SuitesTabViewModel`
  - `SuiteEditorViewModel`
  - `PlansTabViewModel`
  - `PlanEditorViewModel`
- `RunViewModel`
- `HistoryViewModel` *(updated)* - Unified tree view and inspector
  - `RunTreeNodeViewModel` - Tree node with expand/collapse
  - `RunIndexEntryViewModel` - Run metadata
  - `StructuredEventViewModel` - Event log entries
  - `ArtifactNodeViewModel` - File tree nodes
- ~~`LogsResultsViewModel`~~ *(removed - merged into HistoryViewModel)*
  - ~~`RunPickerViewModel`~~ *(removed)*
- `SettingsViewModel`

### New Value Converters
- `EnumToBooleanConverter` - For RadioButton binding to enum
- `EnumToVisibilityConverter` - Show/hide based on enum match
- `BoolToOpacityConverter` / `InverseBoolToOpacityConverter` - Opacity transitions
- `IsExpandedToIconConverter` - ChevronRight/ChevronDown based on expand state
- `DepthToIndentConverter` - Calculate left margin for tree indentation

---

## 6. MVP Acceptance Criteria (Updated)

- **Navigation**:
  - Compact icon-only sidebar with selection indicator
  - Keyboard accessible navigation
  - Settings button separated at bottom
- **Plan page**:
  - Segmented control tab navigation
  - Discover Cases
  - Create / edit / save Suites and Plans
  - Import / export Suite and Plan JSON
  - Inline editing with dirty-state handling
- **Run**:
  - Execute, Stop, Abort, live output
  - Real-time status updates
- **History** *(unified view)*:
  - Hierarchical tree navigation with expand/collapse
  - Master-detail layout with resizable panels
  - Filter by status, type, time range, search text
  - Detail view selector (Summary/Stdout/Stderr/Events/Artifacts)
  - Run inspector with all diagnostic views
  - Copy Run ID, refresh, reset filters
- **Settings**:
  - Card-based layout
  - Theme selection with immediate auto-save
  - Path configuration with browse dialogs
  - Save/Discard controls for non-theme settings

### Removed from Scope
- Separate "Logs & Results" page (consolidated into History)
- Run Picker dialog (no longer needed with unified History)

---

## 7. Visual Design System (New Section)

### 7.1 Theme Architecture

#### Theme Files
- `Themes/Light/Colors.Light.xaml` - Light theme color palette
- `Themes/Dark/Colors.Dark.xaml` - Dark theme color palette
- `Themes/Theme.Shared.xaml` - Common typography and spacing
- `Resources/DesignTokens.xaml` - Sizing and radius constants

#### Theme Manager
- Singleton service `IThemeManager`
- Runtime theme switching without restart
- Theme persistence via `ISettingsService`
- Event notification on theme change

#### Semantic Color Tokens
| Token | Purpose |
|-------|---------|
| `SurfaceBackgroundBrush` | Page background |
| `CardBackgroundBrush` | Elevated card surfaces |
| `NavBackgroundBrush` | Navigation sidebar |
| `ControlBackgroundSecondaryBrush` | Secondary control backgrounds |
| `PrimaryBrush` | Accent color (selection, buttons) |
| `TextPrimaryBrush` | Primary text |
| `TextSecondaryBrush` | Secondary text |
| `TextTertiaryBrush` | Tertiary/muted text |
| `BorderSubtleBrush` | Subtle borders |
| `InteractiveHoverBrush` | Hover state background |
| `InteractiveSelectedBrush` | Selection background |
| `TerminalBackgroundBrush` | Console output background |
| `TerminalTextBrush` | Console output text |

### 7.2 Component Styles

#### Segmented Control
- Used for: Tab navigation in Plan page
- Style: `SegmentedControlTabControl` / `SegmentedControlTabItem`
- Appearance: Pill-shaped buttons with rounded corners
- Selected state: Colored background

#### Segmented Radio Buttons
- Used for: Detail view selection in History page
- Style: `SegmentButtonStyle` (inline in HistoryPage.xaml)
- Appearance: Horizontal radio buttons styled as connected segments
- Selected state: Primary color fill

#### Compact Navigation Button
- Used for: Sidebar navigation
- Styles: `CompactNavButtonStyle` / `CompactNavButtonSelectedStyle`
- Size: 46x60px with 4px margin
- Selection indicator: 2px left accent bar
- Hover: Subtle background (no button-pressed appearance)

#### Card Layout
- Border with 1px `BorderSubtleBrush`
- Corner radius: `{StaticResource RadiusM}`
- Drop shadow: 8px blur, 2px depth, 0.3 opacity
- Padding: 20px

### 7.3 Design Tokens

#### Spacing Scale
| Token | Value | Usage |
|-------|-------|-------|
| `ControlMarginSmall` | `4,2` | Compact spacing |
| `ControlMarginMedium` | `8,4` | Standard control spacing |
| `ControlMarginLarge` | `12,6` | Generous spacing |
| `CardPadding` | `20` | Card interior padding |

#### Border Radius
| Token | Value |
|-------|-------|
| `RadiusS` | `4` |
| `RadiusM` | `8` |
| `RadiusL` | `12` |

#### Typography
| Token | Value |
|-------|-------|
| `FontSizeS` | `11` |
| `FontSizeM` | `13` |
| `FontSizeL` | `16` |
| `FontWeightSemiBold` | `600` |

### 7.4 Interaction Patterns

#### Tree Navigation
- Click chevron OR entire row to expand/collapse
- Single selection model
- Auto-expand ancestor chain when searching
- Visual indentation: 24px per level

#### Theme Switching
- Immediate visual update (no restart)
- Auto-save preference (no manual save button)
- Updates all windows and controls
- Syncs WPF-UI theme system

#### Filter Reset
- Single "Reset" button clears all filters
- Returns to default state (top-level only, no search, ALL status/type)
- Date pickers cleared

---

## 8. Parameter Editors: Enum/Bool Rendering Rules

### 8.1 Overview

The UI provides automatic parameter editor rendering based on parameter type metadata. The editor control (ComboBox, Toggle, TextBox, etc.) is selected at runtime via a `DataTemplateSelector` without requiring manual UI logic in code-behind or per-case XAML.

### 8.2 Type-Based Editor Selection

| Parameter Type | Conditions | Control | Description |
|---|---|---|---|
| `json` | `type: "json"` AND `enumValues` non-empty | **CheckBox list** (multi-select) | Multiple checkboxes allowing selection of multiple values from enumValues |
| `enum` | `EnumValues` list non-empty | **ComboBox** | Dropdown showing enumValues as selectable options |
| `boolean` | `UiHint` contains "checkbox" (case-insensitive) | **CheckBox** (plain) | Standard CheckBox with label |
| `boolean` | Default (no specific UiHint) | **CheckBox** (Toggle style) | Custom toggle switch appearance (40×20px track, animated thumb) |
| Other types | `string`, `int`, `double`, `path`, `json` (without enumValues), etc. | **TextBox** | Standard WPF-UI TextBox with placeholder |

### 8.3 Implementation Details

#### ParameterViewModel Extensions
- **IsMultiSelect**: `Type == "json" && EnumValues != null && EnumValues.Count > 0`
- **IsEnum**: `Type == "enum"`
- **EnumValues**: Exposes `Definition.EnumValues` list for ComboBox/CheckBox binding
- **IsBoolean**: `Type == "boolean"`
- **UsePlainCheckBox**: `IsBoolean && UiHint.Contains("checkbox")`
- **HasError** / **ErrorMessage**: Validation state properties

#### DataTemplateSelector
- **ParameterEditorTemplateSelector** selects appropriate DataTemplate
- **MultiSelectEditorTemplate**: ItemsControl with CheckBoxes (for json + enumValues)
- **EnumEditorTemplate**: ComboBox bound to `EnumValues` and `CurrentValue`
- **BooleanToggleTemplate**: CheckBox with `ToggleSwitchCheckBoxStyle`
- **BooleanCheckBoxTemplate**: Standard CheckBox
- **DefaultEditorTemplate**: WPF-UI TextBox

##MultiSelect: Uses `JsonArrayContainsConverter` (MultiBinding) for IsChecked state and `MultiSelectJsonBehavior` for updates
- ## Value Binding
- Enum: `SelectedItem="{Binding CurrentValue, Mode=TwoWay}"`
- Boolean: `IsChecked="{Binding CurrentValue, Converter={StaticResource BooleanStringConverter}}"`
  - Converter maps `"true"`/`"false"` strings ↔ `bool`
  - Supports `"true"`, `"false"`, `"1"`, `"0"` (case-insensitive)
- Other: `Text="{Binding CurrentValue, Mode=TwoWay}"`

### 8.4 Validation

Validation executes on `CurrentValue` change:
- **Required**: Non-empty when `Required=true`
- **Enum**: Value must be in `EnumValues` list (if provided)
- **Boolean**: Must be `true`/`false`/`1`/`0` (case-insensitive)

Validation errors display below the editor control with `PaletteDangerBrush` foreground. Invalid editors show red border (2px for ComboBox, adjusted BorderBrush for TextBox).

### 8.5 Toggle Switch Style

Custom CheckBox style (`ToggleSwitchCheckBoxStyle`) provides modern toggle appearance without third-party dependencies:
- **Track**: 40×20px rounded rectangle
- **Thumb**: 14×14px circle
- **Unchecked**: Gray track, text-colored thumb at left (X=0)
- **Checked**: Primary-colored track, white thumb at right (X=20)
- **Animation**: 150ms cubic-ease transition on thumb position

### 8.6 Usage in XAML

Parameters display using `ContentPresenter` with selector:

```xaml
<ContentPresenter Content="{Binding}"
                  ContentTemplateSelector="{StaticResource ParameterEditorTemplateSelector}"/>
```

No additional code required per parameter. Editor automatically adapts to type.

### 8.7 Design Rationale

- **Centralized**: Single DataTemplateSelector eliminates per-page duplication
- **Declarative**: XAML-driven, no code-behind switching logic
- **Extensible**: New types/editors added by extending selector and templates
- **Consistent**: All parameter editors share validation/error display patterns
- **Type-Safe**: Enum values from manifest prevent invalid input
- **Modern UX**: Toggle switch aligns with Windows 11 Fluent design language

---

**END OF UI SPEC v0.5**

