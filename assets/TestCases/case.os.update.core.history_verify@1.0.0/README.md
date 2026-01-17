# Windows Update History Verification

## Purpose
Validates that Windows Update history contains at least a minimum number of update records.

## Test Logic
- Uses Windows Update COM automation (`Microsoft.Update.Session`) to query update history
- Retrieves all update history entries
- Counts total number of updates
- Compares against `MinExpectedCount` parameter
- Pass if update count >= minimum
- Fail if update count < minimum

## Parameters
- **MinExpectedCount** (int, required, default: 1, min: 0): Minimum number of update records expected in Windows Update history.

## How to Run Manually
```powershell
pwsh ./run.ps1 -MinExpectedCount 5
```

## Expected Result
- **Success**: Windows Update history contains at least the minimum number of updates. Exit code 0.
- **Failure**: Windows Update history contains fewer updates than required. Exit code 1.
