# Power Source Switch (AC/DC) via private_wmi

## Purpose
Validate that the platform can switch power source between AC and DC using functions provided by `private_wmi.ps1`, and can read back the current power status.

## Test Logic
- Resolves `private_wmi.ps1` path (relative paths are resolved from `PVTX_TESTCASE_PATH` if present, otherwise from the case directory).
- Executes one of the following actions in a child PowerShell process (ExecutionPolicy Bypass):
  - `SwitchToDC`: runs `. <private_wmi.ps1>; Switch-PowerToDC` and checks exit code
  - `SwitchToAC`: runs `. <private_wmi.ps1>; Switch-PowerToAC` and checks exit code
  - `GetStatus`: runs `. <private_wmi.ps1>; Get-PowerStatus` and parses console output (`AC` or `DC`)
- Produces `artifacts/report.json` with command results and captured stdout/stderr.

## Parameters
- `E_Action` (enum): `SwitchToDC` | `SwitchToAC` | `GetStatus`
- `P_PrivateWmiScript` (path): path to `private_wmi.ps1`
- `E_ExpectedStatus` (enum): `any` | `AC` | `DC` (only used for `GetStatus`)
- `N_CommandTimeoutSec` (int): timeout for the child command execution (seconds)

## How to Run Manually
```powershell
# Switch to DC
pwsh .\run.ps1 -E_Action SwitchToDC -P_PrivateWmiScript .\private_wmi.ps1

# Switch to AC
pwsh .\run.ps1 -E_Action SwitchToAC -P_PrivateWmiScript .\private_wmi.ps1

# Query status (expects AC or DC on stdout)
pwsh .\run.ps1 -E_Action GetStatus -P_PrivateWmiScript .\private_wmi.ps1

# Query status and assert expected value
pwsh .\run.ps1 -E_Action GetStatus -P_PrivateWmiScript .\private_wmi.ps1 -E_ExpectedStatus AC
```

## Expected Result
- `SwitchToDC` / `SwitchToAC`:
  - Exit code `0` when the switch succeeds.
  - Exit code `1` when the switch fails (non-zero child exit code).
- `GetStatus`:
  - Exit code `0` when it outputs `AC` or `DC` (and matches `E_ExpectedStatus` if specified).
  - Exit code `1` when output is missing/invalid or does not match `E_ExpectedStatus`.
- Exit code `2` indicates a script or environment error (e.g., missing `private_wmi.ps1`, timeout, or unexpected exception).
