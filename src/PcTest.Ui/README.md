# PcTest.Ui - PC Test System Desktop Application

A WPF desktop application for managing and executing PC test cases, suites, and plans.

## Features

### Navigation
- **Compact Icon-Only Sidebar**: Borderless 68px wide navigation panel
- **Visual Selection Indicator**: Left accent bar shows current page
- **Page Icons**: Plan (Clipboard), Run (Play), History (History), Settings (Settings)
- **Keyboard Navigation**: Tab-accessible navigation buttons
- **Contextual States**: Hover effects without button-pressed appearance

### Plan Page
- **Segmented Tab Control**: Modern pill-style tab navigation (Cases/Suites/Plans)
- **Cases Tab**: Browse discovered test cases (read-only)
- **Suites Tab**: Create, edit, delete, import/export test suites
- **Plans Tab**: Create, edit, delete, import/export test plans
- Inline editing with dirty state tracking
- Search and filter capabilities
- Improved spacing and layout consistency

### Run Page
- Execute individual test cases, suites, or plans
- Real-time status display for each node
- Live console output streaming
- Stop/Abort execution controls
- Quick access to logs after completion
- **Execution Pipeline Panel**: Shows queued nodes as Pending, updates status in real-time during execution

### History Page
- **Unified Run Browser**: Consolidated view combining run index and detailed inspection
- **Tree Navigation**: Hierarchical display showing Plan → Suite → Case relationships
  - Top-level runs shown by default
  - Expand/collapse nodes to show nested child runs
  - Visual indentation indicates hierarchy depth
- **Filtering**: By status, run type, time range, with text search across all fields
- **Master-Detail Layout**:
  - **Left Panel**: Tree list of runs with status icons, type badges, timestamps
  - **Right Panel**: Run inspector with tabbed detail views
- **Detail View Selection** (Radio Button Segmented Control):
  - **Summary**: Run information, status, timing, exit code
  - **Stdout**: Console output in terminal-styled viewer
  - **Stderr**: Error output in terminal-styled viewer
  - **Structured Events**: Filterable event log with JSON detail view
  - **Artifacts**: Tree browser with file preview
- **Quick Actions**: Copy Run ID, Refresh, Reset Filters

### Settings Page
- **Modern Card-Based Layout**: Organized in visual cards with drop shadows
- **Path Configuration**: Assets root, runs directory, runner executable
- **Appearance Settings**:
  - **Theme Selection**: Light/Dark mode with **immediate auto-save**
  - Theme changes apply instantly without requiring manual save
- **Console Settings**: Show/hide console window during execution
- **Save/Discard Controls**: For non-theme settings

## Technology Stack

- **.NET 10** (Windows Desktop)
- **WPF** with MVVM architecture
- **WPF-UI v3.0.5** for Windows 11 Fluent styling
- **CommunityToolkit.Mvvm** for MVVM infrastructure
- **Microsoft.Extensions.DependencyInjection** for DI
- **System.Text.Json** for JSON serialization

## Visual Design

### Theme System
- **Light and Dark Themes**: Full theme support with semantic color tokens
- **Dynamic Theme Switching**: Themes apply immediately without restart
- **Theme Manager Service**: Centralized theme management (`IThemeManager`)
- **Auto-Save**: Theme preference saved automatically on change
- **Color Tokens**: Defined in `Themes/Light/Colors.Light.xaml` and `Themes/Dark/Colors.Dark.xaml`
- **Shared Styles**: Common typography and spacing in `Themes/Theme.Shared.xaml`

### Design System
- **Modern Card UI**: Drop-shadow elevated cards for major content sections
- **Segmented Controls**: Pill-style tab navigation in Plan page
- **Compact Navigation**: Icon-only sidebar with selection accent bar
- **Fluent Typography**: Consistent font scales and weights
- **Design Tokens**: Centralized spacing, radius, and sizing values in `Resources/DesignTokens.xaml`

### Key UI Components
- **Tree View with Virtualization**: Efficient hierarchical run display in History
- **Radio Button Segmented Control**: Used for detail view selection
- **Status Icons and Badges**: Visual status indicators with color coding
- **Terminal-Styled Viewers**: Monospace console output with appropriate theming

## Building

```bash
# From solution root
dotnet build src/PcTest.Ui/PcTest.Ui.csproj

# Or build entire solution
dotnet build pc-test-system.sln
```

## Running

```bash
dotnet run --project src/PcTest.Ui/PcTest.Ui.csproj
```

## Running Tests

```bash
dotnet test tests/PcTest.Ui.Tests/PcTest.Ui.Tests.csproj
```

## Project Structure

```
src/PcTest.Ui/
├── App.xaml(.cs)           # Application entry point, DI setup
├── ViewModels/             # MVVM ViewModels
│   ├── ViewModelBase.cs    # Base classes with dirty state
│   ├── RelayCommand.cs     # ICommand implementations
│   ├── MainWindowViewModel.cs
│   ├── PlanViewModel.cs
│   ├── RunViewModel.cs
│   ├── HistoryViewModel.cs
│   ├── LogsResultsViewModel.cs
│   └── SettingsViewModel.cs
├── Views/                  # XAML Views
│   ├── MainWindow.xaml(.cs)
│   └── Pages/
│       ├── PlanPage.xaml(.cs)
│       ├── RunPage.xaml(.cs)
│       ├── HistoryPage.xaml(.cs)
│       ├── LogsResultsPage.xaml(.cs)
│       └── SettingsPage.xaml(.cs)
├── Services/               # Business logic services
│   ├── IDiscoveryService.cs
│   ├── ISuiteRepository.cs
│   ├── IPlanRepository.cs
│   ├── IRunService.cs
│   ├── IRunRepository.cs
│   ├── ISettingsService.cs
│   └── Implementations/
├── Resources/              # Converters, styles, design tokens
│   ├── Converters.cs       # Value converters (enum, bool, visibility, opacity)
│   ├── Styles.xaml         # Segmented controls, navigation buttons
│   └── DesignTokens.xaml   # Spacing, sizing, typography tokens
├── Themes/                 # Theme system
│   ├── Light/
│   │   └── Colors.Light.xaml
│   ├── Dark/
│   │   └── Colors.Dark.xaml
│   └── Theme.Shared.xaml
└── Services/               # Business logic services
    ├── IDiscoveryService.cs
    ├── ISuiteRepository.cs
    ├── IPlanRepository.cs
    ├── IRunService.cs
    ├── IRunRepository.cs
    ├── ISettingsService.cs
    ├── IThemeManager.cs
    ├── INavigationService.cs
    └── Implementations/
```

## Configuration

Settings are stored in `%APPDATA%/PcTest/settings.json`:

```json
{
  "assetsRoot": "assets",
  "runsDirectory": "Runs",
  "runnerExecutable": "",
  "theme": "Dark",
  "defaultTimeout": 300,
  "maxRetryCount": 3,
  "showConsoleWindow": false,
  "autoRefreshHistory": true
}
```

## Dependencies

The UI project references:
- **PcTest.Contracts** - Manifest models, enums, result types
- **PcTest.Engine** - Discovery service, test execution engine

## Development Notes

### MVVM Pattern
- All ViewModels inherit from `ViewModelBase` or `EditableViewModelBase`
- `EditableViewModelBase` provides dirty state tracking with `IsDirty` property
- Use `SetPropertyAndMarkDirty()` for properties that should mark editor dirty

### Testability
- Services are abstracted via interfaces for mockability
- `IFileDialogService` wraps system dialogs for unit testing
- `IFileSystemService` abstracts file system operations

### Execution Progress Reporting

The UI receives real-time updates during test execution through the `IExecutionReporter` interface, implementing the requirements defined in **Spec Section 10A (Execution UX and Runtime State Model)**.

#### Runtime Node States (per Spec 10A.3)

| State | Display | Description |
|-------|---------|-------------|
| Pending | "Pending" | Node queued but not started (Status=null, IsRunning=false) |
| Running | "Running..." | Node execution in progress (IsRunning=true) |
| Passed | "Passed" | Node completed successfully |
| Failed | "Failed" | Node completed with test failure |
| Error | "Error" | Node completed with execution error |
| Timeout | "Timeout" | Node exceeded time limit |
| Aborted | "Aborted" | Node terminated by user action or cascading abort |

#### Progress Events (per Spec 10A.4)

```csharp
public interface IExecutionReporter
{
    void OnRunPlanned(string runId, RunType runType, IReadOnlyList<PlannedNode> plannedNodes);
    void OnNodeStarted(string runId, string nodeId);
    void OnNodeFinished(string runId, NodeFinishedState nodeState);
    void OnRunFinished(string runId, RunStatus finalStatus);
}
```

**Event Flow:**
1. `OnRunPlanned` - Called when execution begins, provides list of nodes to display as Pending
2. `OnNodeStarted` - Called before each node executes, updates status to Running
3. `OnNodeFinished` - Called after each node completes, updates status to Passed/Failed/Error/etc.
4. `OnRunFinished` - Called when execution completes, sets final run status

#### Implementation Details

- `RunService` implements `IExecutionReporter` and converts callbacks to UI state updates
- `RunViewModel` maintains a dictionary of nodes for incremental updates (no collection rebuild per Spec 10A.6)
- Dispatcher is used to marshal updates to UI thread
- Pipeline nodes are displayed immediately when execution starts (per Spec 10A.2)
- Abort button remains accessible throughout execution (per Spec 10A.5)
- Progress is pushed from engine, not polled from artifacts (per Spec 10A.4)

### Virtualization
- Large lists (runs, events) use WPF virtualization
- `VirtualizingStackPanel.IsVirtualizing="True"` enabled on ListBoxes and DataGrids

## License

See repository root for license information.
