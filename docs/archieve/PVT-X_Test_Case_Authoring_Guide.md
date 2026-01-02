# PVT-X Test Case Authoring Guide

This document describes the **standard way to author a Test Case** in PVT-X.  
All test cases must follow this contract so that Runner, UI, and reporting behave consistently.

This guide focuses on **test case scripts and manifests only**.  
Runner/Engine responsibilities (result.json, env.json, stdout/stderr capture, timeout enforcement, etc.) are out of scope here.

---

## 1. Test Case Structure

A test case is a **self-contained folder**:

```
MyTestCase/
  test.manifest.json
  run.ps1
  README.md   (optional but recommended)
```

---

## 2. test.manifest.json

### Required fields (minimum)

```json
{
  "id": "MyTestCase",
  "name": "My Test Case",
  "version": "1.0.0",
  "entry": {
    "type": "powershell",
    "script": "run.ps1"
  },
  "timeoutSec": 30,
  "parameters": []
}
```

---

### timeoutSec

```json
"timeoutSec": 2
```

- Enforced by **Runner**
- Script **must not** attempt to implement its own timeout logic
- If execution exceeds this value:
  - Runner terminates the process
  - Result is marked as `Timeout`
- Script may be killed before `finally` executes — this is expected behavior

---

### parameters

Each parameter describes **test intent**, not implementation details.

Example:

```json
"parameters": [
  {
    "name": "TestMode",
    "type": "string",
    "required": true,
    "default": "pass",
    "help": "Controls outcome: pass|fail|timeout|error"
  }
]
```

#### Parameter rules

| Rule | Description |
|------|------------|
| Name | Must match the PowerShell parameter name exactly |
| type | Primitive only (string, int, bool, etc.) |
| required | Indicates whether Runner must supply a value |
| default | Used when not overridden by Suite / Plan |
| help | Human-readable explanation shown in UI |

❌ Do **not** use parameters for:
- File paths
- Output directories
- Debug flags
- Logging verbosity
- Runner behavior

---

## 3. run.ps1 Responsibilities

### What a Test Case **IS responsible for**

- Implementing test logic  
- Writing **human-readable logs** to stdout/stderr  
- Writing **test-owned artifacts** under `artifacts/`  
- Returning a meaningful **exit code**

---

### What a Test Case **MUST NOT do**

- Write `result.json`, `env.json`, `manifest.json`  
- Modify files outside the case run folder  
- Control timeout or execution lifecycle  
- Assume the working directory outside its run folder  

---

## 4. Logging Rules (stdout / stderr)

Use standard PowerShell streams:

| Purpose | API |
|--------|-----|
| Info | `Write-Output` |
| Warning | `Write-Warning` or `Write-Output "[WARN] ..."` |
| Error | `Write-Error` |

Recommended log prefixes:

```
[INFO] ...
[WARN] ...
[ERROR] ...
[RESULT] Pass|Fail
```

These logs are:
- Captured by Runner
- Displayed in UI Console
- Persisted to stdout/stderr logs

---

## 5. Artifacts Output

All test-owned files **must be written under `artifacts/`**:

```
artifacts/
  report.json
  raw/
  attachments/
```

---

### report.json (required)

A machine-readable summary of **test logic outcome** (not execution status).

Example:

```json
{
  "testId": "MyTestCase",
  "outcome": "Pass",
  "summary": "Camera detected successfully",
  "details": {},
  "metrics": {}
}
```

Notes:
- `outcome` reflects **test judgment only**
- Timeout / Abort / Engine errors are handled by Runner `result.json`
- `report.json` may be missing in timeout scenarios — this is valid

---

### raw/

For:
- Structured data
- Command outputs
- Enumeration results

Examples:
- `devices.json`
- `events.txt`

---

### attachments/

For:
- Logs
- Binary blobs
- Screenshots
- Dumps

Examples:
- `trace.log`
- `capture.bin`

---

## 6. Exit Code Contract (Critical)

Exit codes are the **only signal** used by Runner to determine execution status.

| Exit Code | Meaning |
|-----------|--------|
| 0 | Test Passed |
| 1 | Test Failed (assertion / validation failed) |
| 2 | Script Error (exception, invalid input, missing dependency) |

Rules:
- Do **not** throw uncaught exceptions
- Always initialize and explicitly exit with a code
- Use `try / catch / finally`

---

## 7. Timeout Behavior

If `timeoutSec` is exceeded:

- Runner terminates the process
- Script may not reach `catch` or `finally`
- Partial artifacts and logs may exist
- This is expected and correct

Do **not** attempt to handle timeout inside the script.

---

## 8. Template Case

A reference **TemplateCase** is provided in the repository.

It demonstrates:
- Parameter normalization
- All exit code paths
- Timeout behavior
- Artifacts writing (raw + attachments)
- StrictMode-safe PowerShell patterns

**Always start new test cases by copying TemplateCase.**

---

## 9. Design Principle (Key Takeaway)

> A Test Case expresses **what to verify**, not **how the system executes it**.

Runner controls execution.  
Test Case controls validation.
