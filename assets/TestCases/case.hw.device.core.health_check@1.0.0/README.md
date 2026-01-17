# Device Health Check

## Purpose
Validates that devices in Device Manager have no errors or warnings.

## Test Logic
- Uses `Get-PnpDevice` to query all devices
- Checks for devices with error/problem status codes
- In normal mode: only fails if problem devices exist
- In restricted mode: fails if any devices are disabled or have problems
- Pass if all validations pass
- Fail if problem devices found (or disabled devices in restricted mode)

## Parameters
- **RestrictedMode** (boolean, optional, default: false): If true, all devices must be enabled and working; if false, only fail on problem devices.

## How to Run Manually
```powershell
pwsh ./run.ps1 -RestrictedMode $true
```

## Expected Result
- **Success**: No problem devices found (and no disabled devices in restricted mode). Exit code 0.
- **Failure**: Problem devices detected, or disabled devices found in restricted mode. Exit code 1.
