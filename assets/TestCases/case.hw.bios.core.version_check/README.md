# BIOS Version Check

## Purpose
Validates that the system BIOS version contains the specified string.

## Test Logic
- Queries system BIOS version using WMI (Win32_BIOS)
- If `VersionContains` parameter is provided (non-empty), checks if the BIOS version string contains the specified substring (case-insensitive)
- Pass if the version matches (or if no validation string is provided)
- Fail if the version does not contain the expected substring

## Parameters
- **VersionContains** (string, optional, default: ""): String that BIOS version should contain. If empty, no validation is performed and the test passes after collecting BIOS information.

## How to Run Manually
```powershell
pwsh ./run.ps1 -VersionContains "A15"
```

## Expected Result
- **Success**: BIOS version contains the specified string (or no validation required). Exit code 0.
- **Failure**: BIOS version does not contain the specified string. Exit code 1.
