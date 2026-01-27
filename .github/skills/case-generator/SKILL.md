---
name: pvt-x-case-generator
description: Generate a new PVT-X test case (run.ps1, test.manifest.json, README.md) based on existing reference cases and a provided case spec.
version: 1.0
---

## Purpose

This skill generates a **new test case** for the PVT-X PC test suite by
**strictly following existing reference cases in the repository**.

The output of this skill is always a **3-file case bundle**:

- `run.ps1`
- `test.manifest.json`
- `README.md`

This skill is designed to ensure **structural consistency**, **parameter compatibility**,
and **stable runner integration** across all cases.

---

## When to Use This Skill

Use this skill when:

- You are adding a **new test case**
- You already have a **case specification** (markdown or text)
- You want the new case to **match existing cases exactly** in structure and behavior
- The implementation language is **PowerShell**

Do NOT use this skill for:
- Refactoring existing cases
- Changing the runner execution model
- Introducing new manifest schemas

---

## Inputs

This skill expects:

1. **A case specification**
   - Plain text or markdown
   - Describes purpose, parameters, execution logic, and pass/fail rules

2. **Existing reference cases in the workspace**
   - **TemplateCaseMinimal**: recommended starting point for simple cases
   - **TemplateCase**: full-featured reference for complex cases
   - Located under `assets/TestCases/`
   - Each reference case contains:
     - `run.ps1`
     - `test.manifest.json`
     - `README.md`

---

## Outputs

This skill produces exactly **three files** for the new case:

```
assets/TestCases/<case-id@version>/
  run.ps1
  test.manifest.json
  README.md
```

**Directory Naming Convention:**
- Directory names **should follow `id@version` format** (e.g., `case.hw.cpu.core.check@1.0.0`)
- This matches the manifest id and version for consistency
- Discovery reads manifests, not directory names; this convention helps distinguish versions

Optional sample plans may be included **only if reference cases include them**.

---

## Reference Case Resolution Rules

When generating a new case, reference cases MUST be selected in the following order:

1. A reference case from the **same functional category**, if available
2. A generic command or execution-style reference case
3. The simplest available reference case as a fallback

Reference cases define:
- Script structure
- Parameter parsing
- Error handling
- Result JSON schema
- Artifact handling conventions

---

## Hard Constraints (Must Follow)

- **Do NOT invent new top-level fields** in `test.manifest.json`
- **Do NOT change** the `report.json` structure
- **Do NOT introduce new execution phases** (except via reboot/resume protocol)
- **Do NOT modify runner assumptions**
- **DO use shared PowerShell modules** from `assets/PowerShell/Modules/` (e.g., `Pvtx.Core`)
- **Do NOT create custom helper functions** within test cases for reusable logic
- Test logic should be self-contained, using only standard PowerShell cmdlets and shared modules

If the spec is incomplete or ambiguous, the behavior MUST default to the
reference case behavior.

---

## Parameter Rules

- Parameter names use `camelCase`
- Optional parameters inherit defaults from reference cases
- Required parameters must be explicitly validated in `run.ps1`
- All parameters must be documented in `README.md`
- All documented parameters must appear in `test.manifest.json`

## Manifest Meta Fields

### Required Fields
- `name`: Display name (does not need to match directory name)
- `description`: Short description for UI display
- `category`: Logical grouping (recommended: `Hardware` / `Software` / `Firmware`)
- `tags`: Array for classification and filtering

### Optional Fields
- `privilege`: Defaults to `User` if omitted
  - `User`: No special privileges required
  - `AdminPreferred`: Test prefers admin rights but can run without (shows warning)
  - `AdminRequired`: Test must run as administrator (blocks execution if not elevated)

---

## Environment Variables

The following environment variables are **automatically injected by the Runner**:

| Variable | Description |
|----------|-------------|
| `PVTX_TESTCASE_PATH` | Absolute path of the Test Case |
| `PVTX_TESTCASE_NAME` | Test Case display name |
| `PVTX_TESTCASE_ID` | Unique execution ID |
| `PVTX_TESTCASE_VER` | Test Case version identifier |
| `PVTX_ASSETS_ROOT` | Absolute path to assets root directory |
| `PVTX_MODULES_ROOT` | Absolute path to PowerShell\Modules directory |
| `PVTX_RUN_ID` | RunId for this Test Case execution |
| `PVTX_PHASE` | Reboot resume phase (0 = initial execution) |
| `PVTX_CONTROL_DIR` | Control directory for reboot requests |

## Exit Code Rules (Mandatory)

| Code | Meaning |
|------|----------|
| 0 | Pass |
| 1 | Fail |
| ≥2 | Script / Environment Error |

## Error Handling Rules

- Execution errors must be captured using `try/catch`
- Failures must produce `report.json` with:
  - `pass: false`
  - A structured `details` list
- Fatal errors must include a readable error message
- Exit with code 1 for test failures, ≥2 for script errors

---

## Shared PowerShell Modules

- Shared modules are located under `assets/PowerShell/Modules/`
- Runner automatically prepends `PVTX_MODULES_ROOT` to `PSModulePath`
- **Runner auto-imports all shared modules** - test cases should NOT include `Import-Module` statements
- **Current available modules:**
  - `Pvtx.Core`: Common file/JSON operations, step creation, output helpers
- **Best Practice:**
  - ✅ Use existing shared modules for common operations
  - ❌ Do NOT create custom helper functions within test cases
  - ❌ Do NOT include `Import-Module` statements (auto-imported by runner)
  - If new reusable logic is needed, add to shared modules instead

Example:
```powershell
# Modules are auto-imported by runner - no Import-Module needed

# Use module functions directly
$step = New-Step 'validate' 1 'Validate configuration'
Write-JsonFile -Path "artifacts/report.json" -Obj $result
```

## Reboot / Resume Protocol

Some cases require a reboot to validate resume behavior.

### Rules
- Treat `PVTX_PHASE=0` as the initial phase
- When a reboot is required, write `reboot.json` to `PVTX_CONTROL_DIR` and exit 0
- `reboot.json` MUST include: `type`, `nextPhase`, and `reason`
- Do not request more than one reboot in a single run
- Store state across reboot under the Case Run Folder (prefer `artifacts/`)
- After resume, `PVTX_PHASE` is set to `nextPhase`

### Example
```powershell
$phase = 0
if ($env:PVTX_PHASE) {
    $phase = [int]$env:PVTX_PHASE
}

if ($phase -eq 0) {
    # Phase 0 work...
    
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

## PowerShell Best Practices

### Array Counting
Always wrap filtering expressions in `@()` to force array conversion:

```powershell
# ❌ Wrong - fails when only 1 match
$passCount = ($steps | Where-Object { $_.status -eq 'PASS' }).Count

# ✅ Correct - always returns array
$passCount = @($steps | Where-Object { $_.status -eq 'PASS' }).Count
```

**Why**: Without `@()`, a single match returns the object itself, and `.Count` may fail.

## Artifact Rules

- Test cases MUST produce `artifacts/report.json` (strongly recommended)
- Other artifacts are optional: `artifacts/raw/`, `artifacts/attachments/`
- Do NOT generate `raw/` or `attachments/` by default unless needed
- Artifact paths should be included in `report.json`
- Artifacts must be deterministic and reproducible
- Pass/Fail is determined by **exit code**, not by output artifacts

---

## Output Format Requirements

When responding, output files MUST be formatted as:

```
<relative file path>
```powershell / json / markdown
<file content>
```

Each file must be clearly separated and complete.

---

## Validation Checklist (Self-Check Before Output)

Before finalizing output, ensure:

- All manifest parameters are parsed in `run.ps1`
- All parameters are documented in `README.md`
- `report.json` structure matches reference cases exactly
- `privilege` field is set if admin rights are needed
- Exit codes follow the mandatory rules (0=Pass, 1=Fail, ≥2=Error)
- Shared modules are imported correctly (no custom helper functions)
- Array counting uses `@()` wrapper where applicable
- Directory name follows `id@version` format
- Reboot protocol is implemented correctly if reboot is required
- No undocumented defaults exist
- No unused parameters exist
