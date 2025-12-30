# PcTest.Ui - PC Test System Desktop Application

A WPF desktop application for managing and executing PC test cases, suites, and plans.

## Features

### Plan Page
- **Cases Tab**: Browse discovered test cases (read-only)
- **Suites Tab**: Create, edit, delete, import/export test suites
- **Plans Tab**: Create, edit, delete, import/export test plans
- Inline editing with dirty state tracking
- Search and filter capabilities

### Run Page
- Execute individual test cases, suites, or plans
- Real-time status display for each node
- Live console output streaming
- Stop/Abort execution controls
- Quick access to logs after completion
- **Execution Pipeline Panel**: Shows queued nodes as Pending, updates status in real-time during execution

### History Page
- Browse all previous test runs
- Filter by status, run type, time range
- Quick actions: View Logs, Open Folder, Rerun

### Logs & Results Page
- Run Picker for selecting runs to inspect
- Result summary with execution details
- Artifacts tree browser
- Stdout/Stderr log viewers
- Structured Events Viewer with:
  - Event filtering by level and type
  - Search functionality
  - JSON detail view
  - Virtualized list for large event streams

### Settings Page
- Configure paths (assets root, runs directory, runner executable)
- Theme selection (Dark/Light)
- Execution settings
- Import/Export settings as JSON

## Technology Stack

- **.NET 10** (Windows Desktop)
- **WPF** with MVVM architecture
- **WPF-UI v3.0.5** for Windows 11 Fluent styling
- **CommunityToolkit.Mvvm** for MVVM infrastructure
- **Microsoft.Extensions.DependencyInjection** for DI
- **System.Text.Json** for JSON serialization

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
└── Resources/              # Converters, styles
    └── Converters.cs
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
