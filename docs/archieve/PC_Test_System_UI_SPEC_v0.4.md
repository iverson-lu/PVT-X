# PC Test System – UI Specification (v0.4)

> Based on PC Test System Core Spec v1.5.0  
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

---

## 2. Top-Level Information Architecture (Frozen)

### 2.1 Navigation (5 Items)

1. **Plan** – Asset and execution planning workspace  
2. **Run** – Execution and real-time monitoring  
3. **History** – Run index and rerun management  
4. **Logs & Results** – Single-run diagnostics  
5. **Settings** – Tool-level global configuration  

> History = find and manage runs  
> Logs & Results = inspect and diagnose a run

---

## 3. Page Specifications

## 3.1 Plan Page (Primary Workspace)

### 3.1.1 Purpose
- Central workspace for managing Cases, Suites, and Plans
- Entry point for asset discovery
- Create, edit, import, and export Suites and Plans
- Perform most Suite/Plan editing inline

### 3.1.2 Layout
- Left: Resource navigation (Tabs: Cases / Suites / Plans)
- Center: List view (search, filter, sort)
- Right: **Inline editor panel**

---

### 3.1.3 Cases Tab (Read-only)
- Display fields: name, id, version, tags, timeout, privilege
- Detail panel: manifest metadata, parameter definitions summary
- Actions:
  - **Discover…** (scan and refresh Case assets)
  - Open in Explorer

> Cases are not created in UI. They originate from discovered manifests.

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

### Layout
- Left: Node list (Suite or Plan execution)
- Right: Live output (Console / simplified Events)
- Top: Run control bar

### Node Fields
- nodeId
- testId@version
- status
- duration
- retry count

---

## 3.3 History Page

### Purpose
- Browse and filter past runs
- Rerun previous executions

### Data Source
- `Runs/index.jsonl`

### Features
- Filters: time range, status, testId, suiteId
- List fields: start time, target, status, duration
- Actions:
  - View Logs & Results
  - Open Run Folder
  - Rerun (reuse snapshots)

---

## 3.4 Logs & Results Page

### Purpose
- Deep diagnostics for a single run

### Entry Modes
- With `runId` (from Run / History): load directly
- Without `runId` (from navigation): show **Run Picker**

### Run Picker
- Recent runs (default 20)
- Search by runId / testId / suiteId
- Filter by time and status

---

### Diagnostic Layout
- Left: Artifacts tree
- Right: Viewer panel

### Common Artifacts
- result.json
- events.jsonl
- stdout.log / stderr.log
- manifest snapshot
- params.json / env.json

---

### Result Summary
- Overall status
- Failed node(s)
- Start / end / duration
- Failure reason (if available)
- Open Run Folder shortcut

---

### Structured Events Viewer

#### Event Table Columns
- Time
- Level
- Source
- NodeId
- Type
- Message

#### Filters
- Level multi-select
- NodeId
- Text search
- Errors-only toggle

#### Event Details Panel
- Raw JSON (foldable)
- Exception / stack trace
- File path quick open

#### Performance Requirements
- Streamed jsonl reading
- Batched UI updates
- Virtualized lists

---

## 3.5 Settings Page

### Purpose
- Configure tool-level defaults and behavior

### Suggested Settings
- Workspace and root paths
- Discover behavior (auto/manual)
- Default run policies (timeouts, retention)
- UI preferences:
  - Theme (Light / Dark / System)
  - Font scale
  - Default landing page

### Import / Export
- Export settings (`settings.json`)
- Import settings (`settings.json`)

---

## 4. Import / Export Rules

### 4.1 File Types
- Suite: `*.suite.json`
- Plan: `*.plan.json`

### 4.2 Behavior
- File location is chosen by user each time
- Import flow:
  1. Parse
  2. Validate
  3. Add to list and select
- Export flow:
  - Export saved state only
  - Prompt to save if dirty

---

## 5. ViewModel and Service Boundaries

### Services
- `IDiscoveryService`
- `ISuiteRepository`
- `IPlanRepository`
- `IRunService`
- `IRunRepository`
- `ISettingsService`
- `IFileDialogService`
- `IFileSystemService`

### ViewModels
- `PlanViewModel`
  - `CasesTabViewModel`
  - `SuitesTabViewModel`
  - `SuiteEditorViewModel`
  - `PlansTabViewModel`
  - `PlanEditorViewModel`
- `RunViewModel`
- `HistoryViewModel`
- `LogsResultsViewModel`
  - `RunPickerViewModel`
- `SettingsViewModel`

---

## 6. MVP Acceptance Criteria

- Plan page:
  - Discover Cases
  - Create / edit / save Suites and Plans
  - Import / export Suite and Plan JSON
  - Inline editing with dirty-state handling
- Run:
  - Execute, Stop, Abort, live output
- History:
  - Filter, rerun, navigate to diagnostics
- Logs & Results:
  - Run picker when entered directly
  - Structured events viewer
- Settings:
  - Paths and theme configuration

---

**END OF UI SPEC v0.4**
