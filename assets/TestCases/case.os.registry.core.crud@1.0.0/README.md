# Windows Registry CRUD

## Purpose
Provides unified Windows registry operations supporting registry file execution and value verification for PC test automation scenarios.

## Test Logic
- **Mode: set_reg** – Applies registry changes (add/update/delete) by executing a `.reg` file using native Windows registry import
  - Validates file existence and extension
  - Automatically copies the `.reg` file to artifacts for audit
  - Executes registry import via `reg.exe import`
  - Optionally exports registry snapshots as artifacts
  - Pass/Fail determined by import success
  
- **Mode: verify_reg** – Verifies registry values exist and optionally match expected content
  - Reads registry values using PowerShell `Get-ItemProperty`
  - Validates existence, type, and data based on verification spec
  - Supports multiple match modes: exact, case_insensitive, contains, regex
  - Optionally captures actual values in artifacts
  - Pass/Fail determined by all verifications passing

- Uses shared `Pvtx.Core` module for structured output and artifact management
- Registry write operations are performed only via `.reg` files
- Registry verification is read-only

## Parameters
- `Mode` (enum, required): Operation mode – `set_reg` to apply registry changes (add/update/delete), `verify_reg` to check values
- `RegFilePath` (path, optional): Path to `.reg` file to execute (required when Mode=set_reg)
- `ArtifactExportScope` (json, optional): Registry export configuration: `{"enabled": true, "keys": ["HKLM\\Software\\Example"], "format": "reg"}`. Supported in both set_reg and verify_reg modes
- `VerifySpec` (json, optional, default `[]`): Array of registry value verification specs (required when Mode=verify_reg): `[{"path": "HKLM\\Software\\Example", "name": "Version", "expected": {"type": "String", "data": "1.0"}}]`. Actual registry values are always included in report.json results

## How to Run Manually

### Set Registry (Add/Update/Delete)
```powershell
pwsh ./run.ps1 -Mode set_reg -RegFilePath "C:\tests\enable_feature.reg"
```

### Verify Registry Values
```powershell
pwsh ./run.ps1 -Mode verify_reg -VerifySpec '[{"path":"HKLM\\Software\\Microsoft\\Windows\\CurrentVersion","name":"ProgramFilesDir","expected":{"type":"String"}}]'
```

## Expected Result
- Success: All operations complete successfully, `report.json` generated, exit code 0
- Failure modes:
  - **set_reg**: File not found, invalid format, registry import fails
  - **verify_reg**: Registry key/value not found, access denied, type mismatch, data mismatch
  
Output includes structured steps with metrics, execution details, and machine-readable summary.
