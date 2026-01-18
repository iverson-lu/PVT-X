# Template Case - All Types

## Purpose
Demonstrates all supported parameter types and validates PVT-X's parameter passing, JSON parsing, and error handling capabilities.

## Test Logic
- **Step 1 – Basic parameter validation:** verifies string, int, boolean, double, and enum parameters, enforcing ranges and type correctness.
- **Step 2 – Path and JSON parsing:** resolves `DataPath` relative to `PVTX_TESTCASE_PATH` and parses `Items`, `Options`, and `Config` payloads to confirm structure and types.
- **Step 3 – Outcome control:** uses the `Mode` parameter to drive pass/fail/timeout/error behavior so the runner can observe each outcome.

## Parameters
- `Mode` (enum, required, default `pass`): Controls final outcome; supports `pass`, `fail`, `timeout`, `error`.
- `Message` (string, optional, default `"hello"`): Example string input validated for non-empty content.
- `Count` (int, optional, default `42`): Demonstrates integer parsing with accepted range -100 to 100.
- `Enabled` (boolean, optional, default `true`): Toggles a simple boolean code path.
- `Threshold` (double, optional, default `3.14`): Validates floating-point parsing with range 0 to 9999.
- `DataPath` (path, optional, default `data/test-data.txt`): Shows how to resolve relative paths within the case directory.
- `Items` (json array, optional, default `[1, 2, 3]`): Parsed to verify array handling.
- `Options` (json array, optional, default `["Windows 7", "Windows 10"]`): Simulates multi-select enum input.
- `Config` (json object, optional, default `{"timeout": 30, "retry": true}`): Provides nested key/value data for validation.

## How to Run Manually

```powershell
pwsh ./run.ps1 -Mode pass -Message "sample" -Count 10 -Enabled:$true -Threshold 2.5 -DataPath "data/test-data.txt" -Items '[1,2,3]' -Options '["Windows 10", "Windows 11"]' -Config '{"timeout":30,"retry":true}'
```

## Expected Result
- Success: script completes both validation steps, emits `report.json`, and exits 0 with console summary similar to the sample below.
- Failure modes: `Mode=fail` forces exit 1 after validations; `Mode=timeout` sleeps ~150 seconds to trigger runner timeout; `Mode=error` throws to yield exit ≥2.

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
