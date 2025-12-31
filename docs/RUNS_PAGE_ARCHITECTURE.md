# Runs Page - Architecture & Data Flow

## Component Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         RunsPage.xaml                            │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │                     Filter Bar                            │  │
│  │  [Search] [Status▼] [Type▼] [☑Top-level] [📅][📅]       │  │
│  │  [🔄 Refresh] [✕ Clear]                                   │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                   │
│  ┌──────────────────┬──────────────────────────────────────────┐│
│  │  Run List        │  Run Inspector                           ││
│  │  (Master)        ║  (Detail)                                ││
│  │                  ║                                          ││
│  │ ┌──────────────┐ ║ ┌──────────────────────────────────┐   ││
│  │ │ RunId │ Type │ ║ │ Header: [RunId] [Badge]          │   ││
│  │ │ Target│Status│ ║ │         Target: ...              │   ││
│  │ │ Start │ Dur  │ ║ │         [🗂 Open Folder]         │   ││
│  │ │──────────────│ ║ └──────────────────────────────────┘   ││
│  │ │ ... 500 rows │ ║                                         ││
│  │ │ ... filtered │ ║ ┌──────────────────────────────────┐   ││
│  │ │ ... virtual  │ ║ │ TabControl                       │   ││
│  │ │ ... scrolling│ ║ │ ┌─────────┬────────┬──────────┐ │   ││
│  │ └──────────────┘ ║ │ │Summary  │Stdout  │Stderr    │ │   ││
│  │                  ║ │ │Events   │Artifacts│          │ │   ││
│  │ [Selected Row]   ║ │ └─────────┴────────┴──────────┘ │   ││
│  │ → Triggers       ║ │                                  │   ││
│  │   Details Load   ║ │ [Tab-specific content here]     │   ││
│  │                  ║ │                                  │   ││
│  │                  ║ │ (All tabs populated on select)  │   ││
│  └──────────────────┴──────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘

DataContext: RunsViewModel
```

---

## ViewModel Data Flow

```
RunsViewModel
├─ Master (Run List)
│  ├─ _allRuns: List<RunIndexEntryViewModel>
│  ├─ Runs: ObservableCollection<RunIndexEntryViewModel> (filtered)
│  ├─ SelectedRun: RunIndexEntryViewModel?
│  ├─ SearchText: string
│  ├─ Filters: Status, Type, DateRange, TopLevelOnly
│  │
│  └─ Commands:
│     ├─ RefreshCommand → LoadAsync()
│     └─ ClearFiltersCommand → Reset all filters
│
└─ Detail (Run Inspector)
   ├─ RunDetails: RunDetails?
   ├─ StdoutContent: string
   ├─ StderrContent: string
   ├─ Events: ObservableCollection<StructuredEventViewModel>
   ├─ Artifacts: ObservableCollection<ArtifactNodeViewModel>
   │
   └─ Commands:
      ├─ OpenFolderCommand → Explorer
      ├─ RefreshEventsCommand → Reload events
      └─ ClearEventFiltersCommand → Reset filters

Selection Flow:
  User clicks run in list
    ↓
  SelectedRun property changes
    ↓
  OnSelectedRunChanged() fires
    ↓
  LoadRunDetailsAsync(runId) called
    ↓
  Parallel loading:
    - LoadArtifactsAsync()
    - LoadStdoutStderrAsync()
    - LoadEventsAsync()
    ↓
  Inspector UI updates
    ↓
  All tabs ready instantly
```

---

## Data Source Integration

```
RunsViewModel
    ↓
    Uses:
    
IRunRepository
├─ GetRunsAsync(filter) → List<RunIndexEntry>
├─ GetRunDetailsAsync(runId) → RunDetails
├─ GetArtifactsAsync(runId) → List<ArtifactInfo>
├─ ReadArtifactAsync(runId, path) → string
├─ StreamEventsAsync(runId) → IAsyncEnumerable<EventBatch>
└─ GetRunFolderPath(runId) → string

IFileSystemService
├─ FileExists(path) → bool
├─ DirectoryExists(path) → bool
├─ ReadAllTextAsync(path) → string
└─ OpenInExplorer(path) → void

IFileDialogService
├─ ShowError(title, message)
└─ ShowWarning(title, message)

INavigationService
├─ NavigateTo(page, param)
└─ CurrentParameter → object?
```

---

## Navigation Flow

```
Old Architecture:
┌────────────┐      ┌─────────────────┐
│  History   │─────▶│ Logs & Results  │
│  (List)    │      │  (Details)      │
└────────────┘      └─────────────────┘
   User selects run → Navigate to new page
   
New Architecture:
┌─────────────────────────────────────┐
│            Runs                      │
│  ┌──────────┬────────────────────┐  │
│  │  List    │     Inspector      │  │
│  │          │     (Details)      │  │
│  └──────────┴────────────────────┘  │
└─────────────────────────────────────┘
   User selects run → Inspector updates
   (No navigation, same page)
```

---

## State Management

```
Initial State:
  Runs = []
  SelectedRun = null
  RunDetails = null
  → Show: Empty list + "Select a run" placeholder

After LoadAsync():
  Runs = [500 recent runs]
  SelectedRun = null
  RunDetails = null
  → Show: Populated list + "Select a run" placeholder

After User Selects Run:
  Runs = [500 recent runs]
  SelectedRun = runs[5]
  RunDetails = null (loading...)
  → Show: Selected row + Loading indicator

After LoadRunDetailsAsync():
  Runs = [500 recent runs]
  SelectedRun = runs[5]
  RunDetails = { ... full data ... }
  → Show: Selected row + Populated inspector

After User Selects Different Run:
  Previous RunDetails cleared
  New LoadRunDetailsAsync() triggered
  Inspector switches to new run
  → No page reload, smooth transition
```

---

## Performance Characteristics

**Run List:**
- Initial load: ~500 runs
- Query time: ~100-300ms
- Rendering: Virtualized DataGrid (renders only visible rows)
- Filtering: Client-side (fast, no server round-trip)
- Search: In-memory LINQ queries

**Run Inspector:**
- Details load: ~200-500ms per run
- Stdout/stderr: Lazy-loaded from disk
- Events: Streamed in batches
- Artifacts: Tree built in-memory
- Tab switching: Instant (data pre-loaded)

**Memory Usage:**
- Run list: ~10KB per entry × 500 = ~5MB
- Selected run details: ~1-5MB depending on logs/events
- Artifacts: ~100KB for tree structure
- Total estimate: ~10-15MB typical, ~50MB worst case

---

## Integration Points

### From Other Pages:
```csharp
// From Run page after execution completes
_navigationService.NavigateTo("Runs", runId);
// → Opens Runs page with specific run selected

// From any page with a run reference
_navigationService.NavigateToLogsResults(runId);
// → Redirects to Runs page (backward compatible)
```

### Backward Compatibility:
```csharp
NavigationService.cs:
  "History" → redirects to "Runs"
  "LogsResults" → redirects to "Runs"
  
Old pages preserved but unused in navigation menu
```

---

## Error Handling

```
RunsViewModel Error Scenarios:

1. No runs found
   → Show empty list with message
   
2. Run details load fails
   → Show error dialog
   → Inspector remains on previous run (if any)
   
3. Stdout/stderr file missing
   → Show "(No stdout.log found)" placeholder
   
4. Events file missing
   → Show "(No structured events found)" placeholder
   
5. Artifact load fails
   → Show error message in content viewer
   
6. Filter produces no results
   → Show empty filtered list
   → "Clear filters" button available
```

---

## User Workflows

### Primary Workflow: Browse and Inspect
```
1. User navigates to "Runs"
2. List loads with recent 500 runs
3. User applies filters (optional)
4. User selects run from list
5. Inspector loads and displays in right panel
6. User switches between tabs as needed
7. User selects different run (back to step 4)
```

### Workflow: Search for Specific Run
```
1. User navigates to "Runs"
2. User types in search box
3. List filters in real-time
4. User selects matching run
5. Inspector displays run details
```

### Workflow: Investigate Failures
```
1. User navigates to "Runs"
2. User filters Status = Failed
3. User scans list for recent failures
4. User selects failed run
5. User checks Stderr tab for errors
6. User checks Events tab (Errors only)
7. User opens folder for full investigation
```

---

## Testing Strategy

### Unit Tests (Recommended)
- `RunsViewModel_LoadAsync_LoadsRuns`
- `RunsViewModel_SelectRun_LoadsDetails`
- `RunsViewModel_ApplyFilter_FiltersCorrectly`
- `RunsViewModel_Search_FindsMatches`

### Integration Tests
- Full page load with real data
- Run selection → details load
- Tab switching → data displayed
- Filter interactions

### Manual Tests
- Visual inspection of layout
- Splitter resizing
- Large run list (500+ entries)
- Runs with large logs (100MB+)
- Runs with many events (10,000+)

---

## Deployment Checklist

- [x] New files created
- [x] Navigation service updated
- [x] DI registration complete
- [x] Build successful
- [x] Backward compatibility verified
- [ ] Manual testing in dev environment
- [ ] User acceptance testing
- [ ] Documentation updated
- [ ] Old pages removed (optional, later)

---

**Architecture Status:** ✅ Implemented and Building
