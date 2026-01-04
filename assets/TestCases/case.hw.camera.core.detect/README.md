# Camera Verification

## Purpose
Validates camera count and name substrings using PnP device discovery.

## Test Logic
- Uses `Get-PnpDevice` with Class "Camera" to discover camera devices
- Counts the number of detected cameras
- Validates against `MinExpectedCount` parameter
- If `NameContains` is specified, checks that at least one camera name contains the substring (case-insensitive)
- Pass if count >= minimum and name check passes (if required)
- Fail if count < minimum or name check fails

## Parameters
- **MinExpectedCount** (int, required, default: 1): Minimum number of cameras expected to be detected
- **NameContains** (string, optional, default: ""): String that at least one camera name should contain. Empty means no name validation.

## How to Run Manually
```powershell
pwsh ./run.ps1 -MinExpectedCount 2 -NameContains "HD"
```

## Expected Result
- **Success**: Camera count meets or exceeds minimum, and name validation passes (if required). Exit code 0.
- **Failure**: Camera count below minimum or name validation fails. Exit code 1.
