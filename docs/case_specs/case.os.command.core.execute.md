# Execute Command or Script Case (v0.1)

## 1. Purpose and Scope

### Purpose
This test case executes a command or script on the DUT (Device Under Test) using **PowerShell** as the execution environment. The case itself does not implement custom pass/fail logic; instead, **the overall result is determined by the runner's exit code handling**.

### Scope
- Supported execution types:
  1. Inline PowerShell command
  2. PowerShell script file (`.ps1`)
  3. Batch script file (`.bat` / `.cmd`)
- Only PowerShell is used as the runner.
- CMD semantics are supported indirectly by invoking `cmd.exe` from PowerShell.
- Timeout, environment variables, working directory, and privilege control are managed by the upper-level tool/runner and are **out of scope** for this case.

---

## 2. Parameters

### 2.1 Required Parameters

- `mode` (enum, required)
  - Allowed values:
    - `inline` – execute inline PowerShell command text
    - `ps1` – execute a PowerShell script file
    - `bat` – execute a batch script file
  - Description: Defines the execution input type.

### 2.2 Mode-Specific Parameters

#### A) `mode = inline`

- `command` (string, required)
  - Description: PowerShell command text to be executed.
  - Notes:
    - Executed as-is in PowerShell.
    - Multi-line commands are allowed.
    - To use CMD syntax, explicitly invoke `cmd /c ...` within the command.

#### B) `mode = ps1`

- `script_path` (string, required)
  - Description: Path to the `.ps1` script file.

- `args` (string, optional)
  - Description: Argument string passed to the script.
  - Notes:
    - Arguments are passed through as raw text.
    - No structured parsing is performed by this case.

#### C) `mode = bat`

- `script_path` (string, required)
  - Description: Path to the `.bat` or `.cmd` script file.

- `args` (string, optional)
  - Description: Argument string passed to the batch script.

---

## 3. Execution Semantics

### 3.1 Runner Behavior

- All executions are initiated from PowerShell.
- The runner is responsible for:
  - Launching the command or script
  - Capturing stdout and stderr
  - Determining the final exit code
  - Deciding Pass/Fail based on exit code

### 3.2 Exit Code Definition

- The **final exit code** is the value returned by the runner after execution.
- For inline commands:
  - If an external executable is invoked, its process exit code is used.
  - If a terminating PowerShell error or unhandled exception occurs, the exit code should be non-zero.

- For `.ps1` scripts:
  - Script authors are responsible for explicitly setting exit codes using `exit <code>`.
  - If the script intends to propagate an external tool's exit code, it should use `exit $LASTEXITCODE`.

- For `.bat` / `.cmd` scripts:
  - Scripts are executed via `cmd.exe /c`.
  - The batch script should use `exit /b <code>` to control the returned exit code.

---

## 4. Validation Rules

- `mode = inline`:
  - `command` must be provided and non-empty.

- `mode = ps1` or `bat`:
  - `script_path` must be provided and non-empty.

- Parameter combinations that do not match the selected `mode` are considered invalid and should result in a failed execution with an appropriate error reason.

---

## 5. Result and Artifacts

### 5.1 Recommended Result Fields

- `status`: Pass / Fail
- `mode`: inline / ps1 / bat
- `command` or `script_path`
- `args` (if provided)
- `exit_code`
- `stdout` (may be truncated)
- `stderr` (may be truncated)
- `failure_reason` (only when failed; e.g., `invalid_param`, `script_not_found`, `runner_failed`)

### 5.2 Output Size Control

- Stdout and stderr may be truncated to avoid excessive log size.
- Truncation behavior and limits are defined by the upper-level framework.

---

## 6. Notes and Recommendations

- This case can execute arbitrary commands and scripts and may modify system state.
- Users should ensure scripts are idempotent and return meaningful exit codes.
- This case is intended as a low-level, reusable execution primitive for higher-level test scenarios.

---

## 7. Versioning and Future Extensions (Non-Blocking)

- Support for multiple commands per case
- Optional stdout/stderr content assertions
- Explicit reboot request signaling (handled by the plan/engine layer)

