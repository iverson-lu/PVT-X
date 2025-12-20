# PC Test System Specification
## Version 1.2 (Revised)

> This version refines v1.1 based on engineering review.
> The focus is **protocol clarity, contract authority, and long-term maintainability**.
> No new features are introduced; only rules are clarified and frozen.

---

## 1. Project Goal

Develop a **local PC test system running on the target machine**, used to:
- Execute automated hardware and system tests
- Standardize test discovery, parameter configuration, execution, logging, and results
- Deliver test cases as **Manifest + PowerShell scripts**

The system must provide:
- Stable and explicit contracts (Manifest / Result / Folder Layout)
- Clear responsibility boundaries
- Forward-compatible evolution strategy
- Engineering-grade observability and debuggability

---

## 2. Technology Stack

### 2.1 Platform
- Windows 11

### 2.2 Runtime & Languages
- UI: WPF, `net10.0-windows`
- Engine / Runner / Core: `net10.0`
- Script Runtime: PowerShell 7+ (`pwsh.exe`)

> Notes:
> - Always target the latest .NET LTS.
> - PowerShell 5.x is explicitly **not supported**.

### 2.3 Repository Structure

```
PVT-X/
  README.md
  docs/
    SPEC.md
    architecture.md
    security.md
    schema/
      manifest.schema.json
      result.schema.json

  src/
    PcTest.UI/                   # WPF app (net10.0-windows)
      PcTest.UI.csproj
      App.xaml
      Views/
      ViewModels/
      Resources/
      ...

    PcTest.Runner/               # runner host (net10.0) - can be console or service-like
      PcTest.Runner.csproj
      RunnerHost.cs              # entry / API facade
      Process/
        PowerShellLocator.cs      # pwsh discovery + version check
        PowerShellRunner.cs       # spawn + capture logs + kill tree
      Storage/
        RunFolderWriter.cs        # create run folder + write index.jsonl
      ...

    PcTest.Engine/               # orchestration (net10.0)
      PcTest.Engine.csproj
      Execution/
        TestExecutor.cs           # load manifest + validate + call runner
      Validation/
        ManifestValidator.cs
        ParameterBinder.cs
      ...

    PcTest.Contracts/            # shared DTOs (net10.0)
      PcTest.Contracts.csproj
      Manifest/
        ManifestModels.cs
      Result/
        ResultModels.cs
      Serialization/
        Json.cs

    PcTest.Cli/                  # optional: CLI runner for CI or lab automation (net10.0)
      PcTest.Cli.csproj

  assets/
    TestCases/
      CpuStress/
        test.manifest.json
        run.ps1
        README.md

  runs/                          # default output folder (gitignored)
    .gitkeep

  tools/
    signing/                     # optional: script signing helpers
    packaging/                   # optional: msix/installer scripts

  .gitignore
  Directory.Build.props
  pc-test-system.sln
```

Rules:
- The repository structure is normative for reference implementation only.
- Runtime behavior MUST NOT depend on physical source tree layout.
- Only the following runtime paths are considered stable contracts:
  - Test Case root folders
  - Run output folder (Runs/)
  - All other directories (src/, tools/, docs/) are non-runtime and MUST NOT be accessed by Runner or scripts at execution time.

---

## 3. System Architecture

```
[ UI (WPF) ]
      |
      v
[ Engine ]
      |
      v
[ Runner ]
      |
      v
[ PowerShell Script ]
```

### 3.1 UI Responsibilities
- Test discovery and metadata display
- Parameter input and validation
- Execution control (start / stop)
- Log and result visualization
- Run history browsing

### 3.2 Engine Responsibilities
- Load and validate Manifest
- Bind parameters
- Enforce privilege policy
- Orchestrate execution lifecycle

### 3.3 Runner Responsibilities (Authoritative)
- Discover and validate `pwsh.exe`
- Enforce execution rules and timeout
- Create Run Folder
- Launch and terminate process tree
- Capture stdout / stderr
- Generate **authoritative result.json**
- Record execution environment

### 3.4 Execution Authority

Runner is the final authority for:
- Process lifecycle
- Timeout enforcement
- Result generation

Engine must not override Runner execution decisions.

---

## 4. Test Case Layout

```
TestCases/
  CpuStress/
    test.manifest.json
    run.ps1
    README.md (optional)
```

Rules:
- Test case folders are immutable during execution
- Discovery is read-only
- All outputs must be written to the Run Folder

Test Case Root Resolution:
- Engine MUST resolve an absolute Test Case Root directory before discovery.
- Discovery MUST be recursive under the resolved root.
- Test Case Root path is implementation-defined (config / CLI / UI), but MUST be treated as read-only at runtime.

---

## 5. Manifest Specification (`test.manifest.json`)

### 5.1 Top-Level Fields

| Field | Type | Required | Description |
|---|---|---|---|
| schemaVersion | string | Yes | Manifest schema version |
| id | string | Yes | Globally unique test ID |
| name | string | Yes | Test name |
| category | string | Yes | Test category |
| description | string | No | Description |
| version | string | Yes | Test version |
| privilege | enum | No | User / AdminPreferred / AdminRequired |
| timeoutSec | int | No | Execution timeout (seconds) |
| tags | string[] | No | Tags |
| parameters | array | No | Parameter definitions |

ID and Folder Rules:
- `id` must be globally unique across all test cases
- Multiple versions of the same `id` are allowed but must not coexist in the same folder
- Test Case folder name is NOT part of the test identity.
- Only `id` + `version` define the test identity.
- Folder renaming MUST NOT affect test identity or history.

---

### 5.2 Parameter Definition

| Field | Type | Required | Description |
|---|---|---|---|
| name | string | Yes | Parameter name |
| type | string | Yes | string / int / double / bool / enum / path / file / folder / string[] / int[] / enum[] |
| required | bool | Yes | Required or not |
| default | any | No | Default value |
| min / max | number | No | Numeric range |
| enumValues | array | No | Enum candidates |
| unit | string | No | Display unit |
| uiHint | string | No | textbox / dropdown / checkboxes / filePicker / folderPicker / multiline |
| pattern | string | No | Regex validation |
| help | string | No | Help text |

---

## 6. Parameter Passing Protocol (Frozen)

All parameters **MUST** be passed as named PowerShell parameters.

Rules:
- Format: `-<Name> <Value>`
- Arrays use PowerShell array literals: `@("A","B")`
- Boolean values: `$true` / `$false`
- Paths and strings are properly quoted by Runner
- Missing optional parameters are omitted (not passed as null)

Example:
```powershell
pwsh.exe run.ps1 -DurationSec 30 -Modes @("A","B")
```

This protocol is **non-breaking and immutable** within schema v1.x.

---

## 7. PowerShell Execution Rules

- Runner invokes `pwsh.exe`
- Version must be >= 7.0
- Working directory is the Run Folder
- Script must be treated as untrusted code

### 7.1 Exit Code Convention

| Exit Code | Meaning |
|---|---|
| 0 | Success |
| 1 | Test Failed |
| 2 | Script Error |
| 3 | Timeout / Aborted |

Exit codes are **advisory**; final status is determined by Runner.

### 7.2 Trust Boundary:

- Scripts run with the same OS privileges as Runner.
- Scripts MUST be treated as untrusted input.
- Runner MUST NOT assume cooperative script behavior.
  
---

## 8. Run Folder Layout

```
Runs/
  index.jsonl
  {RunId}/
    manifest.json
    params.json
    stdout.log
    stderr.log
    events.jsonl
    env.json
    result.json
```

### 8.1 Run Folder Ownership Rules:
- The Run Folder is exclusively owned by Runner.
- Scripts MUST NOT create, delete, or rename top-level files in the Run Folder.
- Scripts MAY write additional files only under:
    {RunId}/artifacts/
- Runner MUST ignore unknown files and folders.

### 8.2 Immutability Rules:
- manifest.json and params.json are snapshots generated by Engine/Runner.
- Scripts MUST NOT modify these files.
- Runner MAY validate immutability for debugging purposes.

### 8.3 index.jsonl
- One JSON object per line
- Fields: runId, testId, startTime, endTime, status

### 8.4 env.json
Records execution environment snapshot:
- OS version
- Runner version
- PowerShell version
- Elevation state

### 8.5 events.jsonl (Optional):
- Each line is a JSON object representing a timestamped event.
- Event producers may include Runner or Script.
- The schema is intentionally unspecified in v1.x.
- Consumers MUST treat events as best-effort diagnostic data only.

---

## 9. Result Specification (`result.json`)

### 9.1 Authority Rule (Critical)

> **Runner is the sole authority of `result.json`.**  
> Script-generated results are treated as input and must be validated.

### 9.2 Top-Level Fields

| Field | Type | Required | Description |
|---|---|---|---|
| schemaVersion | string | Yes | Result schema version |
| testId | string | Yes | Test ID |
| status | enum | Yes | Passed / Failed / Error / Timeout |
| startTime | string | Yes | ISO8601 |
| endTime | string | Yes | ISO8601 |
| metrics | object | No | Metrics |
| message | string | No | Summary |
| exitCode | int | No | Script exit code |
| error | object | No | Error details |
| runner | object | No | Runner metadata |

### 9.3 Error Object

| Field | Description |
|---|---|
| type | Timeout / ScriptError / RunnerError |
| source | Script / Runner |
| message | Human-readable |
| stack | Optional stack trace |

### 9.4 Result Priority Rules

1. Valid `result.json` generated by Runner is authoritative
2. If script fails or crashes, Runner generates fallback result
3. Exit code alone must never override Runner decision

---

## 10. Privilege Policy

| privilege | Behavior |
|---|---|
| User | Run as standard user |
| AdminPreferred | Warn if not elevated |
| AdminRequired | Block execution if not elevated |

Privilege enforcement is performed by Engine before execution.

---

## 11. Concurrency & Isolation

- v1 executes tests sequentially
- Runner must support:
  - Hard timeout enforcement
  - Full process tree termination
- Reserved for future:
  - `exclusive`
  - `requiredResources`

---

## 12. Compatibility Strategy

- Runner must support schemaVersion v1.x
- Unknown fields must be ignored
- Breaking changes require schemaVersion v2.0

---

## 13. Out of Scope (v1.x)

- Distributed execution
- Network scheduling
- Web UI

---

END OF SPEC v1.2
