# Storage Minimum Disk Count and Capacity Check

## Purpose
Validate that the system storage configuration meets minimum requirements:
- At least a minimum number of eligible storage disks are present.
- Each eligible disk has a capacity greater than or equal to the required minimum (GB).

## Test Logic
- Enumerates storage disks using `Get-PhysicalDisk` when available, otherwise falls back to `Get-Disk`.
- Applies eligibility filtering based on `E_Scope`:
  - `internal`: excludes typical removable/external bus types (for example USB/SD/MMC).
  - `all`: includes all discovered disks.
- When `B_IgnoreVirtual` is `true`, excludes disks that appear to be virtual (for example BusType=`Virtual` or FriendlyName hints).
- Fails if:
  - Eligible disk count is less than `MinimumDiskCount`, or
  - Any eligible disk is smaller than `MinimumDiskSizeGB`.

## Parameters
- **MinimumDiskCount** (int, default: 1): Minimum number of eligible disks required.
- **MinimumDiskSizeGB** (int, default: 64): Minimum capacity required for each eligible disk (GB).
- **E_Scope** (enum, default: internal): Disk scope to evaluate (`internal` or `all`).
- **B_IgnoreVirtual** (boolean, default: true): Exclude disks that appear to be virtual.

## How to Run Manually
```powershell
pwsh .\run.ps1 -MinimumDiskCount 1 -MinimumDiskSizeGB 64 -E_Scope internal -B_IgnoreVirtual $true
echo $LASTEXITCODE
```

## Expected Result
- **Pass (exit code 0)**: Eligible disk count is >= `MinimumDiskCount` and every eligible disk is >= `MinimumDiskSizeGB`.
- **Fail (exit code 1)**: Disk count is too low or one or more eligible disks are undersized.
- **Error (exit code >= 2)**: The script encountered an unexpected runtime/environment error.
