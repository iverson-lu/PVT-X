# Execute Command or Script Case

## Purpose
Executes a command or script on the DUT (Device Under Test) using **PowerShell** as the execution environment. The case does not implement custom pass/fail logic; instead, **the overall result is determined by the exit code** (0=Pass, non-zero=Fail).

## Test Logic
- **Step 1 – Parameter validation:** validates that required parameters are provided based on the selected mode (inline/ps1/bat).
- **Step 2 – Command execution:** executes the command or script and captures stdout, stderr, and exit code. The final Pass/Fail status is determined by the exit code.

## Parameters
- `Mode` (enum, required): Execution mode - `inline` (PowerShell command), `ps1` (PowerShell script file), or `bat` (batch script file).
- `Command` (string, optional): Inline PowerShell command text (required when Mode=inline).
- `ScriptPath` (path, optional): Path to script file (required when Mode=ps1 or Mode=bat). Can be absolute or relative to the test case directory.
- `Args` (string, optional, default `""`): Arguments passed to the script (used when Mode=ps1 or Mode=bat).

## Supported Execution Types

### 1. Inline PowerShell Command (Mode=inline)
Execute a PowerShell command directly:
```powershell
pwsh ./run.ps1 -Mode inline -Command "Write-Host 'Hello World'; exit 0"
```

### 2. PowerShell Script File (Mode=ps1)
Execute a PowerShell script file:
```powershell
pwsh ./run.ps1 -Mode ps1 -ScriptPath "scripts/test-script.ps1" -Args "arg1 arg2"
```

### 3. Batch Script File (Mode=bat)
Execute a batch script file:
```powershell
pwsh ./run.ps1 -Mode bat -ScriptPath "scripts/test-script.bat" -Args "arg1 arg2"
```

## Exit Code Semantics

### For inline commands:
- If an external executable is invoked, its process exit code is used.
- If a terminating PowerShell error occurs, the exit code should be non-zero.

### For .ps1 scripts:
- Script authors must explicitly set exit codes using `exit <code>`.
- To propagate an external tool's exit code: `exit $LASTEXITCODE`.

### For .bat/.cmd scripts:
- Scripts are executed via `cmd.exe /c`.
- Use `exit /b <code>` to control the returned exit code.

## Example Scripts

### Example 1: PowerShell script (scripts/test-script.ps1)
```powershell
param([string]$Message = "default")
Write-Host "Message: $Message"
# Your test logic here
if ($Message -eq "fail") {
  exit 1  # Fail
} else {
  exit 0  # Pass
}
```

### Example 2: Batch script (scripts/test-script.bat)
```batch
@echo off
echo Running batch test
echo Args: %*
REM Your test logic here
exit /b 0
```

## Expected Result
- **Success (exit 0):** Test completes both validation and execution steps, emits `report.json`, and exits with code 0.
- **Failure (exit non-zero):** Test execution fails, report indicates failure with exit code and reason.
- **Error:** Invalid parameters or script not found results in failure with appropriate error message.

## Sample Console Output
```
==================================================
TEST: case.os.command.core.execute  RESULT: PASS  EXIT: 0
UTC:  2026-01-21T12:34:56Z
--------------------------------------------------
[1/2] validate_parameters ............. PASS
[2/2] execute_command ................. PASS
--------------------------------------------------
mode: inline, command_length: 35
actual_exit_code: 0
stdout: Hello World
--------------------------------------------------
SUMMARY: total=2 passed=2 failed=0 skipped=0
==================================================
MACHINE: overall=PASS exit_code=0
```

## Notes
- This case can execute arbitrary commands and scripts and may modify system state.
- Users should ensure scripts are idempotent and return meaningful exit codes.
- Stdout and stderr are captured and may be truncated to avoid excessive log size (max 4KB in report, 500 chars in console).
- Timeout, environment variables, working directory, and privilege control are managed by the upper-level runner.
