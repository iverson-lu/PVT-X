# Event Viewer Level Check

## Purpose
Checks the most recent N events in Event Viewer to ensure none have a specified severity level.

## Test Logic
- Uses `Get-WinEvent` to retrieve recent events from specified log
- Queries the most recent `EventCount` events
- Filters for events matching `ProhibitedLevel` severity
- Pass if no events with prohibited level are found
- Fail if any events with prohibited level are detected

## Parameters
- **EventCount** (int, optional, default: 100, range: 1-10000): Number of recent events to check
- **ProhibitedLevel** (enum, optional, default: "Error"): Event level to check for ("Critical", "Error", "Warning", "Information", "Verbose")
- **LogName** (string, optional, default: "System"): Event log name to check ("System", "Application", "Security", etc.)

## How to Run Manually
```powershell
pwsh ./run.ps1 -EventCount 50 -ProhibitedLevel "Critical" -LogName "Application"
```

## Expected Result
- **Success**: No events with prohibited severity level found in recent events. Exit code 0.
- **Failure**: One or more events with prohibited severity level detected. Exit code 1.
