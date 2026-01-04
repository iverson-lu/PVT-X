# ReadFileCase

A simple test case that reads a file and outputs its content to the console.

## Parameters

| Name       | Type | Required | Default                              | Description                          |
|------------|------|----------|--------------------------------------|--------------------------------------|
| P_FilePath | path | Yes      | C:\\Windows\\System32\\drivers\\etc\\hosts | Path to the file to read and output. Can be absolute or relative. If relative, it's resolved against the test case source folder. |

## PVT-X Environment Variables

The Runner automatically injects these environment variables into all test cases:

| Variable            | Description                                    | Example                                  |
|---------------------|------------------------------------------------|------------------------------------------|
| PVTX_TESTCASE_PATH  | Absolute path to test case source folder       | `d:\Dev\PVT-X-1\assets\TestCases\ReadFileCase` |
| PVTX_TESTCASE_NAME  | Test case display name from manifest           | `Read File Case`                         |
| PVTX_TESTCASE_ID    | Test case unique identifier                    | `ReadFileCase`                           |
| PVTX_TESTCASE_VER   | Test case version from manifest                | `1.0.0`                                  |

These can be accessed in PowerShell via `$env:PVTX_TESTCASE_PATH`, `$env:PVTX_TESTCASE_NAME`, `$env:PVTX_TESTCASE_ID`, `$env:PVTX_TESTCASE_VER`.

## Execution Flow

1. **Resolve File Path** - If P_FilePath is relative, resolves it against PVTX_TESTCASE_PATH (test case source folder)
2. **Check File Exists** - Verifies that the resolved file path exists
3. **Read and Output Content** - Reads the file content and outputs it to console

## Expected Behavior

### Success Case
- File exists and is readable
- File content is displayed in console between markers
- Outputs line count and content length
- Script exits with code `0`
- Runner result: `Passed`

### Failure Case
- File does not exist
- Outputs error message
- Script exits with code `2`
- Runner result: `Error`

## Artifacts

**Always created:**
- `artifacts/report.json` - Structured report with schema v1.0 format

**Format:**
```json
{
  "schema": { "version": "1.0" },
  "test": {
    "id": "ReadFileCase",
    "name": "ReadFileCase",
    "params": {
      "P_FilePath": "path/to/file"
    }
  },
  "summary": {
    "status": "PASS|FAIL",
    "exit_code": 0|1|2,
    "counts": { "total": 2, "pass": 0-2, "fail": 0-2, "skip": 0 },
    "duration_ms": 123
  },
  "steps": [
    {
      "id": "check_file_exists",
      "index": 1,
      "name": "Check if file exists",
      "status": "PASS|FAIL",
      "expected": { "file_exists": true },
      "actual": { "file_exists": true|false }
    },
    {
      "id": "read_file_content",
      "index": 2,
      "name": "Read and output file content",
      "status": "PASS|FAIL",
      "expected": { "content_read": true },
      "actual": {
        "content_read": true,
        "line_count": 10,
        "content_length": 1234
      }
    }
  ]
}
```

## Console Output Format

```
==================================================
TEST: ReadFileCase
UTC:  2026-01-04T12:34:56Z
--------------------------------------------------
[1/2] Check if file exists ...
      File exists: C:\path\to\file.txt
[2/2] Read and output file content ...

---------- FILE CONTENT START ----------
[file content displayed here]
---------- FILE CONTENT END ----------

      Lines read: 10
--------------------------------------------------
SUMMARY: total=2 passed=2 failed=0 skipped=0
==================================================
MACHINE: overall=PASS exit_code=0
```

## Local Quick Test

```powershell
# Test with default file (absolute path - hosts)
pwsh .\run.ps1 -P_FilePath "C:\Windows\System32\drivers\etc\hosts"

# Test with relative path (file in test case folder)
# First create a test file: Set-Content -Path "test-data.txt" -Value "Hello World"
pwsh .\run.ps1 -P_FilePath "test-data.txt"

# Test with relative subfolder path
pwsh .\run.ps1 -P_FilePath "data\sample.txt"

# Test with non-existent file (should fail)
pwsh .\run.ps1 -P_FilePath "C:\nonexistent\file.txt"
```

## Notes

- Absolute paths are used as-is
- Relative paths are resolved against the test case source folder (PVTX_TESTCASE_PATH)
- File content is displayed directly in console output
- Uses `-Raw` parameter to read entire file as single string
- Also counts lines separately for reporting
- Proper error handling for missing files
- Follows PVT-X structured output format
- The resolved path is included in the report.json for traceability
