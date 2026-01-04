# Template Case - All Types

## Purpose
Demonstrates all supported parameter types and structured output format with controllable test outcomes.

## Test Logic
- Validates and parses all parameter types (string, int, boolean, double, enum, path, json)
- Resolves relative path using `PVTX_TESTCASE_PATH` environment variable
- If path exists and is a file, reads and includes content preview in metrics
- Executes based on `E_Mode` parameter:
  - `pass`: validates all parameters and returns success
  - `fail`: validates parameters but forces failure
  - `timeout`: sleeps 150 seconds to trigger timeout
  - `error`: throws exception to demonstrate error handling
- Generates structured `report.json` with all parameter values and validation results

## Parameters
- **E_Mode** (enum, required, default: "pass"): Controls test outcome (pass | fail | timeout | error)
- **S_Text** (string, optional, default: "hello"): String parameter example
- **N_Int** (int, optional, default: 42, range: -100 to 100): Integer parameter with min/max constraints
- **B_Flag** (boolean, optional, default: true): Boolean parameter
- **N_Double** (double, optional, default: 3.14, range: 0 to 9999): Double parameter with unit
- **P_Path** (path, optional, default: "data/test-data.txt"): Path parameter (relative to test case directory, resolved using `PVTX_TESTCASE_PATH`)
- **ItemsJson** (json, optional, default: "[1, 2, 3]"): JSON array example parsed using ConvertFrom-Json
- **ConfigJson** (json, optional, default: '{"timeout": 30, "retry": true}'): JSON object example parsed using ConvertFrom-Json

## How to Run Manually
```powershell
# Test pass scenario
pwsh ./run.ps1 -E_Mode "pass"

# Test fail scenario
pwsh ./run.ps1 -E_Mode "fail"

# Test with custom parameters
pwsh ./run.ps1 -E_Mode "pass" -S_Text "custom text" -N_Int 99 -B_Flag $false

# Test path reading (will use PVTX_TESTCASE_PATH if available, otherwise $PSScriptRoot)
pwsh ./run.ps1 -E_Mode "pass" -P_Path "data/test-data.txt"
```

## Expected Result
- **Success (E_Mode="pass")**: All parameters validated, path resolved and read (if exists), structured report generated. Exit code 0.
- **Failure (E_Mode="fail")**: Parameters validated but test forced to fail. Exit code 1.
- **Timeout (E_Mode="timeout")**: Script sleeps 150 seconds, should be terminated by runner. Exit code varies.
- **Error (E_Mode="error")**: Exception thrown, error captured in report. Exit code 2.
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
