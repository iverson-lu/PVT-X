# Event View Check

## Purpose
Validate PC stability and health by checking Windows **System** and **Application** Event Logs within a relative time window. The test fails immediately on **Blocklist** matches, or fails when the remaining event count (after Allowlist exclusion) exceeds a simple threshold.

## Test Logic
1. Read events from `System` and `Application` within the last `WindowMinutes`.
2. Optionally filter events by minimum severity (`MinLevel`).
3. Evaluate rules with priority:
   - **Blocklist first**: any match causes **FAIL**.
   - **Allowlist next**: matching events are excluded from threshold counting.
4. Count the remaining events (threshold pool). If `poolCount >= FailThreshold`, the test **FAILS**; otherwise **PASS**.
5. Always write `artifacts/report.json` and `artifacts/events_summary.csv`. Optionally write `artifacts/events_detail.csv`.

## Parameters
- **WindowMinutes**: Lookback window in minutes.
- **LogNames**: JSON array of logs to query. Only `["System","Application"]` is supported.
- **MinLevel**: Minimum severity to include (`Critical`, `Error`, `Warning`, `Information`, `None`).
- **AllowlistCsv**: Allowlist rules CSV path (relative to the test case directory by default).
- **BlocklistCsv**: Blocklist rules CSV path (relative to the test case directory by default).
- **FailThreshold**: Fail if the threshold pool count is >= this value.
- **MaxEventsPerLog**: Maximum number of events read per log.
- **TruncateMessageChars**: Max message length written to outputs (0 = no truncation).
- **CaptureEventsToFile**: `Enable` writes `artifacts/events_detail.csv` with per-event details.

## How to Run Manually
From the case directory:
```powershell
pwsh -File .\run.ps1
```

Override parameters (example):
```powershell
pwsh -File .\run.ps1 -WindowMinutes 120 -MinLevel Error -FailThreshold 5 -CaptureEventsToFile Enable
```

## Expected Result
- **PASS (exit 0)**: No Blocklist match and `threshold_pool_count < FailThreshold`.
- **FAIL (exit 1)**: Any Blocklist match, or `threshold_pool_count >= FailThreshold`.
- **ERROR (exit >= 2)**: Script/environment error (e.g., invalid parameters, CSV parse error).
Artifacts are written to `artifacts/`:
- `report.json` (required)
- `events_summary.csv` (always)
- `events_detail.csv` (only when `CaptureEventsToFile=Enable`)
