# Template Case - All Types

## Purpose
Demonstrates all supported parameter types and validates PVT-X's parameter passing, JSON parsing, and error handling capabilities.

## What This Test Does

### Step 1: Basic Parameter Validation
- Validates string, int, boolean, double, and enum parameters
- Checks min/max constraints and type correctness
- Captures parameter values in metrics

### Step 2: Path and JSON Parsing
- Resolves relative path using `PVTX_TESTCASE_PATH` environment variable
- Parses JSON array (`Items`) and JSON object (`Config`) using `ConvertFrom-Json`
- Parses multi-select JSON array (`Options`) from enumValues
- Validates parsed data structure and types

### Step 3: Outcome Control
Controlled by `Mode` parameter:
- **pass** → All validations pass, exit code 0
- **fail** → Validations pass but forces failure, exit code 1
- **timeout** → Sleeps 150 seconds to demonstrate timeout handling
- **error** → Throws exception to demonstrate error capture

## Parameters

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| Mode | enum | ✓ | pass | Test outcome: pass/fail/timeout/error |
| Message | string | | "hello" | String example |
| Count | int | | 42 | Integer with range -100 to 100 |
| Enabled | boolean | | true | Boolean flag |
| Threshold | double | | 3.14 | Double with range 0 to 9999 |
| DataPath | path | | "data/test-data.txt" | Relative path example |
| Items | json | | [1, 2, 3] | JSON array (textarea in UI) |
| Options | json | | ["Windows 7", "Windows 10"] | Multi-select from 4 Windows versions |
| Config | json | | {"timeout": 30, "retry": true} | JSON object (textarea in UI) |

## Implementation Highlights

**Parameter Validation:**
```powershell
if ([string]::IsNullOrWhiteSpace($Message)) { throw 'Message cannot be empty.' }
if ($Count -lt 0) { throw 'Count must be >= 0.' }
```

**JSON Parsing:**
```powershell
$itemsData = $Items | ConvertFrom-Json -AsHashtable -ErrorAction Stop
$optionsData = $Options | ConvertFrom-Json -ErrorAction Stop
$configData = $Config | ConvertFrom-Json -AsHashtable -ErrorAction Stop
```

**Path Resolution:**
```powershell
$baseDir = $env:PVTX_TESTCASE_PATH ?? $PSScriptRoot
$resolvedPath = [IO.Path]::IsPathRooted($DataPath) ? $DataPath : (Join-Path $baseDir $DataPath)
```

**Structured Output:**
- Uses `report.json` schema v1.0
- Two steps with timing, status, and metrics
- Captures all parameter values and validation results

## Quick Test

```powershell
# Test pass
pwsh .\run.ps1 -Mode pass

# Test with custom parameters
pwsh .\run.ps1 -Mode pass -Message "test" -Count 99 -Enabled:$false

# Test multi-select options
pwsh .\run.ps1 -Mode pass -Options '["Windows 10", "Windows 11"]'

# Test fail mode
pwsh .\run.ps1 -Mode fail

# Test error handling
pwsh .\run.ps1 -Mode error
```

## Expected Output

```
==================================================
TEST: case.template.demo.core.all_types  RESULT: PASS  EXIT: 0
UTC:  2026-01-18T12:34:56Z
--------------------------------------------------
[1/2] verify_basic_params ................. PASS
[2/2] verify_path_and_json ................ PASS
--------------------------------------------------
basic: message_len=5 count=42 enabled=True threshold=3.14
multiselect: selected=[Windows 7, Windows 10]
path+json: items=3 path_exists=True timeout=30 retry=True
--------------------------------------------------
SUMMARY: total=2 passed=2 failed=0 skipped=0
==================================================
MACHINE: overall=PASS exit_code=0
```
