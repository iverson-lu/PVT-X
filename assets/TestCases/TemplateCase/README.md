# TemplateCase

Template test case demonstrating all supported parameter types and structured output format (similar to NetworkPingConnectivity).

## Parameters

| Name       | Type    | Required | Default                            | Description                                                    |
|------------|---------|----------|------------------------------------|----------------------------------------------------------------|
| E_Mode     | enum    | Yes      | pass                               | Controls test outcome: pass \| fail \| timeout \| error        |
| S_Text     | string  | No       | hello                              | String parameter example                                       |
| N_Int      | int     | No       | 42                                 | Integer parameter (min: -100, max: 100)                        |
| B_Flag     | boolean | No       | true                               | Boolean parameter                                              |
| N_Double   | double  | No       | 3.14                               | Double parameter (min: 0, max: 9999, unit: unit)               |
| P_Path     | path    | No       | C:\\Windows                        | Path parameter                                                 |
| ItemsJson  | json    | No       | [1, 2, 3]                          | JSON array example - parsed using ConvertFrom-Json             |
| ConfigJson | json    | No       | {"timeout": 30, "retry": true}     | JSON object example - parsed using ConvertFrom-Json            |

## PVT-X Environment Variables

The Runner automatically injects these environment variables into all test cases:

| Variable            | Description                                    | Example                                  |
|---------------------|------------------------------------------------|------------------------------------------|
| PVTX_TESTCASE_PATH  | Absolute path to test case source folder       | `D:\Dev\PVT-X-1\assets\TestCases\TemplateCase` |
| PVTX_TESTCASE_NAME  | Test case display name from manifest           | `Template Case`                          |
| PVTX_TESTCASE_ID    | Test case unique identifier                    | `TemplateCase`                           |
| PVTX_TESTCASE_VER   | Test case version from manifest                | `1.0.0`                                  |

Access in PowerShell: `$env:PVTX_TESTCASE_PATH`, `$env:PVTX_TESTCASE_NAME`, `$env:PVTX_TESTCASE_ID`, `$env:PVTX_TESTCASE_VER`

## Execution Flow

1. **Normalize & Validate** - Normalizes E_Mode, validates all parameters
2. **Parse JSON** - Parses ItemsJson and ConfigJson using ConvertFrom-Json
3. **Type Validation** - Validates all parameter types
4. **Execute Mode** - Runs based on E_Mode value
5. **Generate Report** - Creates structured report.json with schema, test, summary, and steps

## Expected Behavior by Mode

### pass
- Validates all parameters successfully
- Outputs structured report with PASS status
- Script exits with code `0`
- Runner result: `Passed`

### fail
- Validates parameters but forces failure
- Outputs structured report with FAIL status
- Script exits with code `1`
- Runner result: `Failed`

### error
- Throws exception during execution
- Outputs report with error details in step.error
- Script exits with code `2`
- Runner result: `Error`

### timeout
- Sleeps 150 seconds (exceeds timeoutSec=120 in manifest)
- Runner should terminate and mark as `Timeout`
- Script exits with code `1` (if not killed by runner)
- Runner result: `Timeout`

## Artifacts

**Always created:**
- `artifacts/report.json` - Structured report with schema v1.0 format

**Format:**
```json
{
  "schema": { "version": "1.0" },
  "test": {
    "id": "TemplateCase",
    "name": "TemplateCase",
    "params": { ... }
  },
  "summary": {
    "status": "PASS|FAIL",
    "exit_code": 0|1|2,
    "counts": { "total": 1, "pass": 0|1, "fail": 0|1, "skip": 0 },
    "duration_ms": 123
  },
  "steps": [ ... ]
}
```

> **Note:** In timeout mode, Runner may kill the process before `finally` executes.

## Console Output Format

Follows NetworkPingConnectivity compact format:

```
==================================================
TEST: TemplateCase  RESULT: PASS  EXIT: 0
UTC:  2026-01-03T12:34:56Z
--------------------------------------------------
[1/1] Validate all parameters ... PASS
      mode=pass text='hello' int=42 flag=True double=3.14
      items_count=3 path_exists=True
--------------------------------------------------
SUMMARY: total=1 passed=1 failed=0 skipped=0
==================================================
MACHINE: overall=PASS exit_code=0
```

## Local Quick Test

```powershell
# Test pass mode
pwsh .\run.ps1 -E_Mode pass
echo $LASTEXITCODE

# Test with custom parameters
pwsh .\run.ps1 -E_Mode pass -S_Text "custom" -N_Int 99 -B_Flag:$false

# Test fail mode
pwsh .\run.ps1 -E_Mode fail
echo $LASTEXITCODE

# Test error mode
pwsh .\run.ps1 -E_Mode error
echo $LASTEXITCODE

# Test timeout mode (will take 150+ seconds or be killed by runner at 120s)
pwsh .\run.ps1 -E_Mode timeout

# Test with JSON parameters
pwsh .\run.ps1 -E_Mode pass -ItemsJson '[10, 20, 30]' -ConfigJson '{"max": 100, "enabled": false}'
```

## Validation Features

- **Type checking** - Validates all PowerShell parameter types
- **JSON parsing** - Demonstrates ConvertFrom-Json usage
- **Enum validation** - Ensures E_Mode is one of allowed values
- **Structured output** - Uses schema v1.0 format with steps
- **Metrics collection** - Captures parameter values and computed metrics
- **Error handling** - Proper try/catch/finally with error details in report
