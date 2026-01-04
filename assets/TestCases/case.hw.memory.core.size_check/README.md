# Memory Check Test

## Purpose
Checks available system memory against minimum requirements.

## Test Logic
- Queries system memory using WMI (Win32_OperatingSystem)
- Retrieves free physical memory in MB
- Compares against `MinMemoryMB` parameter
- Pass if available memory >= minimum requirement
- Fail if available memory < minimum requirement

## Parameters
- **MinMemoryMB** (int, optional, default: 100, range: 1-999999): Minimum required available memory in megabytes.

## How to Run Manually
```powershell
pwsh ./run.ps1 -MinMemoryMB 2048
```

## Expected Result
- **Success**: Available memory meets or exceeds minimum requirement. Exit code 0.
- **Failure**: Available memory below minimum requirement. Exit code 1.
