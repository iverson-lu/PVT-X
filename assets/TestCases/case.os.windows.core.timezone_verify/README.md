# Timezone Verification

## Purpose
Validates that the system timezone matches the expected UTC offset.

## Test Logic
- Retrieves current system timezone using `[System.TimeZoneInfo]::Local`
- Extracts current UTC offset (accounting for DST if applicable)
- Compares against `ExpectedUtcOffset` parameter
- Pass if offsets match (or if parameter is empty)
- Fail if offsets don't match

## Parameters
- **ExpectedUtcOffset** (string, optional, default: "+08:00"): Expected UTC offset in format '+HH:MM' or '-HH:MM' (e.g., +08:00 for UTC+8). Leave empty to skip validation.

## How to Run Manually
```powershell
# UTC+8 (Beijing, Singapore, Hong Kong)
pwsh ./run.ps1 -ExpectedUtcOffset "+08:00"

# UTC (Greenwich Mean Time)
pwsh ./run.ps1 -ExpectedUtcOffset "+00:00"

# Eastern Time (US)
pwsh ./run.ps1 -ExpectedUtcOffset "-05:00"
```

## Expected Result
- **Success**: Timezone UTC offset matches expected value or validation skipped (empty parameter). Exit code 0.
- **Failure**: Timezone UTC offset mismatch. Exit code 1.
- User privilege
