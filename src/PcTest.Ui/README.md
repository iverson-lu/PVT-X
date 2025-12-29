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

### Virtualization
- Large lists (runs, events) use WPF virtualization
- `VirtualizingStackPanel.IsVirtualizing="True"` enabled on ListBoxes and DataGrids

## License

See repository root for license information.
