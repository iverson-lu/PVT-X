# Network Ping Connectivity

## Purpose
Checks basic network reachability by pinging a target address (ICMP).

## Test Logic
- Uses `Test-Connection` cmdlet to ping the target address
- If `MaxReplyTimeMs` > 0, validates that reply time is within threshold
- Pass if ping succeeds and reply time is acceptable (if threshold specified)
- Fail if ping fails (DNS failure, timeout, unreachable, ICMP blocked) or reply time exceeds threshold

## Parameters
- **TargetAddress** (string, required, default: "www.microsoft.com"): Target address to ping (hostname or IP).
- **MaxReplyTimeMs** (int, optional, default: 0): Optional maximum allowed reply time (ms). Use 0 to disable this threshold.

## How to Run Manually
```powershell
# Default target
pwsh .\run.ps1

# Custom target
pwsh .\run.ps1 -TargetAddress "8.8.8.8"

# Enforce a maximum reply time threshold (ms)
pwsh .\run.ps1 -TargetAddress "www.microsoft.com" -MaxReplyTimeMs 200
```

## Expected Result
- **Success**: Target is reachable and reply time within threshold (if specified). Exit code 0.
- **Failure**: Target unreachable or reply time exceeds threshold. Exit code 1.
- **Error**: Script encounters unhandled runtime error. Exit code 2.
