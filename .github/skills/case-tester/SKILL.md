---
name: pvt-x-case-tester
description: Test a newly created PVT-X test case by running it directly via PowerShell and then through the PVT-X CLI to ensure correctness.
version: 1.0
---

## Purpose

This skill provides a **systematic approach to testing a newly created test case** for the PVT-X PC test suite.

Testing ensures:
- The PowerShell script executes correctly with all parameter variations
- Artifacts are generated properly (`report.json` and optional files)
- The test case integrates correctly with the PVT-X runner (CLI)
- Exit codes match expected outcomes
- Parameter validation works as documented

---

## When to Use This Skill

Use this skill when:

- You have just created or modified a test case
- You want to validate a test case before committing it
- You need to verify parameter handling, artifact generation, and exit codes
- You want to ensure CLI integration works correctly

Do NOT use this skill for:
- Testing the entire suite or plan execution
- Performance testing or stress testing
- Testing the runner itself (use unit tests for that)

---

## Testing Strategy

Testing a new test case follows **two phases**:

### Phase 1: Direct PowerShell Execution
Run the `run.ps1` script directly with PowerShell to test the core logic.

**Key Points:**
- Import shared PowerShell modules manually (runner auto-imports, but direct execution doesn't)
- Test with various parameter combinations from README examples
- Verify exit codes match expected behavior
- Inspect generated artifacts (`report.json`, optional `raw/`, `attachments/`)
- Test both pass and fail scenarios when applicable

### Phase 2: CLI Integration Testing
Run the test case through the PVT-X CLI to ensure runner integration.

**Key Points:**
- Use `dotnet run --project src/PcTest.Cli -- run` command
- Verify Case Run Folder structure is created properly
- Check that runner-injected environment variables work
- Validate timeout handling
- Confirm redaction of secrets (if applicable)

---

## Phase 1: Direct PowerShell Execution

### Step 1: Set up PowerShell Module Path

Before running any test case directly, you **MUST** configure PowerShell to load shared modules:

```powershell
# Navigate to workspace root
cd c:\Dev\PVT-X

# Import shared modules by adding to PSModulePath
$env:PSModulePath = "$(Resolve-Path 'assets/PowerShell/Modules');$env:PSModulePath"

# Verify modules are available
Get-Module -ListAvailable Pvtx.*
```

**Why this is needed:**
- The runner automatically injects `PVTX_MODULES_ROOT` and imports modules
- Direct PowerShell execution does NOT have this automatic setup
- Test cases depend on shared modules like `Pvtx.Core`

### Step 2: Run with Default Parameters

Navigate to the test case directory and execute with defaults:

```powershell
cd assets/TestCases/<case-id@version>
pwsh -File .\run.ps1
```

### Step 3: Run with Example Parameters from README

Use the parameter examples documented in the case's `README.md`:

```powershell
# Example from README
pwsh -File .\run.ps1 `
  -MinPhysicalCores 4 `
  -MinLogicalProcessors 8 `
  -RequireX64 $true
```

### Step 4: Test Edge Cases and Failure Scenarios

Test scenarios that should **fail** (exit code 1):

```powershell
# Test with unrealistic requirements to trigger failure
pwsh -File .\run.ps1 -MinPhysicalCores 999
```

Test scenarios with **invalid parameters** (exit code ≥2):

```powershell
# Test with invalid parameter type
pwsh -File .\run.ps1 -MinPhysicalCores "not-a-number"
```

### Step 5: Verify Artifacts

After each run, inspect the generated artifacts:

```powershell
# Check report.json exists and is valid
if (Test-Path "artifacts/report.json") {
    Get-Content "artifacts/report.json" | ConvertFrom-Json | Format-List
} else {
    Write-Error "Missing artifacts/report.json"
}

# Check optional artifacts (if B_SaveRaw or similar parameter used)
Get-ChildItem "artifacts/" -Recurse
```

### Step 6: Verify Exit Codes

Check that exit codes match expectations:

| Scenario | Expected Exit Code |
|----------|-------------------|
| All validations pass | 0 |
| One or more validations fail | 1 |
| Script error (invalid parameter, exception, etc.) | ≥2 |

```powershell
pwsh -File .\run.ps1 -SomeParam Value
$exitCode = $LASTEXITCODE
Write-Host "Exit code: $exitCode"

if ($exitCode -eq 0) {
    Write-Host "✅ Test passed"
} elseif ($exitCode -eq 1) {
    Write-Host "⚠️ Test failed (expected behavior)"
} else {
    Write-Host "❌ Script error"
}
```

---

## Phase 2: CLI Integration Testing

### Step 1: Discover Test Assets

Ensure the new test case is discoverable:

```powershell
cd c:\Dev\PVT-X

dotnet run --project src/PcTest.Cli -- discover
```

**Verify output includes your test case.**

### Step 2: Run via CLI with Default Parameters

```powershell
dotnet run --project src/PcTest.Cli -- run `
  --target testcase `
  --id "<case-id>@<version>"
```

Example:
```powershell
dotnet run --project src/PcTest.Cli -- run `
  --target testcase `
  --id "case.hw.cpu.core.params_verify@1.0.0"
```

### Step 3: Run via CLI with Custom Parameters

Pass inputs as JSON:

```powershell
dotnet run --project src/PcTest.Cli -- run `
  --target testcase `
  --id "<case-id>@<version>" `
  --inputs '{"MinPhysicalCores": 4, "MinLogicalProcessors": 8}'
```

### Step 4: Inspect Case Run Folder

After CLI execution, inspect the Case Run Folder:

```powershell
# Find the latest run
$latestRun = Get-ChildItem "Runs/" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
cd $latestRun.FullName

# Inspect structure
Get-ChildItem -Recurse

# Check key files
Get-Content "manifest.json" | ConvertFrom-Json | Format-List
Get-Content "params.json" | ConvertFrom-Json | Format-List
Get-Content "result.json" | ConvertFrom-Json | Format-List
Get-Content "stdout.log"
Get-Content "stderr.log"
```

**Expected structure:**
```
Runs/{runId}/
├── manifest.json       # Test case manifest
├── params.json         # Resolved parameters (secrets redacted)
├── env.json            # Environment snapshot (secrets redacted)
├── result.json         # Status, exitCode, duration
├── stdout.log          # PowerShell stdout
├── stderr.log          # PowerShell stderr
└── artifacts/          # Test case artifacts
    └── report.json
```

### Step 5: Verify Runner Injection

Check that runner-injected environment variables are available to the script:

```powershell
# These should be logged in stdout.log or used by the script
Get-Content "stdout.log" | Select-String "PVTX_"
```

**Runner-injected variables:**
- `PVTX_TESTCASE_PATH`
- `PVTX_TESTCASE_NAME`
- `PVTX_TESTCASE_ID`
- `PVTX_TESTCASE_VER`
- `PVTX_ASSETS_ROOT`
- `PVTX_MODULES_ROOT`
- `PVTX_RUN_ID`
- `PVTX_PHASE`
- `PVTX_CONTROL_DIR`

### Step 6: Test Timeout Handling (Optional)

If the test case has a timeout defined in `test.manifest.json`, test that the runner enforces it:

```powershell
# If timeout is 30 seconds, the runner should abort the test case if it runs longer
dotnet run --project src/PcTest.Cli -- run `
  --target testcase `
  --id "<case-id>@<version>" `
  --inputs '{"DurationSec": 60}'  # Longer than timeout
```

Check `result.json` for `status: "TimedOut"` or similar.

### Step 7: Test Secret Redaction (If Applicable)

If the test case accepts secret parameters (with `EnvRef` and `secret: true`), verify redaction:

```powershell
# Set environment variable
$env:SECRET_API_KEY = "super-secret-value"

dotnet run --project src/PcTest.Cli -- run `
  --target testcase `
  --id "<case-id>@<version>" `
  --inputs '{"ApiKey": {"$env": "SECRET_API_KEY", "secret": true}}'

# Check params.json - value should be redacted
Get-Content "Runs/{runId}/params.json" | Select-String "REDACTED"
```

---

## Testing Checklist

Before considering a test case ready:

### Direct PowerShell Execution
- [ ] Modules imported correctly (`$env:PSModulePath` configured)
- [ ] Runs successfully with default parameters
- [ ] Runs successfully with all example parameters from README
- [ ] Handles invalid parameters gracefully (exit code ≥2)
- [ ] Generates `artifacts/report.json` in all scenarios
- [ ] Exit code 0 for pass scenarios
- [ ] Exit code 1 for expected fail scenarios
- [ ] Exit code ≥2 for script/environment errors
- [ ] Optional artifacts (`raw/`, `attachments/`) generated only when needed

### CLI Integration
- [ ] Test case discovered successfully
- [ ] Runs via CLI with default parameters
- [ ] Runs via CLI with custom parameters
- [ ] Case Run Folder structure is correct
- [ ] `manifest.json`, `params.json`, `env.json`, `result.json` generated
- [ ] `stdout.log` and `stderr.log` captured
- [ ] Artifacts copied to Case Run Folder
- [ ] Runner-injected environment variables work
- [ ] Timeout handling works (if applicable)
- [ ] Secrets redacted properly (if applicable)

---

## Common Issues and Fixes

### Issue: "Module not found" when running PowerShell directly

**Cause:** Shared modules not in `PSModulePath`.

**Fix:**
```powershell
$env:PSModulePath = "$(Resolve-Path 'assets/PowerShell/Modules');$env:PSModulePath"
```

### Issue: Test case not discovered by CLI

**Cause:** `test.manifest.json` is invalid or in wrong location.

**Fix:** Validate JSON structure and ensure it's in `assets/TestCases/<case-id@version>/test.manifest.json`.

### Issue: Exit code is always 0 even when test fails

**Cause:** Script doesn't set exit code explicitly.

**Fix:** Ensure script ends with `exit 0` (pass) or `exit 1` (fail) or `exit 2+` (error).

### Issue: Artifacts not generated

**Cause:** Script error before artifact creation, or missing directory.

**Fix:** Wrap artifact creation in `try/catch` and ensure `artifacts/` directory is created:
```powershell
$artifactsDir = "artifacts"
if (-not (Test-Path $artifactsDir)) {
    New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
}
```

### Issue: CLI run fails with "Parameter type conversion failed"

**Cause:** JSON input doesn't match parameter type in manifest.

**Fix:** Ensure JSON types match manifest (e.g., `"MinCores": 4` not `"MinCores": "4"`).

---

## Best Practices

1. **Always test both phases** - Direct PowerShell AND CLI integration
2. **Test multiple parameter combinations** - Don't just test defaults
3. **Test failure scenarios** - Ensure failures are handled gracefully
4. **Verify artifacts** - Don't just check exit codes; inspect `report.json` content
5. **Clean up between runs** - Delete `artifacts/` directory between tests to ensure fresh state
6. **Use descriptive test scenarios** - Name your test variations clearly
7. **Document test results** - Keep notes on what worked and what didn't

---

## Example Testing Session

Here's a complete example of testing a new CPU test case:

```powershell
# Setup
cd c:\Dev\PVT-X
$env:PSModulePath = "$(Resolve-Path 'assets/PowerShell/Modules');$env:PSModulePath"

# Phase 1: Direct PowerShell Testing
cd assets/TestCases/case.hw.cpu.core.params_verify@1.0.0

# Test 1: Default parameters
Remove-Item -Path artifacts -Recurse -Force -ErrorAction SilentlyContinue
pwsh -File .\run.ps1
Write-Host "Exit code: $LASTEXITCODE"
Get-Content artifacts/report.json | ConvertFrom-Json | Format-List

# Test 2: Custom valid parameters
Remove-Item -Path artifacts -Recurse -Force -ErrorAction SilentlyContinue
pwsh -File .\run.ps1 -MinPhysicalCores 2 -MinLogicalProcessors 4
Write-Host "Exit code: $LASTEXITCODE"

# Test 3: Failure scenario (unrealistic requirements)
Remove-Item -Path artifacts -Recurse -Force -ErrorAction SilentlyContinue
pwsh -File .\run.ps1 -MinPhysicalCores 999
Write-Host "Exit code: $LASTEXITCODE (should be 1)"

# Phase 2: CLI Integration Testing
cd c:\Dev\PVT-X

# Discover
dotnet run --project src/PcTest.Cli -- discover | Select-String "cpu.core.params_verify"

# Run with defaults
dotnet run --project src/PcTest.Cli -- run `
  --target testcase `
  --id "case.hw.cpu.core.params_verify@1.0.0"

# Inspect results
$latestRun = Get-ChildItem "Runs/" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Get-Content "$($latestRun.FullName)/result.json" | ConvertFrom-Json | Format-List
Get-Content "$($latestRun.FullName)/artifacts/report.json" | ConvertFrom-Json | Format-List
```

---

## Additional Resources

- Test case authoring rules: `docs/pvtx-test-authoring_rules.md`
- Core spec (runner behavior): `docs/pvtx-core-spec.md`
- CLI documentation: `README.md`
