# Test Case Authoring Rules v1

> Unified authoring rules for **all Test Cases** in the PVT-X PC Test Suite.
>
> Goals:

- Ensure consistency, maintainability, and scalability of Test Cases
- **Improve readability and understandability of Test Case execution results**
- Enable both humans and AI to reliably produce high-quality Test Cases

---

## 1. Case Directory Structure (Mandatory)

Each Test Case **must contain at least** the following files:

```
CaseName/
 ├─ test.manifest.json   (required)
 ├─ run.ps1              (required)
 ├─ README.md            (strongly recommended)
 └─ <other files>        (optional, e.g. test data)
```

### Rules

- `test.manifest.json` and `run.ps1` are **mandatory**
- `README.md` is **strongly recommended** to describe test purpose and logic
- A Case may include additional data files (configs, samples, reference data, etc.)
- All bundled files must be accessed via `PVTX_TESTCASE_PATH` + relative paths

- ❌ Do NOT pre-commit:
  - `artifacts/`
- Runner / Engine may generate additional directories or files during execution
- These artifacts are **not part of the Test Case design**

### Case Execution Outputs

- **Console output** during execution is the most basic feedback
- It is **strongly recommended** that a Case produces:
  - `report.json` (describing the Case’s own execution result)
- Producing other files is completely optional
- Pass / Fail is determined by the **exit code**, not by output artifacts
- The directory name is used only for physical organization and loading
- The **only stable and unique identifier** is the ID

---

## 2. test.manifest.json Specification

### 2.1 Meta Information

#### ID Rules (Mandatory)

- Every **Case / Suite / Plan** must define a **globally unique, stable ID**
- IDs follow **reverse-domain style naming** with controlled semantic layers
- Case / Suite / Plan share a **single global ID namespace**, distinguished by prefixes

Examples:

```
case.hw.power.core.sleep_resume
case.sw.os.windows.service_status
suite.fw.bios_ver_check
plan.sw.smoke_test
```

##### Core Design Principles (Summary)

> A complete and scalable ID structure is the foundation of long-term system governance.

- ID semantics are split into **fixed layers** to avoid ambiguity as the library grows
- Recommended structure (Case example):
  ```
  case.<domain>.<subsystem>.<feature>.<action>
  ```
- Layer responsibilities:
  - `domain`: top-level technical area (hw / sw / fw / os / network)
  - `subsystem`: major functional block (power / storage / wifi)
  - `feature`: specific capability or focus area (use `core` when not applicable)
  - `action`: test intent or behavior (check / verify / stress, etc.)

> Notes:
> - This is a **recommended and governed standard** for scalability, UI grouping, and analytics
> - Full definitions, whitelists, and constraints are defined in: `pvtx-id-schema.md`

##### Rules

- IDs **must be globally unique** and **must not change once published**
- Case / Suite / Plan use unified prefixes:
  - `case.*`
  - `suite.*`
  - `plan.*`
- Directory names **should follow `id@version` format** (e.g., `case.hw.cpu.core.check@1.0.0`) to match manifest id and version. Discovery reads manifests, not directory names; this convention helps distinguish versions and maintain consistency.
- IDs are **decoupled from UI display names**
- IDs must appear in:
  - `test.manifest.json` (meta section)
  - `report.json` produced by the Case

---

### 2.2 Meta Fields

#### Required fields

```json
{
  "name": "Display Name",
  "description": "...",
  "category": "...",
  "tags": ["..."],
  "privilege": "User | AdminPreferred | AdminRequired"
}
```

### Rules

- `name` **does not need to match** the directory name
- `description`: short description for UI display
- `category`: logical grouping for Suite / Plan / UI
  - Recommended values: `Hardware` / `Software` / `Firmware`
- `tags`:
  - Used **only for classification and filtering**
- `privilege` (optional, defaults to `User`):
  - `User`: No special privileges required
  - `AdminPreferred`: Test prefers admin rights but can run without (shows warning)
  - `AdminRequired`: Test must run as administrator (blocks execution if not elevated)
  - Suite/Plan privilege is computed as max(child privileges)

---

### 2.3 Parameters Definition

Each parameter supports the following fields:

```json
{
  "name": "ParamName",
  "type": "string | enum | boolean | int | double | json",
  "required": true,
  "default": "...",
  "description": "..."
}
```

---

## 3. README.md Specification (Mandatory Structure)

All Test Case READMEs **must follow this exact structure and order**:

```md
# Case Name

## Purpose
One sentence describing what this test validates.

## Test Logic
- Core test approach
- System capabilities / commands / APIs used
- Pass / fail decision logic

## Parameters
- Semantic explanation of manifest parameters

## How to Run Manually
Provide an example command to run `run.ps1` directly for debugging:

```powershell
pwsh ./run.ps1 -ParamA valueA -ParamB valueB
```

## Expected Result
- Expected behavior on success
- Typical behavior on failure
```
```

---

## 4. run.ps1 Specification

### 4.0 System-Level Environment Variables

Injected automatically by Runner:

| Variable | Description |
|--------|-------------|
| `PVTX_TESTCASE_PATH` | Absolute path of the Test Case |
| `PVTX_TESTCASE_NAME` | Test Case display name |
| `PVTX_TESTCASE_ID` | Unique execution ID |
| `PVTX_TESTCASE_VER` | Test Case version identifier |
| `PVTX_ASSETS_ROOT` | Absolute path to the assets root directory |
| `PVTX_MODULES_ROOT` | Absolute path to PowerShell\Modules directory |
| `PVTX_RUN_ID` | RunId for this Test Case execution |
| `PVTX_PHASE` | Reboot resume phase (0 = initial execution) |
| `PVTX_CONTROL_DIR` | Control directory for reboot requests |

- `PVTX_PHASE` is `0` on the first execution; if a reboot is requested and resumed, it is set to `nextPhase`.
- `PVTX_CONTROL_DIR` is created by the Runner; only control files should be written there.

> Plan- and Suite-level environment variable injection is also supported.

### 4.1 Shared PowerShell Modules

Runner automatically configures PowerShell module discovery to enable Test Cases to use shared helper modules:

- Shared PowerShell modules SHOULD be placed under `assets/PowerShell/Modules/`
- Runner prepends `PVTX_MODULES_ROOT` to the `PSModulePath` environment variable
- Test Cases MAY import shared modules using standard PowerShell syntax: `Import-Module <ModuleName>`
- Modules are auto-discovered; no explicit path specification is required
- Shared modules MAY provide utilities for:
  - Hardware information collection
  - System state validation
  - Logging and output formatting
  - Common test operations
  - Data processing and reporting helpers

Example usage in run.ps1:
```powershell
# Import a shared helper module
Import-Module PvtxCommon

# Use module functions
$hwInfo = Get-HardwareInfo
Write-TestLog "Hardware detected: $($hwInfo.Model)"
```

> Note: The specific modules and functions available will evolve over time. Refer to the modules under `assets/PowerShell/Modules/` for current offerings.

---

### 4.2 Exit Codes

| Code | Meaning |
|-----|--------|
| 0 | Pass |
| 1 | Fail |
| ≥2 | Script / Environment Error |

### 4.3 Reboot / Resume Protocol

Some cases require a reboot to validate resume behavior. Use the control channel rather than calling Restart-Computer directly.

Rules:
- Treat PVTX_PHASE=0 as the initial phase.
- When a reboot is required, write reboot.json into PVTX_CONTROL_DIR and exit 0.
- reboot.json MUST include type, nextPhase, and a non-empty reason; unknown fields are rejected.
- Do not request more than one reboot in a single run (PVTX_PHASE > 0 must complete).
- Store any state needed across reboot under the Case Run Folder (prefer `artifacts/`); do not write to the Test Case source folder.
- After resume, PVTX_PHASE is set to nextPhase; complete the remaining phases and exit with the final pass/fail code.

Example:
```powershell
$phase = 0
if ($env:PVTX_PHASE) {
    $phase = [int]$env:PVTX_PHASE
}

if ($phase -eq 0) {
    if ([string]::IsNullOrWhiteSpace($env:PVTX_CONTROL_DIR)) {
        throw "PVTX_CONTROL_DIR is required for reboot control."
    }

    $payload = @{
        type = "control.reboot_required"
        nextPhase = 1
        reason = "Phase 0 complete; request reboot."
        reboot = @{ delaySec = 10 }
    } | ConvertTo-Json -Depth 5

    $tmpPath = Join-Path $env:PVTX_CONTROL_DIR "reboot.tmp"
    $finalPath = Join-Path $env:PVTX_CONTROL_DIR "reboot.json"

    $payload | Set-Content -Path $tmpPath -Encoding UTF8
    Move-Item -Path $tmpPath -Destination $finalPath -Force

    exit 0
}

# Phase 1 work...
exit 0
```


---

## 5. Template Positioning

- **TemplateCase**: full-featured reference
- **TemplateCaseMinimal**: recommended starting point

---

## 6. Design Principles

- ID defines **identity**
- Manifest defines **structure**
- README defines **intent**
- Script defines **execution**

---

**Version**: v1  
**Status**: Frozen
